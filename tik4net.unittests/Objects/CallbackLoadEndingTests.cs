// CallbackLoadEndingTests.cs — the mapper's callback loads hand onExceptionCallback the classified trap
// (TikTrapClassifier, as every Execute* does), and the connection-level LoadWithCallback has an overload with
// onDoneCallback (command-layer X9). The overload pins which call shapes bind where.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    [TestClass]
    public class CallbackLoadEndingTests
    {
        private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

        private static TikFakeConnection TrapOn(string verb, string message)
            => new TikFakeConnection().WithTrap(rows => rows.First().EndsWith(verb, StringComparison.Ordinal), message, "4");

        private static Exception WaitForError(Func<Action<Exception>, ITikCommand> start)
        {
            Exception error = null;
            var got = new ManualResetEventSlim(false);
            var cmd = start(ex => { error = ex; got.Set(); });
            Assert.IsTrue(got.Wait(Wait), "no error reached the callback");
            cmd.CancelAndJoin();
            return error;
        }

        [TestMethod]
        public void ConnectionLoadWithCallback_GetsTheClassifiedTrap()
        {
            var conn = TrapOn("/print", "no such item (4)");
            var ex = WaitForError(onEx => conn.LoadWithCallback<IpAddress>(_ => { }, onEx));
            Assert.IsInstanceOfType(ex, typeof(TikNoSuchItemException));
            Assert.AreEqual("no such item (4)", ex.Message);
        }

        [TestMethod]
        public void CommandLoadWithCallback_GetsTheClassifiedTrap()
        {
            var conn = TrapOn("/print", "no such command");
            var ex = WaitForError(onEx =>
            {
                var cmd = conn.CreateCommand("/ip/address/print");
                cmd.LoadWithCallback<IpAddress>(_ => { }, onEx);
                return cmd;
            });
            Assert.IsInstanceOfType(ex, typeof(TikNoSuchCommandException));
        }

        [TestMethod]
        public void LoadListenWithCallback_GetsTheClassifiedTrap()
        {
            var conn = TrapOn("/listen", "no such item");
            var ex = WaitForError(onEx => conn.LoadListenWithCallback<IpAddress>(_ => { }, null, onEx));
            Assert.IsInstanceOfType(ex, typeof(TikNoSuchItemException));
        }

        [TestMethod]
        public void AnUnclassifiedTrapIsTheBaseTrap()
        {
            var conn = TrapOn("/print", "input does not match any value of interface");
            var ex = WaitForError(onEx => conn.LoadWithCallback<IpAddress>(_ => { }, onEx));
            Assert.AreEqual(typeof(TikCommandTrapException), ex.GetType());
        }

        // ── The onDone overload (X9) ──────────────────────────────────────────

        private static TikFakeConnection TwoAddresses()
            => new TikFakeConnection().WithEntities(
                new IpAddress { Address = "192.168.4.1/24", Interface = "ether1" }.WithId("*1"),
                new IpAddress { Address = "192.168.4.2/24", Interface = "ether1" }.WithId("*2"))
            .WithNonQuery(rows => rows.First().EndsWith("/set", StringComparison.Ordinal));

        [TestMethod]
        public void ALambdaInTheThirdPositionIsOnDone()
        {
            var conn = TwoAddresses();
            var loaded = new List<IpAddress>();
            var done = new ManualResetEventSlim(false);

            var cmd = conn.LoadWithCallback<IpAddress>(e => { lock (loaded) loaded.Add(e); }, ex => Assert.Fail(ex.ToString()),
                () => done.Set());

            Assert.IsTrue(done.Wait(Wait), "onDone was not called after the load's !done");
            cmd.CancelAndJoin();
            lock (loaded)
                Assert.AreEqual(2, loaded.Count, "every row arrived before onDone");
        }

        [TestMethod]
        public void TheOnDoneOverloadTakesFiltersAfterOnDone()
        {
            var conn = TwoAddresses();
            var done = new ManualResetEventSlim(false);

            var cmd = conn.LoadWithCallback<IpAddress>(_ => { }, null, done.Set,
                conn.CreateParameter("address", "192.168.4.1/24"));

            Assert.IsTrue(done.Wait(Wait));
            cmd.CancelAndJoin();
            Assert.IsTrue(conn.SentCommands.Last().Contains("?address=192.168.4.1/24"),
                string.Join(" ", conn.SentCommands.Last()));
        }

        [TestMethod]
        public void TheExistingCallShapesBindAsBefore()
        {
            // (callback) and (callback, onException, filter...) are the old overload's shapes; they compile unchanged
            // and send the same command as before (the fake answers every row whatever the filter).
            var conn = TwoAddresses();
            var loaded = 0;
            var cmd1 = conn.LoadWithCallback<IpAddress>(_ => Interlocked.Increment(ref loaded));
            var cmd2 = conn.LoadWithCallback<IpAddress>(_ => Interlocked.Increment(ref loaded), ex => Assert.Fail(ex.ToString()),
                conn.CreateParameter("address", "192.168.4.2/24"));

            Assert.IsTrue(SpinWait.SpinUntil(() => Volatile.Read(ref loaded) == 4, Wait), "loaded " + loaded);
            cmd1.CancelAndJoin();
            cmd2.CancelAndJoin();
            Assert.IsTrue(conn.SentCommands.Last().Contains("?address=192.168.4.2/24"));
        }

        [TestMethod]
        public void BothOverloadsSnapshotTheLoadedEntities()
        {
            // The old overload calls the new one with onDoneCallback: null; the change-tracker snapshot is shared.
            var conn = TwoAddresses();
            var done = new ManualResetEventSlim(false);
            IpAddress first = null;
            var cmd = conn.LoadWithCallback<IpAddress>(e => first ??= e, null, done.Set);
            Assert.IsTrue(done.Wait(Wait));
            cmd.CancelAndJoin();

            first.Comment = "changed";
            conn.Save(first);
            var set = conn.SentCommands.Last();
            CollectionAssert.Contains(set, "=comment=changed");
            Assert.IsFalse(set.Any(w => w.StartsWith("=address=", StringComparison.Ordinal)),
                "only the changed field is sent: " + string.Join(" ", set));
        }
    }
}
