#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Connection;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// The verbs a read method reaches that are no table. A parameter of theirs is an input, and <c>as-value</c> sent to
    /// one of them is its first positional argument: <c>:put [/tool torch as-value]</c> answers <i>input does not match any
    /// value of interface</i>, and <c>:put [/ip address find as-value]</c> answers nothing where <c>find</c> answers the ids
    /// (7.25, 7.21.5, 6.49.21).
    /// </summary>
    [TestClass]
    public class CliTorchAndOtherVerbReadTests
    {
        private const string Frame =
            "-- [Q quit|D dump|C-z pause]Columns: IP-PROTOCOL, SRC-ADDRESS, SRC-PORT, DST-ADDRESS, DST-PORT, TX, RX, TX-PACKETS, RX-PACKETS\r\n"
            + "IP-PROTOCOL SRC-ADDRESS SRC-PORT DST-ADDRESS DST-PORT TX RX TX-PACKETS RX-PACKETS\r\n"
            + "tcp 10.0.0.1 23 10.0.0.2 5000 1440bps 480bps 1 1\r\n"
            + "-- [Q quit|D dump|C-z pause]";

        private sealed class Router : ScriptedCliConnectionBase
        {
            internal readonly List<string> Sent = new List<string>();

            internal Router(Func<string, string> answer)
            {
                CliFieldSeparator = null;
                OpenScripted(_ => Task.FromResult(0),
                    (line, ct) => { Sent.Add(line); return Task.FromResult(answer(line)); },
                    (raw, ct) => Task.FromResult(string.Empty), () => { });
            }

            public override void Open(string host, string user, string password) { }
            public override void Open(string host, int port, string user, string password) { }
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) => Task.FromResult(0);
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) => Task.FromResult(0);
        }

        private static ITikCommand Torch(ITikConnection connection, TikCommandParameterFormat format = TikCommandParameterFormat.NameValue)
            => connection.CreateCommandAndParameters("/tool/torch", format, "interface", "ether1");

        // ── Torch through a read method ────────────────────────────────────────

        [DataTestMethod]
        [DataRow(TikCommandParameterFormat.NameValue)]
        [DataRow(TikCommandParameterFormat.Default)]
        public void TorchKeepsItsInterface_AndAnswersOneFrame(TikCommandParameterFormat format)
        {
            using var router = new Router(line => Frame);

            var rows = Torch(router, format).ExecuteList().ToList();

            Assert.AreEqual(1, router.Sent.Count);
            StringAssert.StartsWith(router.Sent[0], ":put [/tool torch interface=ether1 duration=4 freeze-frame-interval=2 proplist=");
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual("tcp", rows[0].GetResponseField("ip-protocol"));
        }

        [TestMethod]
        public void TorchWithADeadlineReadsFramesUntilIt()
        {
            int frames = 0;
            using var router = new Router(line => { frames++; Thread.Sleep(50); return Frame; });

            Assert.ThrowsException<TikCommandAbortException>(() => Torch(router).ExecuteListUntilDone(TimeSpan.FromMilliseconds(400)));

            Assert.IsTrue(frames >= 2, "frames until the deadline, not one: " + frames);
            StringAssert.Contains(router.Sent[0], "duration=2 freeze-frame-interval=1",
                "the shortest frame that flushes, so the deadline is not overrun by more than one of them");
        }

        [TestMethod]
        public void ARefusedTorchThrowsOnce_AndDoesNotSpin()
        {
            using var router = new Router(line => "input does not match any value of interface");

            var ex = Assert.ThrowsException<TikCommandTrapException>(() => Torch(router).ExecuteList());

            StringAssert.Contains(ex.Message, "input does not match any value of interface");
            Assert.AreEqual(1, router.Sent.Count);
        }

        [TestMethod]
        public void ARefusedTorchOnTheCallbackPathReportsOnce_AndEnds()
        {
            using var router = new Router(line => "input does not match any value of interface");
            var errors = new List<string>();
            using var ended = new ManualResetEventSlim();

            Torch(router).ExecuteWithCallback(row => { }, trap => { errors.Add(trap.Message); ended.Set(); });

            Assert.IsTrue(ended.Wait(TimeSpan.FromSeconds(5)), "the refusal was never reported");
            Thread.Sleep(100);
            CollectionAssert.AreEqual(new[] { "input does not match any value of interface" }, errors);
            Assert.AreEqual(1, router.Sent.Count, "no second command after the refusal");
        }

        [TestMethod]
        public void WhenThePlainTorchIsRefusedTooTheCallerReadsThatAnswer()
        {
            // RouterOS 6: proplist= is the first refusal, and the plain form says what is really wrong (6.49.21).
            using var router = new Router(line => line.Contains("proplist=")
                ? "expected end of command (line 1 column 71)"
                : "input does not match any value of interface");

            var ex = Assert.ThrowsException<TikCommandTrapException>(() => Torch(router).ExecuteList());

            StringAssert.Contains(ex.Message, "input does not match any value of interface");
        }

        [TestMethod]
        public void AClassifiableRefusalOfThePlainTorchIsClassified()
        {
            using var router = new Router(line => line.Contains("proplist=")
                ? "expected end of command (line 1 column 71)"
                : "syntax error (line 1 column 9)");

            Assert.ThrowsException<TikNoSuchCommandException>(() => Torch(router).ExecuteList());
        }

        // ── A verb nobody listed ───────────────────────────────────────────────

        [TestMethod]
        public void AnUnlistedVerbKeepsItsInputs_AndGetsNoAsValue()
        {
            using var router = new Router(line => "*2;*3");

            string? ids = router.CreateCommand("/ip/address/find").ExecuteScalar();

            Assert.AreEqual(":put [/ip address find]", router.Sent.Single());
            Assert.AreEqual("*2;*3", ids);
        }

        [TestMethod]
        public void AnUnlistedVerbsParametersGoOutAsInputsInEveryFormat()
        {
            foreach (var format in new[] { TikCommandParameterFormat.Default, TikCommandParameterFormat.NameValue, TikCommandParameterFormat.Filter })
            {
                using var router = new Router(line => string.Empty);
                var command = router.CreateCommandAndParameters("/ip/address/find", format, "interface", "ether1");

                command.ExecuteScalarOrDefault("none");

                Assert.AreEqual(":put [/ip address find interface=ether1]", router.Sent.Single(), format.ToString());
            }
        }

        [TestMethod]
        public void AnUnlistedVerbWithRecordsAnswersRows()
        {
            using var router = new Router(line => "name=a;value=1");

            Assert.AreEqual("a", router.CreateCommand("/some/menu/report").ExecuteList().Single().GetResponseField("name"));
        }

        [TestMethod]
        public void AnUnlistedVerbsRefusalIsTheRouters()
        {
            using var router = new Router(line => "bad command name report (line 1 column 12)");

            Assert.ThrowsException<TikNoSuchCommandException>(() => router.CreateCommand("/some/menu/report").ExecuteList());
        }

        [TestMethod]
        public void AWriteThroughAScalarRunsAsTheWrite()
        {
            using var router = new Router(line => string.Empty);
            var command = router.CreateCommandAndParameters("/ip/address/set", ".id", "*2", "comment", "x");

            command.ExecuteScalarOrDefault("none");

            Assert.AreEqual("/ip address set numbers=*2 comment=x", router.Sent.Last(), "after the first write's question about the end marker");
        }

        // ── Self-terminating monitors go through the table ─────────────────────

        private const string PingTable =
            "  SEQ HOST                                     SIZE TTL TIME       STATUS\r\n"
            + "    0 127.0.0.1                                  56  64 100us\r\n"
            + "    1 127.0.0.1                                  56  64 176us\r\n"
            + "    sent=2 received=2 packet-loss=0% min-rtt=100us avg-rtt=138us\r\n"
            + "   max-rtt=176us\r\n";

        [TestMethod]
        public void APingIsReadThroughItsTable_WithTheSummaryOnTheRowItFollows()
        {
            using var router = new Router(line => PingTable);
            var command = router.CreateCommandAndParameters("/ping", TikCommandParameterFormat.NameValue, "address", "127.0.0.1", "count", "2");

            var rows = command.ExecuteList().ToList();

            Assert.AreEqual("/ping address=127.0.0.1 count=2", router.Sent.Single());
            Assert.AreEqual(2, rows.Count);
            Assert.IsFalse(rows[0].Words.ContainsKey(TikSpecialProperties.Id), "the API's ping row has no .id");
            Assert.AreEqual("100us", rows[0].GetResponseField("time"));
            Assert.IsFalse(rows[0].Words.ContainsKey("sent"), "the router printed no running count for this row");
            Assert.AreEqual("2", rows[1].GetResponseField("sent"));
            Assert.AreEqual("2", rows[1].GetResponseField("received"));
            Assert.AreEqual("0", rows[1].GetResponseField("packet-loss"), "the API writes it without the sign");
            Assert.AreEqual("100us", rows[1].GetResponseField("min-rtt"));
            Assert.AreEqual("138us", rows[1].GetResponseField("avg-rtt"));
            Assert.AreEqual("176us", rows[1].GetResponseField("max-rtt"), "the summary wraps at the terminal's width on 7.25");
        }

        [TestMethod]
        public void TheRepeatedHeaderAndSummaryOfALongPingAreNotRows()
        {
            // Every 20 replies the router prints a summary and the header again (7.25, 7.21.5, 6.49.21).
            string header = "  SEQ HOST                                     SIZE TTL TIME       STATUS\r\n";
            string lines = header
                + string.Concat(Enumerable.Range(0, 20).Select(i => "   " + i.ToString().PadLeft(2) + " 127.0.0.1                                  56  64 100us\r\n"))
                + "    sent=20 received=20 packet-loss=0% min-rtt=100us avg-rtt=100us max-rtt=100us\r\n"
                + header
                + "   20 127.0.0.1                                  56  64 100us\r\n"
                + "    sent=21 received=21 packet-loss=0% min-rtt=100us avg-rtt=100us max-rtt=100us\r\n";
            using var router = new Router(line => lines);

            var rows = router.CreateCommandAndParameters("/ping", TikCommandParameterFormat.NameValue, "address", "127.0.0.1", "count", "21").ExecuteList().ToList();

            Assert.AreEqual(21, rows.Count);
            Assert.AreEqual("20", rows[19].GetResponseField("sent"));
            Assert.AreEqual("21", rows[20].GetResponseField("sent"));
        }

        [TestMethod]
        public void ARefusedPingIsTheRouters()
        {
            using var router = new Router(line => "failure: resolve failed");

            var ex = Assert.ThrowsException<TikCommandTrapException>(
                () => router.CreateCommandAndParameters("/ping", TikCommandParameterFormat.NameValue, "address", "nosuch.invalid").ExecuteList());

            StringAssert.Contains(ex.Message, "resolve failed");
        }

        // ── X16: '?-name' ──────────────────────────────────────────────────────

        [DataTestMethod]
        [DataRow("")]
        [DataRow(null)]
        public void HasNoSuchPropertyIsANegatedPresenceTest(string? value)
        {
            var parameters = new List<ITikCommandParameter> { new TikCommandParameter("-comment", value, TikCommandParameterFormat.Filter) };

            Assert.AreEqual("!(comment~\".\")", CliCommandBuilder.BuildWhereClause(parameters));
        }

        [TestMethod]
        public void TheQueryStackAgrees()
        {
            var filter = new List<ITikCommandParameter> { new TikCommandParameter("-comment", "", TikCommandParameterFormat.Filter) };

            Assert.IsTrue(tik4net.Connection.TikQueryStack.Matches(new TikRecordSentence(new Dictionary<string, string> { ["name"] = "a" }), filter));
            Assert.IsFalse(tik4net.Connection.TikQueryStack.Matches(new TikRecordSentence(new Dictionary<string, string> { ["comment"] = "x" }), filter));
        }

        // ── X15: the 8-bit CSI in a completion replay ──────────────────────────

        [TestMethod]
        public void AnEightBitCsiIsNotDrawnAsText()
        {
            string withSevenBit = "\x1b[36mip\x1b[m";
            string withEightBit = "\u009b36mip\u009bm";

            CollectionAssert.AreEqual(
                CliCompletionParser.Items(withSevenBit, string.Empty).Select(i => i.Name + "/" + i.Kind).ToList(),
                CliCompletionParser.Items(withEightBit, string.Empty).Select(i => i.Name + "/" + i.Kind).ToList());
        }

        // ── X11: the end marker ────────────────────────────────────────────────

        [DataTestMethod]
        [DataRow("/ip address set numbers=*1 comment=x", true)]
        [DataRow(":put [/ip address add address=1.1.1.1/32 interface=ether1]", true)]
        [DataRow("/ip firewall filter comment numbers=*1 comment=x", true)]
        [DataRow(":put [/interface get number=*2 value-name=comment]", false)]
        [DataRow("/tool fetch url=\"http://x/add\"", false)]
        [DataRow("/system reboot", false)]
        public void OnlyAWriteVerbTakesTheEndMarker(string line, bool marked)
        {
Assert.AreEqual(marked, CliConnectionBase.IsEndMarkerVerbLine(line));
        }

        // ── D3: detail on a generic print ──────────────────────────────────────

        [TestMethod]
        public void AGenericPrintAsksForEveryField()
        {
            using var router = new Router(line => line.StartsWith(":local d [") ? ".id=*1;mtu=1500\r\n#n=1/num" : ".id=*1;disabled=false");

            var row = router.CreateCommand("/interface/print").ExecuteList().Single();

            Assert.IsTrue(router.Sent.Any(l => l.StartsWith(":local d [/interface print detail as-value]")));
            Assert.AreEqual("1500", row.GetResponseField("mtu"));
        }

        [DataTestMethod]
        [DataRow("bad parameter detail (line 1 column 27)")]
        [DataRow("expected end of command (line 1 column 27)")]
        [DataRow("input does not match any value of peer")]
        public void AMenuThatRefusesDetailIsReadWithout_AndRemembered(string refusal)
        {
            using var router = new Router(line =>
                line.Contains(" detail ") ? refusal
                : line.StartsWith(":local d [") ? "name=router\r\n#n=1/nil"
                : ".id=*1;disabled=false");

            Assert.AreEqual("router", router.CreateCommand("/system/identity/print").ExecuteList().Single().GetResponseField("name"));

            router.Sent.Clear();
            router.CreateCommand("/system/identity/print").ExecuteList();
            Assert.IsFalse(router.Sent.Any(l => l.Contains(" detail ")), "asked once");
        }

        [TestMethod]
        public void ABadFilterIsStillTheCallersToSee()
        {
            using var router = new Router(line => line.StartsWith(":local d [") ? "expected yes or no (line 1 column 53)" : ".id=*1;disabled=false");

            var ex = Assert.ThrowsException<TikCommandTrapException>(
                () => router.CreateCommandAndParameters("/interface/print", "disabled", "maybe").ExecuteList());

            StringAssert.Contains(ex.Message, "expected yes or no");
            router.Sent.Clear();
            try { router.CreateCommand("/interface/print").ExecuteList(); } catch (TikCommandTrapException) { }
            Assert.IsTrue(router.Sent.Any(l => l.Contains(" detail ")), "both forms failed, so nothing is concluded about the word");
        }

        // ── D7: Ctrl-C on a cancelled read ─────────────────────────────────────

        private sealed class CancellableRouter : ScriptedCliConnectionBase
        {
            internal readonly ScriptedCliTerminal Terminal;
            private readonly TaskCompletionSource<string> _answer = new TaskCompletionSource<string>();
            internal readonly List<string> Sent = new List<string>();

            internal CancellableRouter(TikCancellationMode mode)
            {
                CliFieldSeparator = null;
                CancellationMode = mode;
                Terminal = new ScriptedCliTerminal(
                    (line, ct) =>
                    {
                        Sent.Add(line);
                        return line.StartsWith("/ping") || line.Contains("/ip address")
                            ? _answer.Task
                            : Task.FromResult(".id=*1;disabled=false");
                    },
                    (raw, ct) => Task.FromResult(string.Empty), () => { });
                Terminal.OnInterrupt = () => _answer.TrySetResult("interrupted");
                OpenScripted(Terminal);
            }

            public override void Open(string host, string user, string password) { }
            public override void Open(string host, int port, string user, string password) { }
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) => Task.FromResult(0);
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) => Task.FromResult(0);

            internal void Finish() => _answer.TrySetResult(string.Empty);
        }

        [TestMethod]
        public async Task ACancelledReadIsInterrupted_AndEndsInTheCancel()
        {
            using var router = new CancellableRouter(TikCancellationMode.Cooperative);
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            var command = (ITikCommandAsync)router.CreateCommandAndParameters("/ping", TikCommandParameterFormat.NameValue, "address", "127.0.0.1");

            await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => command.ExecuteListAsync(cancel.Token));

            Assert.AreEqual(1, router.Terminal.InterruptRequests);
        }

        [TestMethod]
        public async Task AWriteIsNeverInterrupted()
        {
            using var router = new CancellableRouter(TikCancellationMode.Cooperative);
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            var command = (ITikCommandAsync)router.CreateCommandAndParameters("/ip/address/set", TikCommandParameterFormat.NameValue, ".id", "*1", "comment", "x");

            var write = command.ExecuteNonQueryAsync(cancel.Token);
            await Task.Delay(200);
            Assert.IsFalse(write.IsCompleted, "the write reads to its end");
            router.Finish();

            await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => write);
            Assert.AreEqual(0, router.Terminal.InterruptRequests);
        }

        [TestMethod]
        public void AbandonAndCloseSendsNoKey()
        {
            using var router = new CancellableRouter(TikCancellationMode.AbandonAndClose);
            router.Finish();

            router.CreateCommandAndParameters("/ping", TikCommandParameterFormat.NameValue, "address", "127.0.0.1").ExecuteList();

            Assert.AreEqual(0, router.Terminal.InterruptRequests);
        }

        [TestMethod]
        public void AfterAnInterruptThePromptEndsTheRead_WithoutTheEchoOrTheCountLine()
        {
            const string sent = ":local d [/ip address print as-value]; :put $d; :put (\"#n=\" . [:len $d])";
            var settle = new CliPromptSettle(sent, settleMs: 0);

            Assert.AreEqual(CliPromptSettle.Outcome.Reading, settle.Observe("[admin@CHR2] > ", true, 10, 15), "no echo yet: a repaint");
            settle.Interrupted();
            Assert.AreEqual(CliPromptSettle.Outcome.Reading, settle.Observe("[admin@CHR2] > ", false, 20, 15), "the settle window starts");
            Assert.AreEqual(CliPromptSettle.Outcome.Settled, settle.Observe("[admin@CHR2] > ", false, 30, 15));
        }

        // RouterOS 6 answers the key with the prompt and repaints it on the same line, and the read waited out its deadline
        // on that (6.49.21, a cancelled /ping over Telnet and WinBox CLI: the prompt 50 ms after the key, the read 30 s).
        [TestMethod]
        public void AfterAnInterruptAPromptRepaintedOnItsLineEndsTheRead()
        {
            const string sent = "/ping address=127.0.0.1 count=20";
            const string answer = sent + "\r\n    0 127.0.0.1  56  64 0ms\r\n    sent=1 received=1\r\n\r\n\r\r\r[admin@CHR2] > \r[admin@CHR2] > ";
            var settle = new CliPromptSettle(sent, settleMs: 0);

            Assert.AreEqual(CliPromptSettle.Outcome.Reading, settle.Observe(answer, true, 10, answer.Length), "without a key this is still an echo's repaint");
            settle.Interrupted();
            Assert.AreEqual(CliPromptSettle.Outcome.Reading, settle.Observe(answer, false, 20, answer.Length), "the settle window starts");
            Assert.AreEqual(CliPromptSettle.Outcome.Settled, settle.Observe(answer, false, 30, answer.Length));
        }

        // ── D8: the flag supplier's and the window's refusals ──────────────────

        [TestMethod]
        public void VerbsAreDecidedInOnePlace()
        {
            Assert.AreEqual(CliReadShape.Table, CliVerbs.ReadShapeOf("print"));
            Assert.AreEqual(CliReadShape.Torch, CliVerbs.ReadShapeOf("torch"));
            Assert.AreEqual(CliReadShape.SelfTerminatingMonitor, CliVerbs.ReadShapeOf("ping"));
            Assert.AreEqual(CliReadShape.SelfTerminatingMonitor, CliVerbs.ReadShapeOf("traceroute"));
            Assert.AreEqual(CliReadShape.Monitor, CliVerbs.ReadShapeOf("monitor-traffic"));
            Assert.AreEqual(CliReadShape.Monitor, CliVerbs.ReadShapeOf("discover"));
            Assert.AreEqual(CliReadShape.Get, CliVerbs.ReadShapeOf("get"));
            Assert.AreEqual(CliReadShape.Action, CliVerbs.ReadShapeOf("run"));
            Assert.AreEqual(CliReadShape.EmptyRowAction, CliVerbs.ReadShapeOf("wol"));
            Assert.AreEqual(CliReadShape.ArgumentRead, CliVerbs.ReadShapeOf("inspect"));
            Assert.AreEqual(CliReadShape.RowWrite, CliVerbs.ReadShapeOf("set"));
            Assert.AreEqual(CliReadShape.Other, CliVerbs.ReadShapeOf("find"));
        }
    }
}
