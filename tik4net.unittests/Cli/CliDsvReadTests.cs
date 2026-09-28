#nullable enable
// CliDsvReadTests.cs — the CLI read that keeps a ';' inside a value.
//
// as-value separates fields with ';' and writes values raw, so 'comment=a; b' reads as a list and 'a;b=c' as a comment
// 'a' plus a field 'b'. Where the router serializes (RouterOS 7), each row is written with
// ':serialize to=dsv delimiter="~^~" options=dsv.remap ({$r})' — its own field names, then its values. The answers
// below are what 7.24.4 and 7.21.5 printed for these rows (2026-09-28); 6.49.13 answers 'bad command name serialize'.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Objects;

namespace tik4net.unittests.Cli
{
    [TestClass]
    public class CliDsvReadTests
    {
        private const string Sep = TikConnectionSetup.DefaultCliFieldSeparator;

        // ── The parser ────────────────────────────────────────────────────────

        private const string SchedulerAnswer =
            ".id~^~comment~^~disabled~^~interval~^~name~^~policy\r\n"
            + "*0~^~a; b \"q\" c\\d;e=f~^~true~^~1d00:00:00~^~t4n-semi~^~ftp;reboot;read\r\n"
            + ".id~^~comment~^~disabled~^~interval~^~name~^~policy\r\n"
            + "*1~^~line1\nline2\ttab~^~true~^~00:05:00~^~t4n-semi2~^~ftp;reboot;read\r\n"
            + ".id~^~disabled~^~interval~^~name~^~policy\r\n"
            + "*2~^~true~^~00:05:00~^~t4n-semi3~^~ftp;reboot;read";

        [TestMethod]
        public void ACommentKeepsItsSemicolonsQuotesAndBackslashes()
        {
            var rows = CliDsvParser.Parse(SchedulerAnswer, Sep);

            Assert.AreEqual(3, rows.Count);
            Assert.AreEqual("a; b \"q\" c\\d;e=f", rows[0].Words["comment"]);
            Assert.IsFalse(rows[0].Words.ContainsKey("e"), "the text after a ';' in the comment is not a field");
            Assert.AreEqual("t4n-semi", rows[0].Words["name"]);
        }

        [TestMethod]
        public void ALineBreakInAValueDoesNotEndTheRow()
        {
            var rows = CliDsvParser.Parse(SchedulerAnswer, Sep);

            Assert.AreEqual("line1\nline2\ttab", rows[1].Words["comment"]);
            Assert.AreEqual("t4n-semi2", rows[1].Words["name"]);
        }

        [TestMethod]
        public void AFieldTheRowDoesNotHaveIsAbsentNotEmpty()
        {
            // Serialized alone, a row names only its own fields; over the whole array dsv.remap wrote one header and
            // an empty cell for every field a row lacked.
            var rows = CliDsvParser.Parse(SchedulerAnswer, Sep);

            Assert.IsFalse(rows[2].Words.ContainsKey("comment"));
        }

        [TestMethod]
        public void ValuesAreSpelledAsTheApiSpellsThem()
        {
            // The same spellings as-value writes, through the same normaliser: a list is joined with ',' as the API
            // prints it, and a duration is the API's compact form.
            var rows = CliDsvParser.Parse(SchedulerAnswer, Sep);

            Assert.AreEqual("ftp,reboot,read", rows[0].Words["policy"]);
            Assert.AreEqual("1d", rows[0].Words["interval"]);
            Assert.AreEqual("5m", rows[1].Words["interval"]);
        }

        [TestMethod]
        public void AValueHoldingTheSeparatorIsRefusedNotShifted()
        {
            var ex = Assert.ThrowsException<TikSentenceException>(() =>
                CliDsvParser.Parse(".id~^~comment~^~name\r\n*1~^~a~^~b~^~x", Sep));
            StringAssert.Contains(ex.Message, "CliFieldSeparator");
        }

        // ── The separator ─────────────────────────────────────────────────────

        [DataTestMethod]
        [DataRow("")]
        [DataRow("~^~^")]      // RouterOS: "value of delimiter should not be longer than 3"
        [DataRow("\t")]        // a control character: "invalid delimiter"
        [DataRow("a\"b")]
        [DataRow("\\")]
        [DataRow("$x")]
        [DataRow(";")]
        public void ASeparatorRouterOsOrTheCommandLineCannotTakeIsRefused(string separator)
        {
            using (var conn = new DsvRouter(refusesSerialize: false))
                Assert.ThrowsException<ArgumentException>(() => conn.CliFieldSeparator = separator);
        }

        [TestMethod]
        public void TheSeparatorDefaultsToTheSetupsDefault_AndNullMeansAsValueOnly()
        {
            using (var conn = new DsvRouter(refusesSerialize: false))
            {
                Assert.AreEqual(TikConnectionSetup.DefaultCliFieldSeparator, conn.CliFieldSeparator);
                conn.CliFieldSeparator = null;
                Assert.IsNull(conn.CliFieldSeparator);
            }
        }

        // ── The read ──────────────────────────────────────────────────────────

        [TikEntity("/box")]
        private sealed class Box
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)] public string? Id { get; private set; }
            [TikProperty("comment")] public string? Comment { get; set; }
            [TikProperty("name")] public string? Name { get; set; }
        }

        [TestMethod]
        public void OnRouterOs7_AWholeTableReadIsDsv_AndTheCommentArrivesWhole()
        {
            using (var conn = new DsvRouter(refusesSerialize: false))
            {
                conn.OpenScripted();

                var rows = conn.LoadAll<Box>().ToList();

                Assert.AreEqual("a; b", rows[0].Comment);
                Assert.AreEqual("", rows[1].Comment, "on 4.0 a field the row does not have reads as its default");
                Assert.AreEqual(2, conn.Sent.Count, "the probe, then the read: " + string.Join(" | ", conn.Sent));
                Assert.AreEqual(CliCommandBuilder.BuildDsvProbe(Sep), conn.Sent[0]);
                StringAssert.Contains(conn.Sent[1], ":serialize to=dsv delimiter=\"~^~\" options=dsv.remap ({$r})");
                conn.Sent.Clear();
                conn.LoadAll<Box>().ToList();
                Assert.AreEqual(1, conn.Sent.Count, "asked once per connection: " + string.Join(" | ", conn.Sent));
            }
        }

        [TestMethod]
        public void OnRouterOs7_AWindowIsDsvToo()
        {
            using (var conn = new DsvRouter(refusesSerialize: false))
            {
                conn.OpenScripted();
                conn.CliReadPageSize = 5;

                var rows = conn.LoadAll<Box>().ToList();

                Assert.AreEqual("a; b", rows[0].Comment);
                StringAssert.Contains(conn.Sent[1], ":pick [/box find] 0 5");
                StringAssert.Contains(conn.Sent[1], ":serialize to=dsv");
            }
        }

        [TestMethod]
        public void OnRouterOs6_TheShortProbeIsRefused_AndEveryReadIsAsValue()
        {
            // 6.x repaints the typed line per character, so the ~300-character DSV read is never tried there: over the
            // RoMON relay its echo spoiled the as-value read that followed. The ~55-character probe settles it.
            using (var conn = new DsvRouter(refusesSerialize: true))
            {
                conn.OpenScripted();

                Assert.AreEqual(2, conn.LoadAll<Box>().Count());
                Assert.AreEqual(2, conn.Sent.Count, "the probe, then as-value: " + string.Join(" | ", conn.Sent));
                Assert.IsFalse(conn.Sent[1].Contains(":serialize"), conn.Sent[1]);
                conn.Sent.Clear();
                Assert.AreEqual(2, conn.LoadAll<Box>().Count());

                Assert.AreEqual(1, conn.Sent.Count, "the second read goes straight to as-value: " + string.Join(" | ", conn.Sent));
                Assert.IsFalse(conn.Sent[0].Contains(":serialize"), conn.Sent[0]);
            }
        }

        [TestMethod]
        public void OnRouterOs6_AWindowIsAsValue_AndStillWindowed()
        {
            using (var conn = new DsvRouter(refusesSerialize: true))
            {
                conn.OpenScripted();
                conn.CliReadPageSize = 5;

                Assert.AreEqual(2, conn.LoadAll<Box>().Count());
                Assert.IsTrue(conn.Sent.Last().Contains(":pick [/box find]"), "still windowed: " + conn.Sent.Last());
            }
        }

        [TestMethod]
        public void ASeparatorTheRouterRefuses_IsNotUsed()
        {
            Assert.IsFalse(CliCommandBuilder.IsDsvProbeAccepted("invalid delimiter"));
            Assert.IsFalse(CliCommandBuilder.IsDsvProbeAccepted("bad command name serialize (line 1 column 17)"));
            Assert.IsTrue(CliCommandBuilder.IsDsvProbeAccepted("str\r\n"));
        }

        [TestMethod]
        public void WithTheSeparatorNull_TheReadIsAsValue()
        {
            using (var conn = new DsvRouter(refusesSerialize: false) { CliFieldSeparator = null })
            {
                conn.OpenScripted();

                conn.LoadAll<Box>().ToList();

                Assert.IsTrue(conn.Sent.All(s => !s.Contains(":serialize")), string.Join(" | ", conn.Sent));
            }
        }

        /// <summary>
        /// Two rows of /box, the first with a comment holding ';'. Answers the DSV form as RouterOS 7 does, or refuses
        /// it as 6.49.13 does; the as-value form is answered either way.
        /// </summary>
        private sealed class DsvRouter : CliConnectionBase
        {
            private readonly bool _refusesSerialize;
            public readonly List<string> Sent = new List<string>();

            public DsvRouter(bool refusesSerialize) => _refusesSerialize = refusesSerialize;

            protected override string TransportName => "Dsv";

            public void OpenScripted()
                => OpenWith(_ => Task.FromResult(0), SendAsync, (raw, ct) => Task.FromResult(string.Empty), () => { });

            private Task<string> SendAsync(string cliText, CancellationToken ct)
            {
                Sent.Add(cliText);
                if (cliText == CliCommandBuilder.BuildDsvProbe(Sep))
                    return Task.FromResult(_refusesSerialize ? "bad command name serialize (line 1 column 17)" : "str");
                bool dsv = cliText.Contains(":serialize to=dsv");
                if (dsv && _refusesSerialize)
                    return Task.FromResult("bad command name serialize (line 1 column 60)");

                string body = dsv
                    ? ".id~^~comment~^~name\r\n*1~^~a; b~^~one\r\n.id~^~name\r\n*2~^~two"
                    : ".id=*1;comment=a;name=one;.id=*2;name=two";
                bool windowed = Regex.IsMatch(cliText, @":pick \[/box find\] 0 \d+\]");
                return Task.FromResult(body + "\r\n" + (windowed ? "#w=2" : "#n=2/num"));
            }

            public override void Open(string host, string user, string password) => OpenScripted();
            public override void Open(string host, int port, string user, string password) => OpenScripted();
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
        }
    }
}
