using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.integrationtests;

namespace tik4net.unittests.IntegrationSuite
{
    /// <summary>
    /// The locks and leases that let the integration suite run its legs in parallel (TestLocks.cs, RunLease.cs in
    /// tik4net.integrationtests, compiled into this project as links). Router-free: they are files.
    /// </summary>
    /// <remarks>
    /// Windows only. The suite is net48 and runs on Windows; on Linux a <see cref="FileShare"/> mode is advisory, so
    /// these would measure a different mechanism than the one the suite relies on.
    /// </remarks>
    [TestClass]
    [DoNotParallelize]   // one test sets TIK4NET_LOCK_TIMEOUT_MINUTES, which is process-wide
    public class TestLockTests
    {
        private static readonly TimeSpan NotYet = TimeSpan.FromMilliseconds(400);
        private static readonly TimeSpan Soon = TimeSpan.FromSeconds(5);

        [TestInitialize]
        public void WindowsOnly()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Assert.Inconclusive("FileShare is advisory outside Windows; the suite these locks serve runs on Windows.");
        }

        private static LockSpec Key(bool exclusive, string key) => new LockSpec(LockSpec.ResourceRank, key, exclusive);
        private static string NewKey() => "unittest@" + Guid.NewGuid().ToString("N");

        private static Task<FileLock> AcquireAsync(LockSpec spec, string holder = "test")
            => Task.Run(() => FileLock.Acquire(spec, holder));

        [TestMethod]
        public void TwoReadersHoldTheSameLockAtOnce()
        {
            string key = NewKey();
            using (FileLock.Acquire(Key(false, key), "a"))
            {
                var second = AcquireAsync(Key(false, key));
                Assert.IsTrue(second.Wait(Soon), "a second reader must not wait for the first");
                second.Result.Dispose();
            }
        }

        [TestMethod]
        public void AWriterWaitsForTheReaderAndThenGetsIn()
        {
            string key = NewKey();
            var reader = FileLock.Acquire(Key(false, key), "reader");
            var writer = AcquireAsync(Key(true, key));
            Assert.IsFalse(writer.Wait(NotYet), "the writer got in while a reader held the lock");

            reader.Dispose();
            Assert.IsTrue(writer.Wait(Soon), "the writer did not get in after the reader left");
            writer.Result.Dispose();
        }

        [TestMethod]
        public void AReaderWaitsForTheWriter()
        {
            string key = NewKey();
            var writer = FileLock.Acquire(Key(true, key), "writer");
            var reader = AcquireAsync(Key(false, key));
            Assert.IsFalse(reader.Wait(NotYet), "a reader got in while the writer held the lock");

            writer.Dispose();
            Assert.IsTrue(reader.Wait(Soon));
            reader.Result.Dispose();
        }

        /// <summary>
        /// A writer queued behind a reader holds new readers off — otherwise legs that start a test every few
        /// seconds would keep a Router-exclusive test waiting for the whole run.
        /// </summary>
        [TestMethod]
        public void AWaitingWriterHoldsNewReadersOff()
        {
            string key = NewKey();
            var first = FileLock.Acquire(Key(false, key), "first reader");
            var writer = AcquireAsync(Key(true, key));
            Assert.IsFalse(writer.Wait(NotYet));

            var late = AcquireAsync(Key(false, key));
            Assert.IsFalse(late.Wait(NotYet), "a reader overtook the waiting writer");

            first.Dispose();
            Assert.IsTrue(writer.Wait(Soon), "the writer did not get in");
            Assert.IsFalse(late.Wait(NotYet), "the late reader got in beside the writer");

            writer.Result.Dispose();
            Assert.IsTrue(late.Wait(Soon));
            late.Result.Dispose();
        }

        [TestMethod]
        public void ALockTimeoutNamesTheHolder()
        {
            string key = NewKey();
            string previous = Environment.GetEnvironmentVariable(FileLock.TimeoutVariable);
            Environment.SetEnvironmentVariable(FileLock.TimeoutVariable, "0.005");   // 0.3 s
            try
            {
                using (FileLock.Acquire(Key(true, key), "HolderTest.ItsMethod"))
                {
                    var ex = Assert.ThrowsException<AggregateException>(() => AcquireAsync(Key(true, key)).Wait(Soon));
                    StringAssert.Contains(ex.InnerException.Message, "HolderTest.ItsMethod");
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(FileLock.TimeoutVariable, previous);
            }
        }

        [TestMethod]
        public void ALockSetIsTakenInOneOrderAndExclusiveWins()
        {
            var specs = LockSpec.Normalize(new[]
            {
                LockSpec.Leg(false),
                LockSpec.Resource("h", "ether2"),
                LockSpec.Router("h", false),
                LockSpec.Global(false),
                LockSpec.Router("h", true),
            });

            CollectionAssert.AreEqual(new[] { "global", "router@h", "res@h@ether2" },
                                      specs.Take(3).Select(s => s.Key).ToArray());
            Assert.AreEqual(LockSpec.LegRank, specs.Last().Rank);
            Assert.IsTrue(specs.Single(s => s.Key == "router@h").Exclusive, "a shared and an exclusive request for one lock must merge to exclusive");
        }

        [TestMethod]
        public void AnEscalationReleasesAndRetakesTheSetInOrder()
        {
            string routerKey = NewKey();
            var set = new TestLockSet("escalating test");
            set.Acquire(new[] { Key(false, routerKey) });

            using (set.Escalate(Key(true, routerKey)))
            {
                var other = AcquireAsync(Key(false, routerKey));
                Assert.IsFalse(other.Wait(NotYet), "the escalated lock is not exclusive");
                set.Release();
                Assert.IsTrue(other.Wait(Soon));
                other.Result.Dispose();
                set.Acquire(new[] { Key(true, routerKey) });
            }

            // Back to shared: another reader gets in.
            var reader = AcquireAsync(Key(false, routerKey));
            Assert.IsTrue(reader.Wait(Soon), "the escalation was not undone");
            reader.Result.Dispose();
            set.Release();
        }

        /// <summary>
        /// A test whose <c>[Timeout]</c> ran out while TestInitialize waited for a lock: MSTest abandons the thread,
        /// which goes on waiting and gets the lock later, with nothing left to release it. In the 2026-10-10
        /// release-gate run one held a router exclusive for the rest of the process, and every later test waited out
        /// its 20 minutes for it. Closed, the set gives back whatever it gets.
        /// </summary>
        [TestMethod]
        public void ALockTakenAfterTheSetWasClosedIsGivenBack()
        {
            string key = NewKey();
            var blocker = FileLock.Acquire(Key(true, key), "the test ahead");
            var abandoned = new TestLockSet("timed-out test");
            var waiting = Task.Run(() => abandoned.Acquire(new[] { Key(true, key) }));
            Assert.IsFalse(waiting.Wait(NotYet), "the set got in while the lock was held");

            abandoned.Close();   // its TestCleanup, or the next test's TestInitialize
            blocker.Dispose();   // ... and then the lock it was waiting for comes free

            try { waiting.Wait(Soon); } catch (AggregateException) { }
            Assert.IsTrue(waiting.IsCompleted, "the closed set is still waiting");
            var next = AcquireAsync(Key(true, key));
            Assert.IsTrue(next.Wait(Soon), "a closed set kept the lock it got after the close");
            next.Result.Dispose();
        }

        [TestMethod]
        public void ALeaseIsAliveExactlyWhileItsFileIsHeld()
        {
            string tag = "u" + Guid.NewGuid().ToString("N").Substring(0, 5);
            string dir = Path.Combine(FileLock.Root, "runs");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, tag + ".lease");

            Assert.IsFalse(RunLease.IsAlive(tag), "a tag with no lease file is a run that ended");
            using (new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
                Assert.IsTrue(RunLease.IsAlive(tag));
            Assert.IsFalse(RunLease.IsAlive(tag), "the lease outlived its holder");
            File.Delete(path);

            Assert.IsTrue(RunLease.IsAlive(RunLease.Tag), "this process's own tag is alive without asking");
        }

        [TestMethod]
        public void ARunTagIsFoundInANameAndNotInAGuid()
        {
            string name = TestNames.Unique("t4n-bond");
            StringAssert.StartsWith(name, "t4n-bond-u");
            Assert.AreEqual(RunLease.Tag, RunLease.TagPattern.Match(name).Groups[1].Value);
            Assert.AreEqual(RunLease.Tag, RunLease.TagPattern.Match("comment " + RunLease.Tag + " tail").Groups[1].Value);

            // A GUID comment is hex: it can never contain the 'u' that starts a tag.
            Assert.IsFalse(RunLease.TagPattern.IsMatch(Guid.NewGuid().ToString()));
            Assert.IsFalse(RunLease.TagPattern.IsMatch("t4n-ubuntu1"), "a tag must stand on its own, not inside a word");
        }
    }
}
