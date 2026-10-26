// CallbackOnDoneAfterErrorTests.cs — TikFakeCommand and the mapper's callback loads follow D21 (command-layer X16):
// onDone fires when the command ends without an error, and not after a trap, which reaches the error callback alone.

#nullable enable

using System;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    [TestClass]
    public class CallbackOnDoneAfterErrorTests
    {
        private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

        private static TikFakeConnection TrapOnPrint()
            => new TikFakeConnection().WithTrap(rows => rows.First().EndsWith("/print", StringComparison.Ordinal),
                "no such item (4)", "4");

        /// <summary>Waits until the fake has delivered every scripted sentence: its worker thread has ended.</summary>
        private static void WaitUntilEnded(ITikCommand cmd)
            => Assert.IsTrue(SpinWait.SpinUntil(() => !cmd.IsRunning, Wait), "the command still runs");

        [TestMethod]
        public void FakeCommand_ARegisteredTrap_CallsTheErrorCallbackAndNotOnDone()
        {
            var cmd = TrapOnPrint().CreateCommand("/ip/address/print");
            int errors = 0, done = 0;
            cmd.ExecuteWithCallback(_ => { }, _ => Interlocked.Increment(ref errors), () => Interlocked.Increment(ref done));

            WaitUntilEnded(cmd);
            Assert.AreEqual(1, Volatile.Read(ref errors), "the trap reached the error callback");
            Assert.AreEqual(0, Volatile.Read(ref done), "onDone is not called after an error (D21)");
        }

        [TestMethod]
        public void FakeCommand_RowsThenDone_CallsOnDone()
        {
            var conn = new TikFakeConnection().WithEntities(
                new IpAddress { Address = "192.168.4.1/24", Interface = "ether1" }.WithId("*1"));
            var cmd = conn.CreateCommand("/ip/address/print");
            int rows = 0, done = 0;
            cmd.ExecuteWithCallback(_ => Interlocked.Increment(ref rows), _ => Assert.Fail("no error expected"),
                () => Interlocked.Increment(ref done));

            WaitUntilEnded(cmd);
            Assert.AreEqual(1, Volatile.Read(ref rows));
            Assert.AreEqual(1, Volatile.Read(ref done));
        }

        [TestMethod]
        public void CommandLoadWithCallback_ATrap_CallsOnExceptionAndNotOnDone()
        {
            var cmd = TrapOnPrint().CreateCommand("/ip/address/print");
            Exception? error = null;
            int done = 0;
            cmd.LoadWithCallback<IpAddress>(_ => { }, ex => error = ex, () => Interlocked.Increment(ref done));

            WaitUntilEnded(cmd);
            Assert.IsInstanceOfType(error, typeof(TikNoSuchItemException));
            Assert.AreEqual(0, Volatile.Read(ref done), "onDone is not called after an error (D21)");
        }

        [TestMethod]
        public void ConnectionLoadWithCallback_ATrap_CallsOnExceptionAndNotOnDone()
        {
            Exception? error = null;
            int done = 0;
            var cmd = TrapOnPrint().LoadWithCallback<IpAddress>(_ => { }, ex => error = ex,
                () => Interlocked.Increment(ref done));

            WaitUntilEnded(cmd);
            Assert.IsInstanceOfType(error, typeof(TikNoSuchItemException));
            Assert.AreEqual(0, Volatile.Read(ref done), "onDone is not called after an error (D21)");
        }
    }
}
