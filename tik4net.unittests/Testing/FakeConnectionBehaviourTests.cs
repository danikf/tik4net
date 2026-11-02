// FakeConnectionBehaviourTests.cs — what TikFakeConnection does beyond answering one command: the entity helpers,
// WithEntities evaluating the read, the command's guards, the connection's lifecycle and the capability flags it can
// and cannot back. FakeCommandParityTests holds the answers themselves against the real binary API command.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip;
using tik4net.Objects.Tool;
using tik4net.Testing;

namespace tik4net.unittests.Testing
{
    [TestClass]
    public class FakeConnectionBehaviourTests
    {
        private static readonly TikFakeDoneSentence Done = new TikFakeDoneSentence();

        private static TikFakeConnection TwoAddresses() => new TikFakeConnection().WithEntities(
            new IpAddress { Address = "10.0.0.1/24", Interface = "ether1", Disabled = false }.WithId("*1"),
            new IpAddress { Address = "10.0.0.2/24", Interface = "ether2", Disabled = true }.WithId("*2"));

        // ── WithValue (X1) ────────────────────────────────────────────────────

        [TestMethod]
        public void WithValue_SetsAReadOnlyField_FromAPlainValue()
        {
            var address = new IpAddress().WithValue("dynamic", true).WithValue("actual-interface", "ether1");

            Assert.IsTrue(address.Dynamic == true);
            Assert.AreEqual("ether1", address.ActualInterface.Value);
        }

        [TestMethod]
        public void WithValue_ConvertsANumberToTheFieldsType()
        {
            var torch = new ToolTorch().WithValue("tx", 1000);   // an int, for a TikField<long?>

            Assert.AreEqual(1000L, torch.Tx.Value);
        }

        [TestMethod]
        public void WithValue_TakesTheFieldItself()
        {
            var address = new IpAddress().WithValue("dynamic", (TikField<bool?>)true);

            Assert.IsTrue(address.Dynamic == true);
        }

        [TestMethod]
        public void WithValue_Null_LeavesTheFieldAbsent()
        {
            var address = new IpAddress().WithValue("dynamic", true).WithValue("dynamic", null);

            Assert.AreEqual(TikFieldState.Absent, address.Dynamic.State);
        }

        [TestMethod]
        public void WithValue_RefusesAValueOfTheWrongKind_NamingTheField()
        {
            var ex = Assert.ThrowsException<ArgumentException>(() => new IpAddress().WithValue("dynamic", "yes-please"));

            StringAssert.Contains(ex.Message, "dynamic");
        }

        // ── WithEntities evaluates the read (X2, X4) ─────────────────────────

        [TestMethod]
        public void WithEntities_AppliesAFilter()
        {
            var conn = TwoAddresses();

            var rows = conn.LoadList<IpAddress>(conn.CreateParameter("disabled", "true")).ToList();

            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual("*2", rows[0].Id);
        }

        [TestMethod]
        public void WithEntities_LoadById_FindsTheRow_AmongSeveral()
        {
            Assert.AreEqual("*2", TwoAddresses().LoadById<IpAddress>("*2").Id);
        }

        [TestMethod]
        public void WithEntities_LoadById_OfAMissingRow_IsNoSuchItem()
        {
            Assert.ThrowsException<TikNoSuchItemException>(() => TwoAddresses().LoadById<IpAddress>("*9"));
        }

        [TestMethod]
        public void WithEntities_EqualityAndAbsenceFilters()
        {
            var conn = new TikFakeConnection().WithEntities(
                new IpAddress { Address = "10.0.0.1/24", Comment = "x" }.WithId("*1"),
                new IpAddress { Address = "10.0.0.2/24" }.WithId("*2"));

            var with = conn.CreateCommand("/ip/address/print", conn.CreateParameter("?comment", "x")).ExecuteList().ToList();
            var without = conn.CreateCommand("/ip/address/print", conn.CreateParameter("?-comment", null)).ExecuteList().ToList();

            CollectionAssert.AreEqual(new[] { "*1" }, with.Select(r => r.GetId()).ToList());
            CollectionAssert.AreEqual(new[] { "*2" }, without.Select(r => r.GetId()).ToList());
        }

        [TestMethod]
        public void WithEntities_HonoursTheFieldList()
        {
            var row = TwoAddresses().CreateCommand("/ip/address/print").ExecuteList("address").First();

            CollectionAssert.AreEqual(new[] { "address" }, row.Words.Keys.ToList());
        }

        [TestMethod]
        public void WithEntities_RefusesAReadItCannotEvaluate()
        {
            var conn = TwoAddresses();
            var cmd = conn.CreateCommand("/ip/address/print", conn.CreateParameter("?#|", null));

            var ex = Assert.ThrowsException<InvalidOperationException>(() => cmd.ExecuteList());
            StringAssert.Contains(ex.Message, "WithResponse");
        }

        // ── The command's guards (X6) ─────────────────────────────────────────

        [TestMethod]
        public void ASecondExecute_WhileACallbackRuns_IsRefused()
        {
            var cmd = new TikFakeConnection().WithResponse(_ => true, new[] { new TikFakeReSentence(new Dictionary<string, string> { ["a"] = "1" }) })
                .CreateCommand("/interface/listen");
            cmd.ExecuteWithCallback(_ => { });
            try
            {
                Assert.ThrowsException<InvalidOperationException>(() => cmd.ExecuteWithCallback(_ => { }));
                Assert.ThrowsException<InvalidOperationException>(() => cmd.CommandText = "/other");
            }
            finally
            {
                Assert.IsTrue(cmd.CancelAndJoin(TimeSpan.FromSeconds(5)));
            }
            Assert.IsFalse(cmd.IsRunning);
        }

        [TestMethod]
        public void ACommandWithoutText_IsRefused()
        {
            var conn = new TikFakeConnection().WithResponse(_ => true, new ITikSentence[] { Done });

            Assert.ThrowsException<InvalidOperationException>(() => conn.CreateCommand().ExecuteList());
            Assert.AreEqual(0, conn.SentCommands.Count);
        }

        [TestMethod]
        public void ATagParameter_IsSentAsATag()
        {
            var conn = new TikFakeConnection().WithResponse(_ => true, new ITikSentence[] { Done });
            var cmd = conn.CreateCommand("/x/print");
            cmd.AddParameter(TikSpecialProperties.Tag, "7");
            cmd.ExecuteList();

            CollectionAssert.AreEqual(new[] { "/x/print", ".tag=7" }, conn.SentCommands.Last());
        }

        [TestMethod]
        public void TheInterruptedTrapOfACancel_EndsInOnDone()
        {
            var ended = new ManualResetEventSlim(false);
            bool error = false, done = false;
            var cmd = new TikFakeConnection().WithTrap(_ => true, "interrupted", "2").CreateCommand("/interface/listen");

            cmd.ExecuteWithCallback(_ => { }, _ => { error = true; ended.Set(); }, () => { done = true; ended.Set(); });

            Assert.IsTrue(ended.Wait(TimeSpan.FromSeconds(5)));
            Assert.IsFalse(error);
            Assert.IsTrue(done);
        }

        [TestMethod]
        public void AConnectionLoss_ReachesTheErrorCallback()
        {
            var ended = new ManualResetEventSlim(false);
            string? error = null;
            var cmd = new TikFakeConnection().WithFatal(_ => true, "connection lost").CreateCommand("/interface/listen");

            cmd.ExecuteWithCallback(_ => { }, t => { error = t.Message; ended.Set(); }, () => ended.Set());

            Assert.IsTrue(ended.Wait(TimeSpan.FromSeconds(5)));
            Assert.AreEqual("connection lost", error);
            Assert.IsTrue(SpinWait.SpinUntil(() => !cmd.IsRunning, TimeSpan.FromSeconds(5)));
        }

        [TestMethod]
        public void AConnectionLoss_IsAFatalException_OnARead()
        {
            var conn = new TikFakeConnection().WithFatal(_ => true, "connection lost");

            Assert.ThrowsException<TikCommandFatalException>(() => conn.CreateCommand("/ip/address/print").ExecuteList());
        }

        // ── Lifecycle (X8) ────────────────────────────────────────────────────

        [TestMethod]
        public void AClosedConnection_RefusesCommands_AndOpenGivesThemBack()
        {
            var conn = new TikFakeConnection().WithResponse(_ => true, new ITikSentence[] { Done });

            conn.Close();
            Assert.IsFalse(conn.IsOpened);
            Assert.ThrowsException<TikConnectionNotOpenException>(() => conn.CreateCommand("/x/print").ExecuteList());

            conn.Open("router", "admin", "");
            Assert.IsTrue(conn.IsOpened);
            conn.CreateCommand("/x/print").ExecuteList();
        }

        [TestMethod]
        public void Close_EndsARunningCallbackInOnDone()
        {
            var ended = new ManualResetEventSlim(false);
            var conn = new TikFakeConnection().WithResponse(_ => true, Array.Empty<ITikSentence>());
            var cmd = conn.CreateCommand("/interface/listen");
            cmd.ExecuteWithCallback(_ => { }, _ => { }, () => ended.Set());

            conn.Close();

            Assert.IsTrue(ended.Wait(TimeSpan.FromSeconds(5)));
        }

        [TestMethod]
        public void TheRowEvents_Fire()
        {
            var written = new List<string>();
            var read = new List<string>();
            var conn = new TikFakeConnection().WithResponse(_ => true,
                new ITikSentence[] { new TikFakeReSentence(new Dictionary<string, string> { ["name"] = "r" }), Done });
            conn.OnWriteRow += (_, e) => written.Add(e.Word);
            conn.OnReadRow += (_, e) => read.Add(e.Word);

            conn.CreateCommand("/system/identity/print").ExecuteList();

            CollectionAssert.AreEqual(new[] { "/system/identity/print" }, written);
            CollectionAssert.AreEqual(new[] { "!re", "=name=r", "!done" }, read);
        }

        [TestMethod]
        public void TheEncoding_IsUtf8_AsOnEveryTransport()
        {
            Assert.AreEqual("utf-8", new TikFakeConnection().Encoding.WebName);
        }

        // ── Capability flags (X7) ─────────────────────────────────────────────

        [TestMethod]
        public void TheMenuSchemaFlag_IsRefused()
        {
            var conn = new TikFakeConnection();

            Assert.ThrowsException<ArgumentException>(() => conn.Capabilities |= TikConnectionCapability.MenuSchema);
        }

#if NET8_0_OR_GREATER
        [TestMethod]
        public void WithTheStreamingFlag_AStreamingReadRuns()
        {
            var conn = new TikFakeConnection { Capabilities = new TikFakeConnection().Capabilities | TikConnectionCapability.Streaming }
                .WithResponse(_ => true, new ITikSentence[] { new TikFakeReSentence(new Dictionary<string, string> { ["a"] = "1" }), Done });

            var rows = Collect(conn.CreateCommand("/interface/monitor-traffic").ExecuteListWithDurationAsync(TimeSpan.FromSeconds(1)));

            Assert.AreEqual(1, rows.Count);
        }

        [TestMethod]
        public void AStreamingRead_EndsInAnAbort_AfterTheRowsBeforeATrap()
        {
            var conn = new TikFakeConnection { Capabilities = new TikFakeConnection().Capabilities | TikConnectionCapability.Streaming }
                .WithResponse(_ => true, new ITikSentence[]
                {
                    new TikFakeReSentence(new Dictionary<string, string> { ["a"] = "1" }),
                    new TikFakeTrapSentence("boom"), Done,
                });
            var rows = new List<ITikReSentence>();

            var ex = Assert.ThrowsException<TikCommandAbortException>(() =>
            {
                foreach (var row in Collect(conn.CreateCommand("/tool/fetch").ExecuteListUntilDoneAsync(TimeSpan.FromSeconds(1)), rows)) { }
            });
            Assert.AreEqual(1, rows.Count);
            Assert.IsInstanceOfType(ex.InnerException, typeof(TikCommandTrapException));
        }

        private static List<ITikReSentence> Collect(IAsyncEnumerable<ITikReSentence> stream, List<ITikReSentence>? into = null)
        {
            var rows = into ?? new List<ITikReSentence>();
            var e = stream.GetAsyncEnumerator();
            try
            {
                while (e.MoveNextAsync().AsTask().GetAwaiter().GetResult())
                    rows.Add(e.Current);
            }
            finally
            {
                e.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            return rows;
        }
#endif
    }
}
