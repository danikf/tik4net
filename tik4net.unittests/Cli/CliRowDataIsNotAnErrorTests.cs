#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// An answer that ran to its end is data, whatever its rows say. A refused script stops before its count marker, so
    /// the marker tells the two apart where a phrase in a comment cannot.
    /// </summary>
    [TestClass]
    public class CliRowDataIsNotAnErrorTests
    {
        private sealed class Router : ScriptedCliConnectionBase
        {
            internal readonly List<string> Sent = new List<string>();

            internal Router(Func<string, string> answer, int pageSize = 0, string? separator = null)
            {
                CliFieldSeparator = separator;
                CliReadPageSize = pageSize;
                OpenScripted(_ => Task.FromResult(0),
                    (line, ct) => { Sent.Add(line); return Task.FromResult(answer(line)); },
                    (raw, ct) => Task.FromResult(string.Empty), () => { });
            }

            public override void Open(string host, string user, string password) { }
            public override void Open(string host, int port, string user, string password) { }
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) => Task.FromResult(0);
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) => Task.FromResult(0);
        }

        // The flags probe and anything else the connection asks first is answered with a row that needs nothing more.
        private static Func<string, string> Answering(string row, string marker)
            => line => line.StartsWith(":local d [") || line.StartsWith(":local w [") ? row + "\r\n" + marker : ".id=*1;disabled=false";

        [DataTestMethod]
        [DataRow("no such item")]
        [DataRow("syntax error")]
        [DataRow("error: x")]
        [DataRow("failure: x")]
        public void ACountedReadKeepsARowWhoseCommentSoundsLikeAnError(string comment)
        {
            using var router = new Router(Answering(".id=*1;comment=" + comment + ";disabled=false", "#n=1/num"));

            var row = router.CreateCommand("/interface/list/print").ExecuteList().Single();

            Assert.AreEqual(comment, row.GetResponseField("comment"));
        }

        [TestMethod]
        public void AWindowKeepsARowWhoseCommentSoundsLikeAnError()
        {
            using var router = new Router(Answering(".id=*1;comment=no such item;disabled=false", "#w=1"), pageSize: 20);

            var row = router.CreateCommand("/interface/list/print").ExecuteList().Single();

            Assert.AreEqual("no such item", row.GetResponseField("comment"));
            Assert.IsFalse(router.Sent.Any(l => l.StartsWith(":local d [")), "the window answered, so the read was not retaken in one command");
        }

        [TestMethod]
        public void ADsvReadKeepsARowWhoseCommentSoundsLikeAnError()
        {
            using var router = new Router(line =>
                    line.StartsWith(":put [:typeof [:serialize") ? "str"
                    : line.StartsWith(":local d [") ? ".id~^~comment\r\n*1~^~no such item\r\n#n=1/num"
                    : ".id=*1;disabled=false",
                separator: "~^~");

            var row = router.CreateCommand("/interface/list/print").ExecuteList().Single();

            Assert.AreEqual("no such item", row.GetResponseField("comment"));
        }

        [TestMethod]
        public void AScalarOrDefaultDoesNotTurnARowIntoNotFound()
        {
            using var router = new Router(Answering(".id=*1;comment=no such item", "#n=1/num"));

            Assert.AreEqual("no such item", router.CreateCommand("/interface/list/print").ExecuteScalarOrDefault("(not found)", "comment"));
        }

        [TestMethod]
        public void AMonitorSnapshotWhoseRowSoundsLikeAnErrorIsARow()
        {
            using var router = new Router(line => "status=no such item;rate=5");

            var command = router.CreateCommandAndParameters("/interface/monitor-traffic", TikCommandParameterFormat.NameValue, "interface", "ether1");

            Assert.AreEqual("no such item", command.ExecuteList().Single().GetResponseField("status"));
        }

        // ── A refusal still is one ─────────────────────────────────────────────

        [DataTestMethod]
        [DataRow("no such item (4)", typeof(TikNoSuchItemException))]
        [DataRow("bad command name nosuch (line 1 column 9)", typeof(TikNoSuchCommandException))]
        [DataRow("failure: x", typeof(TikCommandTrapException))]
        public void AnAnswerWithNoMarkerIsTheRefusalItSays(string answer, Type expected)
        {
            using var router = new Router(line => line.StartsWith(":local d [") ? answer : ".id=*1;disabled=false");

            try
            {
                router.CreateCommand("/interface/list/print").ExecuteList();
                Assert.Fail("the refusal did not throw");
            }
            catch (TikCommandTrapException ex)
            {
                Assert.AreEqual(expected, ex.GetType());
            }
        }

        [TestMethod]
        public void AMonitorRefusalThatLooksLikeAFieldIsStillARefusal()
        {
            using var router = new Router(line => "failure: already have such address=1.2.3.4");

            var command = router.CreateCommandAndParameters("/interface/monitor-traffic", TikCommandParameterFormat.NameValue, "interface", "ether1");

            Assert.ThrowsException<TikAlreadyHaveSuchItemException>(() => command.ExecuteList());
        }

        [TestMethod]
        public void AGetKeepsThePhraseCheck()
        {
            // One line has no shape to tell a value from a refusal, so a value reading "no such item" is reported as one.
            using var router = new Router(line => "no such item");

            Assert.ThrowsException<TikNoSuchItemException>(
                () => router.CreateCommandAndParameters("/system/identity/get", TikCommandParameterFormat.NameValue, "value-name", "name").ExecuteList());
        }

        // ── X4, X5, X6: what is learnt from a refusal ──────────────────────────

        [TestMethod]
        public void OnlyAMissingFindSwitchesPagingOff()
        {
            int windows = 0;
            using var router = new Router(line =>
            {
                if (!line.StartsWith(":local w [")) return "#n=0/nil";
                windows++;
                return "expected end of command (line 1 column 40)";
            }, pageSize: 20);

            Assert.ThrowsException<TikNoSuchCommandException>(() => router.CreateCommand("/ip/route/print").ExecuteList());
            Assert.ThrowsException<TikNoSuchCommandException>(() => router.CreateCommand("/ip/route/print").ExecuteList());

            Assert.AreEqual(2, windows, "the refusal said nothing about 'find', so the second read windows again");
        }

        [TestMethod]
        public void AMenuWithNoFindIsReadInOneCommand()
        {
            using var router = new Router(line => line.StartsWith(":local w [")
                ? "bad command name find (line 1 column 35)"
                : "name=router\r\n#n=1/nil", pageSize: 20);

            Assert.AreEqual("router", router.CreateCommand("/system/identity/print").ExecuteList().Single().GetResponseField("name"));
            router.Sent.Clear();
            router.CreateCommand("/system/identity/print").ExecuteList();
            Assert.IsFalse(router.Sent.Any(l => l.StartsWith(":local w [")), "remembered");
        }

        [TestMethod]
        public void AFailedJsonWindowConcludesNothingUnlessThePlainFormWorks()
        {
            int jsonWindows = 0;
            using var router = new Router(line =>
            {
                if (!line.StartsWith(":local w [")) return "#n=0/nil";
                if (line.Contains("to=json")) jsonWindows++;
                return "bad filter (line 1 column 9)";
            }, pageSize: 20);
            var read = new Func<ITikCommand>(() =>
            {
                var c = router.CreateCommand("/system/script/print");
                c.AddParameter(TikSpecialProperties.CliJson, "source", TikCommandParameterFormat.NameValue);
                return c;
            });

            Assert.ThrowsException<TikCommandTrapException>(() => read().ExecuteList());
            Assert.ThrowsException<TikCommandTrapException>(() => read().ExecuteList());

            Assert.AreEqual(2, jsonWindows, "both forms failed, so whether the router serializes is still unknown");
        }

        // ── X9: the marker's trim ──────────────────────────────────────────────

        [DataTestMethod]
        [DataRow("trailing;", "#n=1/num", ":local d [")]
        [DataRow("trailing ", "#n=1/num", ":local d [")]
        [DataRow("trailing;", "#w=1", ":local w [")]
        [DataRow("trailing ", "#w=1", ":local w [")]
        public void TheLastValueKeepsItsOwnEnd(string comment, string marker, string form)
        {
            // One DSV block: the names, then the row whose value ends in the character the trim used to eat.
            using var router = new Router(line => line.StartsWith(form)
                    ? ".id~^~comment\r\n*1~^~" + comment + "\r\n" + marker
                    : line.StartsWith(":put [:typeof") ? "str" : ".id=*1;disabled=false",
                pageSize: form == ":local w [" ? 20 : 0, separator: "~^~");

            var row = router.CreateCommand("/interface/list/print").ExecuteList().Single();

            Assert.AreEqual(comment, row.GetResponseField("comment"));
        }

        // ── X8: as-value has no quoting ────────────────────────────────────────

        [TestMethod]
        public void AValueStartingWithAQuoteDoesNotSwallowTheRows()
        {
            // 7.25, 7.21.5 and 6.49.21 all write it so: the router adds no closing quote and no escape.
            var rows = CliOutputParser.ParseAsValue(".id=*1;comment=\"a;name=x;.id=*2;comment=b;name=y");

            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("x", rows[0].GetResponseField("name"));
            Assert.AreEqual("y", rows[1].GetResponseField("name"));
        }
    }
}
