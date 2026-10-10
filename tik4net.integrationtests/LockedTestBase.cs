using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tik4net.integrationtests
{
    /// <summary>Test categories the run script acts on.</summary>
    public static class TestCategories
    {
        /// <summary>
        /// A class that never reads the leg's transport (<c>tik.connectionType</c>): it picks its transports itself,
        /// or needs none of the suite's. It measures the same thing in every leg, so the run script runs it in the
        /// first leg only — RomonRelayTest alone is almost six minutes, eleven times over in a full matrix.
        /// </summary>
        public const string LegIndependent = "LegIndependent";

        /// <summary>
        /// A test that holds on any lab router, not only the provisioned one. The secondary routers (CHR2, CHR3) carry
        /// none of its topology, so the run script sends them only this category.
        /// </summary>
        public const string AnyRouter = "AnyRouter";
    }

    /// <summary>
    /// The base of every test class in the suite: holds the test's locks (<see cref="TestLockAttribute"/>) from
    /// TestInitialize to TestCleanup, so that legs and routers can be run at the same time.
    /// </summary>
    /// <remarks>
    /// <para>What a test holds without asking: the whole run, its router and its leg <b>shared</b>, and its own
    /// name <b>exclusive</b> — the same test in two legs never overlaps, because it creates the same fixed-name
    /// rows in both. A test of a MAC-layer leg also holds <see cref="TestLockAttribute.MacLayer"/>.
    /// <see cref="TestLockAttribute"/> adds to that; see there for which lock to choose.</para>
    /// <para>MSTest runs a base class's TestInitialize before the derived class's and its TestCleanup after, so
    /// the locks cover the whole of a derived class's set-up and teardown.</para>
    /// <para>With the <c>TIK4NET_LOCK_ALL</c> environment variable (or the <c>tik.lockAll</c> run parameter) set to
    /// <c>1</c>, every test takes the global lock exclusive — the whole machine runs one test at a time, which is
    /// the way to tell whether a failure is a collision between parallel tests.</para>
    /// </remarks>
    public class LockedTestBase
    {
        /// <summary>The environment variable that serialises every test on the machine.</summary>
        public const string LockAllVariable = "TIK4NET_LOCK_ALL";

        private TestLockSet _locks;

        // The set the running test holds, per process (a leg runs one test at a time). When a derived class's
        // TestCleanup throws, MSTest does not run the base class's (measured on MSTest 3.7.3), so ReleaseTestLocks
        // is skipped and the test's locks stay held until the process ends. Held shared, they make the next
        // Router-exclusive test in any leg wait for good — and a second leg then waits behind that one. So each test
        // first lets go of whatever the previous one in this process still holds.
        private static TestLockSet _heldInThisProcess;
        private static readonly object HeldSync = new object();

        /// <summary>MSTest injects this for access to runsettings parameters.</summary>
        public TestContext TestContext { get; set; }

        [TestInitialize]
        public void AcquireTestLocks()
        {
            lock (HeldSync)
            {
                if (_heldInThisProcess != null)
                {
                    _heldInThisProcess.Close();
                    Console.WriteLine("[lock] released the locks of the previous test: its TestCleanup failed before ours ran");
                }
                _locks = new TestLockSet(TestId);
                _heldInThisProcess = _locks;
            }
            // Timed here rather than summed from the locks, so that a wait which ends in a timeout is counted too.
            var waiting = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                _locks.Acquire(LocksForThisTest());
            }
            finally
            {
                _locks.TakeWaited();
                Console.WriteLine("[lock-wait-ms] " + waiting.ElapsedMilliseconds);
            }
        }

        [TestCleanup]
        public void ReleaseTestLocks()
        {
            lock (HeldSync)
            {
                _locks?.Close();
                if (ReferenceEquals(_heldInThisProcess, _locks))
                    _heldInThisProcess = null;
                _locks = null;
            }
        }

        private string TestId => (TestContext?.FullyQualifiedTestClassName ?? GetType().FullName) + "." + TestContext?.TestName;

        private IEnumerable<LockSpec> LocksForThisTest()
        {
            string host = RouterHost("host");
            bool lockAll = Environment.GetEnvironmentVariable(LockAllVariable) == "1"
                           || (TestContext?.Properties != null && "1".Equals(TestContext.Properties["tik.lockAll"] as string));

            yield return LockSpec.Global(lockAll);
            if (host != null)
            {
                yield return LockSpec.Router(host, false);
                if (!IsSafeInParallelLegs())
                    yield return LockSpec.Resource(host, "test:" + TestId);
                if (IsMacLayerLeg())
                    yield return LockSpec.Resource(host, TestLockAttribute.MacLayer);
            }
            yield return LockSpec.Leg(false);

            foreach (var attr in Attributes())
            {
                string lockHost = RouterHost(attr.RouterHostKey);
                if (attr.Resource != null)
                {
                    if (lockHost != null)
                        yield return LockSpec.Resource(lockHost, attr.Resource);
                    continue;
                }
                switch (attr.Scope.Value)
                {
                    case TestLockScope.Global:
                        yield return LockSpec.Global(true);
                        break;
                    case TestLockScope.Router:
                        if (lockHost != null)
                            yield return LockSpec.Router(lockHost, true);
                        break;
                    case TestLockScope.Leg:
                        yield return LockSpec.Leg(true);
                        break;
                }
            }
        }

        private IEnumerable<TestLockAttribute> Attributes()
        {
            var onClass = GetType().GetCustomAttributes<TestLockAttribute>(inherit: true);
            var onMethod = TestMethod()?.GetCustomAttributes<TestLockAttribute>(inherit: true) ?? Enumerable.Empty<TestLockAttribute>();
            return onClass.Concat(onMethod);
        }

        private bool IsSafeInParallelLegs()
            => GetType().IsDefined(typeof(SafeInParallelLegsAttribute), inherit: true)
               || (TestMethod()?.IsDefined(typeof(SafeInParallelLegsAttribute), inherit: true) ?? false);

        private bool IsMacLayerLeg()
        {
            string leg = TestContext?.Properties?["tik.connectionType"] as string;
            return "MacTelnet".Equals(leg, StringComparison.OrdinalIgnoreCase)
                   || "WinboxCliMac".Equals(leg, StringComparison.OrdinalIgnoreCase)
                   || "WinboxNativeMac".Equals(leg, StringComparison.OrdinalIgnoreCase);
        }

        private MethodInfo TestMethod()
            => TestContext?.TestName == null
                ? null
                : GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                           .FirstOrDefault(m => m.Name == TestContext.TestName);

        private static string RouterHost(string key)
        {
            string host = LabConfig.Get(key);
            return string.IsNullOrWhiteSpace(host) ? null : host.Trim();
        }

        /// <summary>
        /// Holds the router exclusively until the handle is disposed, for the part of a test that needs it. The
        /// test's locks are released and taken again in order, so another test may run in between; prefer
        /// <c>[TestLock(TestLockScope.Router)]</c> when the whole test needs it.
        /// </summary>
        protected IDisposable LockRouter() => Escalate(LockSpec.Router(RouterHost("host") ?? "unknown", true));

        /// <summary>Holds every test on the machine off until the handle is disposed; see <see cref="LockRouter"/>.</summary>
        protected IDisposable LockGlobal() => Escalate(LockSpec.Global(true));

        /// <summary>Holds the other tests of this leg off until the handle is disposed; see <see cref="LockRouter"/>.</summary>
        protected IDisposable LockLeg() => Escalate(LockSpec.Leg(true));

        /// <summary>Takes an exclusive lock on a named router resource until the handle is disposed; see <see cref="LockRouter"/>.</summary>
        protected IDisposable LockResource(string resource) => Escalate(LockSpec.Resource(RouterHost("host") ?? "unknown", resource));

        private IDisposable Escalate(LockSpec spec)
        {
            if (_locks == null)
                throw new InvalidOperationException("Test locks are taken in TestInitialize; there is no running test to escalate.");
            return _locks.Escalate(spec);
        }
    }
}
