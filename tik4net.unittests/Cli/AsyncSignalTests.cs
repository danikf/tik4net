using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// Covers <see cref="AsyncSignal"/>, the awaitable "the pump received something" signal the terminal
    /// transports wait on instead of blocking a thread-pool thread (A6).
    /// </summary>
    /// <remarks>
    /// The property worth pinning is set-before-wait. The pump can signal in the window between the
    /// reader's <c>Reset</c> and its <c>WaitAsync</c>, and a signal lost there does not fail — it costs one
    /// poll interval, on every command, which is invisible in a green test run and shows up only as a
    /// transport that got slower.
    /// </remarks>
    [TestClass]
    public class AsyncSignalTests
    {
        [TestMethod]
        public async Task ASignalSetBeforeTheWaitIsNotLost()
        {
            var signal = new AsyncSignal();
            signal.Set();

            var sw = Stopwatch.StartNew();
            bool signalled = await signal.WaitAsync(5000);
            sw.Stop();

            Assert.IsTrue(signalled);
            Assert.IsTrue(sw.ElapsedMilliseconds < 1000,
                $"the standing signal was not observed immediately ({sw.ElapsedMilliseconds} ms)");
        }

        [TestMethod]
        public async Task SetIsIdempotentAndSignalsOnlyTheNextWait()
        {
            var signal = new AsyncSignal();
            signal.Set();
            signal.Set();   // must not throw (SemaphoreFullException) and must not queue a second signal

            Assert.IsTrue(await signal.WaitAsync(5000));
            Assert.IsFalse(await signal.WaitAsync(50), "two Sets must not satisfy two waits");
        }

        [TestMethod]
        public async Task ResetClearsAStandingSignal()
        {
            var signal = new AsyncSignal();
            signal.Set();
            signal.Reset();

            Assert.IsFalse(await signal.WaitAsync(50));
        }

        [TestMethod]
        public async Task AWaitEndsOnItsTimeoutWhenNothingArrives()
        {
            var signal = new AsyncSignal();

            var sw = Stopwatch.StartNew();
            bool signalled = await signal.WaitAsync(100);
            sw.Stop();

            Assert.IsFalse(signalled);
            Assert.IsTrue(sw.ElapsedMilliseconds >= 90, $"returned after only {sw.ElapsedMilliseconds} ms");
        }

        [TestMethod]
        public async Task AWaiterIsReleasedByASignalFromAnotherThread()
        {
            // The live arrangement: the pump thread signals a reader that is already waiting.
            var signal = new AsyncSignal();
            var waiting = signal.WaitAsync(5000);

            var pump = new Thread(() => { Thread.Sleep(50); signal.Set(); }) { IsBackground = true };
            pump.Start();

            Assert.IsTrue(await waiting);
        }
    }
}
