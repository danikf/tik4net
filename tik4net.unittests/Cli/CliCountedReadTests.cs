using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// A whole-table CLI read states its own record count, and the rows read back are checked against it.
    /// </summary>
    /// <remarks>
    /// The command shape and the marker arithmetic were measured on RouterOS 7.24 before being encoded
    /// (Docs/findings-cli.md §1): <c>:put $d</c> and <c>:serialize to=json $d</c> are byte-identical to the
    /// direct forms; <c>[:len]</c> of a singleton counts its fields, and
    /// <c>[:typeof [:find $d [:pick $d 0]]]</c> answers <c>num</c> for a list and <c>nil</c> for a keyed record.
    /// </remarks>
    [TestClass]
    public class CliCountedReadTests
    {
        // ── The command ───────────────────────────────────────────────────────

        [TestMethod]
        public void ACountedReadBindsThePrintWritesItAndThenItsCount()
        {
            Assert.AreEqual(
                ":local d [/ip firewall mangle print as-value]; :put $d; "
                + ":put (\"#n=\" . [:len $d] . \"/\" . [:typeof [:find $d [:pick $d 0]]])",
                CliCommandBuilder.BuildCountedRead("/ip firewall mangle print as-value", asJson: false));
        }

        [TestMethod]
        public void TheJsonFormSerializesTheBoundValueInsteadOfThePrint()
        {
            Assert.AreEqual(
                ":local d [/file print detail as-value]; :put [:serialize to=json $d]; "
                + ":put (\"#n=\" . [:len $d] . \"/\" . [:typeof [:find $d [:pick $d 0]]])",
                CliCommandBuilder.BuildCountedRead("/file print detail as-value", asJson: true));
        }

        // ── What the marker means ─────────────────────────────────────────────

        [DataTestMethod]
        [DataRow("1672/num", 1672, DisplayName = "a list: its length is its rows")]
        [DataRow("1/num", 1, DisplayName = "a list filtered to one row")]
        [DataRow("0/nil", 0, DisplayName = "an empty table")]
        [DataRow("1/nil", 1, DisplayName = "a one-field singleton (/system identity)")]
        [DataRow("19/nil", 1, DisplayName = "a 19-field singleton is ONE record (/ip dns)")]
        [DataRow("5/str", -1, DisplayName = "a kind this builder never asks for")]
        [DataRow("x/num", -1, DisplayName = "not a number")]
        [DataRow("12", -1, DisplayName = "no kind")]
        public void TheMarkerStatesHowManyRecordsTheAnswerMustCarry(string tail, int expected)
        {
            Assert.AreEqual(expected, CliCommandBuilder.ExpectedRecordCount(tail));
        }

        // ── The check ─────────────────────────────────────────────────────────

        [TestMethod]
        public void ARowCountThatMatchesIsReturned()
        {
            using (var conn = Open(".id=*1;name=a;.id=*2;name=b" + Lf + "#n=2/num"))
            {
                Assert.AreEqual(2, conn.CreateCommand("/probe/print").ExecuteList().Count());
            }
        }

        [TestMethod]
        public void ASingletonIsOneRecordWhateverItsFieldCount()
        {
            using (var conn = Open("cache-size=2048;servers=;verify-doh-cert=false" + Lf + "#n=19/nil"))
            {
                Assert.AreEqual(1, conn.CreateCommand("/probe/print").ExecuteList().Count());
            }
        }

        [TestMethod]
        public void AnEmptyTableIsZeroRecords()
        {
            using (var conn = Open(Lf + "#n=0/nil"))
            {
                Assert.AreEqual(0, conn.CreateCommand("/probe/print").ExecuteList().Count());
            }
        }

        [TestMethod]
        public void AnAnswerShortOfTheRoutersCountIsRefused()
        {
            // The measured MAC-layer loss: a well-formed answer, ending at a prompt, rows missing.
            using (var conn = Open(".id=*1;name=a;.id=*3;name=c" + Lf + "#n=3/num"))
            {
                var ex = Assert.ThrowsException<TikConnectionResponseIncompleteException>(
                    () => conn.CreateCommand("/probe/print").ExecuteList().ToList());

                StringAssert.Contains(ex.Message, "counted 3 record(s)");
                StringAssert.Contains(ex.Message, "2 were read");
                Assert.AreEqual(0, ex.LostDatagrams);
            }
        }

        [TestMethod]
        public void AnAnswerWithoutItsCountIsRefused()
        {
            // The count is the last thing the router writes, so an answer without it has lost its end.
            using (var conn = Open(".id=*1;name=a"))
            {
                var ex = Assert.ThrowsException<TikConnectionResponseIncompleteException>(
                    () => conn.CreateCommand("/probe/print").ExecuteList().ToList());

                StringAssert.Contains(ex.Message, "without the router's count");
            }
        }

        [TestMethod]
        public void TheMarkerTextInsideAFieldIsNotTakenForTheCount()
        {
            // A comment that happens to read like the marker, and the real marker lost: the look-alike does
            // not start a line, so it must not be believed.
            using (var conn = Open(".id=*1;comment=#n=1/num"))
            {
                Assert.ThrowsException<TikConnectionResponseIncompleteException>(
                    () => conn.CreateCommand("/probe/print").ExecuteList().ToList());
            }
        }

        [TestMethod]
        public void TheJsonPathIsCountedToo()
        {
            using (var conn = Open("[{\".id\":\"*1\",\"name\":\"a\"}]" + Lf + "#n=2/num"))
            {
                var cmd = conn.CreateCommand("/probe/print");
                cmd.AddParameter(TikSpecialProperties.CliJson, "", TikCommandParameterFormat.NameValue);

                Assert.ThrowsException<TikConnectionResponseIncompleteException>(() => cmd.ExecuteList().ToList());
                StringAssert.Contains(conn.Sent.Single(), ":serialize to=json $d");
            }
        }

        // ── The double ────────────────────────────────────────────────────────

        private static readonly string Lf = ((char)10).ToString();

        private static ReplyingCliConnection Open(string reply)
        {
            var conn = new ReplyingCliConnection(reply);
            conn.OpenScripted();
            return conn;
        }

        /// <summary>Answers every command with one fixed reply, marker and all, exactly as given.</summary>
        private sealed class ReplyingCliConnection : CliConnectionBase
        {
            private readonly string _reply;
            public readonly List<string> Sent = new List<string>();

            public ReplyingCliConnection(string reply) => _reply = reply;

            protected override string TransportName => "Replying";

            public void OpenScripted()
                => OpenWith(_ => Task.FromResult(0),
                    (cliText, ct) => { Sent.Add(cliText); return Task.FromResult(_reply); },
                    (raw, ct) => Task.FromResult(string.Empty), () => { });

            public override void Open(string host, string user, string password) => OpenScripted();
            public override void Open(string host, int port, string user, string password) => OpenScripted();
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
        }
    }
}
