// TestLocks.cs — the locks that let the suite run several legs (and several routers) at once.
//
// Every test holds a set of reader/writer locks from TestInitialize to TestCleanup (LockedTestBase). By default
// the set is all SHARED — the whole run, the router, the leg — plus one EXCLUSIVE lock on the test's own name,
// so the same test in two legs never runs at once (it would create the same fixed-name rows twice). A
// [TestLock] attribute turns one level exclusive, or adds an exclusive lock on a named router resource.
//
// The locks are files, not named mutexes: a Mutex belongs to the thread that took it, and a test that awaits
// may release on another thread; and a file lock is released by the OS when its process dies, so a killed
// leg can never wedge the others. Levels are always taken in one order (Global, Router, resource, Leg) and
// released together, which is what keeps two legs from deadlocking on each other.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace tik4net.integrationtests
{
    /// <summary>The level a <see cref="TestLockAttribute"/> makes exclusive.</summary>
    public enum TestLockScope
    {
        /// <summary>
        /// Every other test of the same leg (this process — one transport). Only matters with more than one MSTest
        /// worker; the other legs keep running.
        /// </summary>
        Leg,

        /// <summary>
        /// Every other test against the same router, in any leg and any process on this machine. For a test that
        /// changes state the whole router shares (services, identity, clock, safe mode, reboot), reads a whole table
        /// and compares it, or measures timing.
        /// </summary>
        Router,

        /// <summary>
        /// Every other test of every run on this machine, whatever router. For a test that talks to more than one
        /// router and cannot name them all with <see cref="TestLockAttribute.RouterHostKey"/>.
        /// </summary>
        Global,
    }

    /// <summary>
    /// Makes a level exclusive for the whole test (TestInitialize to TestCleanup, so the teardown of what the test
    /// created is covered too), or takes an exclusive lock on a named router resource. On a class it applies to
    /// every test in it; more than one may be given.
    /// </summary>
    /// <remarks>
    /// Choose the least that is enough. A resource lock (<c>[TestLock("ether2")]</c>, <c>[TestLock("ip-service")]</c>)
    /// only excludes tests that name the same resource, so it costs nothing to the rest of the run; a
    /// <see cref="TestLockScope.Router"/> lock waits for every test on the router to finish and holds all of them
    /// off. A test that creates rows under a name of its own (<see cref="TestNames.Unique"/>) needs neither.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class TestLockAttribute : Attribute
    {
        /// <summary>
        /// The resource every test of a MAC-layer leg (MAC-Telnet, WinBox-CLI-MAC, WinBox-native-MAC) holds without
        /// asking, and a test elsewhere names when it opens a MAC-layer connection of its own. While two clients
        /// talk to one router on the MAC layer at once, the router drops datagrams of its own output — MAC-Telnet
        /// reports the backlog, a WinBox-MAC login comes back without a session id.
        /// </summary>
        public const string MacLayer = "mac-layer";

        /// <summary>Makes <paramref name="scope"/> exclusive.</summary>
        public TestLockAttribute(TestLockScope scope) { Scope = scope; }

        /// <summary>Takes an exclusive lock on <paramref name="resource"/> of the router.</summary>
        public TestLockAttribute(string resource) { Resource = resource; }

        /// <summary>The exclusive level, or <c>null</c> for a resource lock.</summary>
        public TestLockScope? Scope { get; }

        /// <summary>The resource, or <c>null</c> for a level lock.</summary>
        public string Resource { get; }

        /// <summary>
        /// The <see cref="LabConfig"/> key naming the router the lock is on; default <c>host</c>, the router the run
        /// selected. A test that also writes to another lab router locks that one too, e.g.
        /// <c>[TestLock(TestLockScope.Router, RouterHostKey = "romonTargetHost")]</c>. A key with no value (the router
        /// is not configured) takes nothing.
        /// </summary>
        public string RouterHostKey { get; set; } = "host";
    }

    /// <summary>
    /// The test may run in several legs at the same moment: it creates nothing under a fixed name and changes nothing
    /// another test reads. It then skips the exclusive lock on its own name that every test takes by default.
    /// </summary>
    /// <remarks>
    /// That default lock is what makes the legs a pipeline — each leg runs a test only after the leg ahead of it has
    /// finished it — so a slow test holds every leg behind it for its whole duration, eleven times over. For a test
    /// that only waits (a timeout, a monitor, a read of a large table) that serialisation buys nothing.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class SafeInParallelLegsAttribute : Attribute
    {
    }

    /// <summary>One lock of a test's set: which, and whether shared or exclusive.</summary>
    internal readonly struct LockSpec
    {
        public LockSpec(int rank, string key, bool exclusive) { Rank = rank; Key = key; Exclusive = exclusive; }

        // The acquisition order. Everything is taken in ascending rank, then key, and that single order is what
        // makes a deadlock between two legs impossible.
        public const int GlobalRank = 0, RouterRank = 1, ResourceRank = 2, LegRank = 3;

        public int Rank { get; }
        public string Key { get; }
        public bool Exclusive { get; }

        public override string ToString() => Key + (Exclusive ? " (exclusive)" : " (shared)");

        public static LockSpec Global(bool exclusive) => new LockSpec(GlobalRank, "global", exclusive);
        public static LockSpec Router(string host, bool exclusive) => new LockSpec(RouterRank, "router@" + host, exclusive);
        public static LockSpec Resource(string host, string name) => new LockSpec(ResourceRank, "res@" + host + "@" + name, true);
        public static LockSpec Leg(bool exclusive) =>
            new LockSpec(LegRank, "leg@" + Process.GetCurrentProcess().Id, exclusive);

        /// <summary>Merges duplicates (exclusive wins) and sorts into acquisition order.</summary>
        public static List<LockSpec> Normalize(IEnumerable<LockSpec> specs)
            => specs.GroupBy(s => s.Key, StringComparer.Ordinal)
                    .Select(g => new LockSpec(g.First().Rank, g.Key, g.Any(s => s.Exclusive)))
                    .OrderBy(s => s.Rank).ThenBy(s => s.Key, StringComparer.Ordinal)
                    .ToList();
    }

    /// <summary>
    /// The locks one test holds, taken in order and released together; supports a temporary escalation from the
    /// body of a test (<see cref="LockedTestBase.LockRouter"/> and friends).
    /// </summary>
    internal sealed class TestLockSet
    {
        private readonly string _holder;
        private List<LockSpec> _specs = new List<LockSpec>();
        private readonly List<FileLock> _held = new List<FileLock>();

        public TestLockSet(string holder) { _holder = holder; }

        /// <summary>
        /// The time spent waiting for locks since the last <see cref="TakeWaited"/>. Reported per test as a
        /// <c>[lock-wait-ms]</c> line, which the run script subtracts from the test durations in the TRX: a TRX duration
        /// includes TestInitialize, where the wait happens.
        /// </summary>
        public TimeSpan Waited { get; private set; }

        /// <summary>Returns <see cref="Waited"/> and starts counting again.</summary>
        public TimeSpan TakeWaited()
        {
            var waited = Waited;
            Waited = TimeSpan.Zero;
            return waited;
        }

        public void Acquire(IEnumerable<LockSpec> specs)
        {
            _specs = LockSpec.Normalize(specs);
            AcquireCurrent();
        }

        public void Release()
        {
            for (int i = _held.Count - 1; i >= 0; i--)
                _held[i].Dispose();
            _held.Clear();
        }

        /// <summary>
        /// Adds <paramref name="extra"/> for the lifetime of the returned handle. Everything is released and taken
        /// again in order — escalating in place (shared → exclusive while holding the shared) would deadlock
        /// against a second leg doing the same — so the escalation is not atomic: another test can run in the gap.
        /// </summary>
        public IDisposable Escalate(LockSpec extra)
        {
            var original = _specs;
            Release();
            _specs = LockSpec.Normalize(original.Concat(new[] { extra }));
            AcquireCurrent();
            ReportWait();
            return new Restore(this, original);
        }

        /// <summary>
        /// Writes the wait since the last report as <c>[lock-wait-ms] n</c> into the test's output; a test's lines add up.
        /// </summary>
        public void ReportWait()
            => Console.WriteLine("[lock-wait-ms] " + (long)TakeWaited().TotalMilliseconds);

        private void AcquireCurrent()
        {
            try
            {
                foreach (var spec in _specs)
                {
                    var held = FileLock.Acquire(spec, _holder);
                    Waited += held.Waited;
                    _held.Add(held);
                }
            }
            catch
            {
                Release();
                throw;
            }
        }

        private sealed class Restore : IDisposable
        {
            private TestLockSet _set;
            private readonly List<LockSpec> _original;
            public Restore(TestLockSet set, List<LockSpec> original) { _set = set; _original = original; }

            public void Dispose()
            {
                var set = Interlocked.Exchange(ref _set, null);
                if (set == null) return;
                set.Release();
                set._specs = _original;
                set.AcquireCurrent();
                set.ReportWait();
            }
        }
    }

    /// <summary>
    /// A cross-process reader/writer lock on a file. Readers open the lock file sharing read, a writer opens it
    /// sharing nothing; a writer first takes an intent file, which new readers wait on, so a writer is not starved
    /// by a stream of readers. The OS drops both when the process dies.
    /// </summary>
    internal sealed class FileLock : IDisposable
    {
        /// <summary>Where the lock files live: one directory per machine, shared by every run on it.</summary>
        public static readonly string Root = Path.Combine(Path.GetTempPath(), "tik4net-testlocks");

        /// <summary>The environment variable overriding how long a lock is waited for, in minutes (default 20).</summary>
        public const string TimeoutVariable = "TIK4NET_LOCK_TIMEOUT_MINUTES";

        private const int PollMs = 50;
        private FileStream _stream;

        private FileLock(FileStream stream, TimeSpan waited) { _stream = stream; Waited = waited; }

        /// <summary>How long <see cref="Acquire"/> waited for this lock.</summary>
        public TimeSpan Waited { get; }

        public void Dispose() => Interlocked.Exchange(ref _stream, null)?.Dispose();

        public static FileLock Acquire(LockSpec spec, string holder)
        {
            Directory.CreateDirectory(Root);
            string basePath = Path.Combine(Root, FileNameFor(spec.Key));
            string lockPath = basePath + ".lock", intentPath = basePath + ".intent", ownerPath = basePath + ".owner";

            var started = Stopwatch.StartNew();
            var deadline = LockTimeout();
            FileStream intent = null;
            try
            {
                while (true)
                {
                    FileStream stream = null;
                    if (spec.Exclusive)
                    {
                        if (intent == null)
                            intent = TryOpen(intentPath, FileAccess.ReadWrite, FileShare.None);
                        if (intent != null)
                            stream = TryOpen(lockPath, FileAccess.ReadWrite, FileShare.None);
                    }
                    else if (!IsHeld(intentPath))
                    {
                        stream = TryOpen(lockPath, FileAccess.Read, FileShare.Read);
                    }

                    if (stream != null)
                    {
                        if (spec.Exclusive)
                            WriteOwner(ownerPath, holder);
                        if (started.Elapsed.TotalSeconds >= 1)
                            Console.WriteLine($"[lock] waited {started.Elapsed.TotalSeconds:0.0} s for {spec}");
                        return new FileLock(stream, started.Elapsed);
                    }

                    if (started.Elapsed > deadline)
                        throw new TimeoutException(
                            $"Waited {started.Elapsed.TotalMinutes:0.0} min for the test lock {spec}. "
                            + $"Its last exclusive holder: {ReadOwner(ownerPath)}. Lock files: {Root} "
                            + $"(the wait is set by {TimeoutVariable}).");
                    Thread.Sleep(PollMs);
                }
            }
            finally
            {
                intent?.Dispose();
            }
        }

        private static TimeSpan LockTimeout()
        {
            string raw = Environment.GetEnvironmentVariable(TimeoutVariable);
            return double.TryParse(raw, System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out double minutes) && minutes > 0
                ? TimeSpan.FromMinutes(minutes)
                : TimeSpan.FromMinutes(20);
        }

        /// <summary>Opens the file with the given sharing, or returns <c>null</c> when another handle conflicts.</summary>
        internal static FileStream TryOpen(string path, FileAccess access, FileShare share)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, access, share);
            }
            catch (IOException ex) when (IsSharingViolation(ex))
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                // A file being deleted at this moment (a dead lease swept by another run) — try again.
                return null;
            }
        }

        /// <summary>True when some handle holds the file without sharing read (a writer's intent).</summary>
        private static bool IsHeld(string path)
        {
            using (var probe = TryOpen(path, FileAccess.Read, FileShare.ReadWrite))
                return probe == null;
        }

        internal static bool IsSharingViolation(IOException ex)
        {
            int code = Marshal.GetHRForException(ex) & 0xFFFF;
            return code == 32 || code == 33;   // ERROR_SHARING_VIOLATION, ERROR_LOCK_VIOLATION
        }

        private static void WriteOwner(string path, string holder)
        {
            try
            {
                File.WriteAllText(path, $"{holder} (pid {Process.GetCurrentProcess().Id}, since {DateTime.Now:HH:mm:ss})");
            }
            catch (IOException) { /* diagnostics only */ }
            catch (UnauthorizedAccessException) { /* diagnostics only */ }
        }

        private static string ReadOwner(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : "unknown"; }
            catch (IOException) { return "unknown"; }
        }

        /// <summary>A file name for a lock key: readable, and short enough for any path limit.</summary>
        internal static string FileNameFor(string key)
        {
            var sb = new StringBuilder(key.Length);
            foreach (char c in key)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '.' || c == '@' ? c : '_');
            string name = sb.ToString();
            if (name.Length <= 120)
                return name;

            using (var sha = SHA1.Create())
            {
                string hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "").Substring(0, 12);
                return name.Substring(0, 100) + "_" + hash;
            }
        }
    }
}
