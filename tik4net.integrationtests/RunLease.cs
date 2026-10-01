// RunLease.cs — who owns a test row on the router, and whether that owner is still running.
//
// Each test process (one leg) draws a short run tag at start and holds a lease file for as long as it lives.
// A row created under TestNames.Unique carries the tag, so the orphan cleaner can tell a row whose run died
// (lease free — remove it) from a row another leg is using right now (lease held — leave it alone). Without
// that, every leg's start-up sweep deleted the rows the other legs had just created.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace tik4net.integrationtests
{
    /// <summary>This process's run tag and its lease; and the leases of every other run on this machine.</summary>
    public static class RunLease
    {
        private static readonly string LeaseDirectory = Path.Combine(FileLock.Root, "runs");
        private static readonly object Sync = new object();
        private static FileStream _lease;

        /// <summary>
        /// The run tag: <c>u</c> and five base-36 characters, drawn once per process. It is what
        /// <see cref="TestNames.Unique"/> appends and <see cref="TagPattern"/> finds.
        /// </summary>
        public static string Tag { get; } = DrawTag();

        /// <summary>Finds a run tag in a row's name or comment; group 1 is the tag.</summary>
        public static readonly Regex TagPattern = new Regex(@"(?<![0-9a-z])(u[0-9a-z]{5})(?![0-9a-z])", RegexOptions.CultureInvariant);

        private static string DrawTag()
        {
            const string alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
            var random = new Random(Guid.NewGuid().GetHashCode());
            var chars = new char[6];
            chars[0] = 'u';
            for (int i = 1; i < chars.Length; i++)
                chars[i] = alphabet[random.Next(alphabet.Length)];
            return new string(chars);
        }

        /// <summary>
        /// Takes this process's lease, recording which leg and router it runs. Idempotent. The lease is held until
        /// the process exits; the OS releases it even when the process is killed.
        /// </summary>
        public static void Take(string leg, string routerHost)
        {
            lock (Sync)
            {
                if (_lease != null)
                    return;
                Directory.CreateDirectory(LeaseDirectory);
                // Read shared, so another run can read who holds it; write not, so a liveness probe asking for write
                // access fails while this process lives.
                _lease = new FileStream(PathFor(Tag), FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
                var text = System.Text.Encoding.UTF8.GetBytes(
                    $"router={routerHost}\nleg={leg}\npid={Process.GetCurrentProcess().Id}\nstarted={DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
                _lease.Write(text, 0, text.Length);
                _lease.Flush();
            }
        }

        /// <summary>
        /// True while the run holding <paramref name="tag"/> is alive. A tag with no lease file at all counts as dead:
        /// its run finished, and a later sweep removed the file.
        /// </summary>
        public static bool IsAlive(string tag)
        {
            if (string.Equals(tag, Tag, StringComparison.Ordinal))
                return true;
            string path = PathFor(tag);
            if (!File.Exists(path))
                return false;
            using (var probe = FileLock.TryOpen(path, FileAccess.ReadWrite, FileShare.ReadWrite))
                return probe == null;
        }

        /// <summary>The tags of the other live runs against <paramref name="routerHost"/>.</summary>
        public static List<string> OtherLiveRuns(string routerHost)
        {
            var live = new List<string>();
            if (!Directory.Exists(LeaseDirectory))
                return live;
            foreach (string file in Directory.GetFiles(LeaseDirectory, "*.lease"))
            {
                string tag = Path.GetFileNameWithoutExtension(file);
                if (tag == Tag || !IsAlive(tag))
                    continue;
                if (string.Equals(ReadRouter(file), routerHost, StringComparison.OrdinalIgnoreCase))
                    live.Add(tag);
            }
            return live;
        }

        /// <summary>Deletes the lease files of runs that have ended. Best effort.</summary>
        public static void SweepDeadLeases()
        {
            if (!Directory.Exists(LeaseDirectory))
                return;
            foreach (string file in Directory.GetFiles(LeaseDirectory, "*.lease"))
            {
                string tag = Path.GetFileNameWithoutExtension(file);
                if (tag == Tag || IsAlive(tag))
                    continue;
                try { File.Delete(file); }
                catch (IOException) { /* taken again meanwhile, or still closing */ }
                catch (UnauthorizedAccessException) { /* same */ }
            }
        }

        private static string ReadRouter(string file)
        {
            try
            {
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                        if (line.StartsWith("router=", StringComparison.Ordinal))
                            return line.Substring("router=".Length);
                }
            }
            catch (IOException) { /* unreadable: treat as another router's */ }
            return null;
        }

        private static string PathFor(string tag) => Path.Combine(LeaseDirectory, tag + ".lease");
    }

    /// <summary>Names for the rows a test creates, unique to the run that creates them.</summary>
    public static class TestNames
    {
        /// <summary>
        /// <paramref name="prefix"/> with this run's tag appended (<c>t4n-bond</c> → <c>t4n-bond-u3k9xa</c>). Keep a
        /// marker prefix the orphan cleaner knows (<c>t4n</c>, <c>test-</c>, <c>tik4net-</c>): the tag says whose row
        /// it is, the prefix says it is a test row at all.
        /// </summary>
        public static string Unique(string prefix) => prefix + "-" + RunLease.Tag;

        // Prefixes the suite stamps on every object it creates (on name or comment). On the dedicated test
        // router nothing else carries these, so a prefix match is a safe "this is ours".
        private static readonly string[] TestMarkers =
        {
            "t4n", "test-", "TEST", "tik4net-", "User for TEST",
        };

        /// <summary>
        /// True when <paramref name="value"/> (a name or comment) carries one of the prefixes the suite stamps on what
        /// it creates. The orphan sweep removes by it; a test reading a whole table skips such rows, because another
        /// leg may be creating or deleting them while it reads.
        /// </summary>
        public static bool IsTestRowName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            foreach (string marker in TestMarkers)
                if (value.StartsWith(marker, StringComparison.Ordinal))
                    return true;
            return false;
        }
    }
}
