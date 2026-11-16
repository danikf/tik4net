// EntityTransportCheckTests.cs — TikEntityTransportCheck finds the silent gaps a custom entity has on a transport other
// than the one it was written against, and names the option that fixes them.

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Objects;
using tik4net.Testing;
using tik4net.unittests.Cli;

namespace tik4net.unittests.Testing
{
    [TestClass]
    public class EntityTransportCheckTests
    {
        [TikEntity("/box")]
        public class Box
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; private set; }

            [TikProperty("name")]
            public TikField<string?> Name { get; set; }

            [TikProperty("secret")]
            public TikField<string?> Secret { get; set; }

            [TikProperty("hidden")]
            public TikField<bool?> Hidden { get; set; }
        }

        [TikEntity("/box")]
        public class SensitiveBox
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; private set; }

            [TikProperty("name")]
            public TikField<string?> Name { get; set; }

            [TikProperty("secret", IsSensitive = true)]
            public TikField<string?> Secret { get; set; }
        }

        private static TikFakeConnection Fake(params Dictionary<string, string>[] rows)
            => new TikFakeConnection().WithResponse(cmd => cmd.First() == "/box/print",
                _ => rows.Select(r => (ITikSentence)new TikFakeReSentence(r)).Concat(new[] { new TikFakeDoneSentence() }).ToArray());

        private static Dictionary<string, string> Row(params (string Key, string Value)[] words)
            => words.ToDictionary(w => w.Key, w => w.Value);

        [TestMethod]
        public void TheSameRows_AreNoFinding()
        {
            var a = Fake(Row((".id", "*1"), ("name", "a"), ("secret", "s"), ("hidden", "true")));
            var b = Fake(Row((".id", "*1"), ("name", "a"), ("secret", "s"), ("hidden", "true")));

            var report = TikEntityTransportCheck.Compare<Box>(a, b);

            Assert.IsFalse(report.HasFindings, report.ToString());
            report.ThrowIfAnyFinding();
            StringAssert.Contains(report.ToString(), "no difference");
            Assert.AreEqual(1, report.ReferenceRowCount);
        }

        [TestMethod]
        public void AFieldNoRowCarries_IsAbsent_AndTheCheckThrowsNamingIt()
        {
            var api = Fake(Row((".id", "*1"), ("name", "a"), ("secret", "s")));
            var other = Fake(Row((".id", "*1"), ("name", "a")));

            var report = TikEntityTransportCheck.Compare<Box>(api, other);

            var finding = report.Findings.Single();
            Assert.AreEqual(TikEntityTransportFindingKind.AbsentFromEveryRow, finding.Kind);
            Assert.AreEqual("secret", finding.FieldName);
            Assert.AreEqual("Secret", finding.PropertyName);
            var ex = Assert.ThrowsException<InvalidOperationException>(() => report.ThrowIfAnyFinding());
            StringAssert.Contains(ex.Message, "secret");
        }

        [TestMethod]
        public void AWritableValueThatDiffers_IsNamedWithItsRow()
        {
            var api = Fake(Row((".id", "*1"), ("name", "a;b")), Row((".id", "*2"), ("name", "c")));
            var other = Fake(Row((".id", "*2"), ("name", "c")), Row((".id", "*1"), ("name", "a")));

            var finding = TikEntityTransportCheck.Compare<Box>(api, other).Findings.Single();

            Assert.AreEqual(TikEntityTransportFindingKind.ValueDiffers, finding.Kind);
            Assert.AreEqual("*1", finding.RowId, "rows are paired by .id, not by position");
            Assert.AreEqual("a;b", finding.ReferenceValue);
            Assert.AreEqual("a", finding.Value);
        }

        [TestMethod]
        public void ALongOrBinaryValue_IsPrintedOnOneShortLine_AndKeptWhole()
        {
            string body = "GIF89a" + (char)0 + (char)1 + "\n" + new string('x', 200);
            var api = Fake(Row((".id", "*1"), ("name", body)));
            var other = Fake(Row((".id", "*1"), ("name", "short")));

            var report = TikEntityTransportCheck.Compare<Box>(api, other);

            string line = report.Findings.Single().ToString();
            StringAssert.Contains(line, @"GIF89a\x00\x01\n");
            StringAssert.Contains(line, "(" + body.Length + " chars)");
            Assert.IsFalse(line.Contains("\n") || line.Contains(((char)0).ToString()), line);
            Assert.AreEqual(body, report.Findings.Single().ReferenceValue, "the property keeps the whole value");
        }

        [TestMethod]
        public void ASecretsValue_IsNeverPrinted()
        {
            var api = Fake(Row((".id", "*1"), ("name", "a"), ("secret", "hunter2")));
            var other = Fake(Row((".id", "*1"), ("name", "a"), ("secret", "other")));

            var report = TikEntityTransportCheck.Compare<SensitiveBox>(api, other);

            Assert.AreEqual(TikEntityTransportFindingKind.ValueDiffers, report.Findings.Single().Kind);
            Assert.IsFalse(report.ToString().Contains("hunter2") || report.ToString().Contains("other\""), report.ToString());
        }

        [TestMethod]
        public void AnEmptyBoolOnTheReferenceOnly_SuggestsIsPresenceFlag()
        {
            var api = Fake(Row((".id", "*1"), ("name", "a"), ("hidden", "")));
            var ssh = Fake(Row((".id", "*1"), ("name", "a"), ("hidden", "true")));

            var finding = TikEntityTransportCheck.Compare<Box>(api, ssh).Findings.Single();

            Assert.AreEqual(TikEntityTransportFindingKind.UnparsedOnTheReferenceOnly, finding.Kind);
            Assert.AreEqual("hidden", finding.FieldName);
            StringAssert.Contains(finding.Suggestion, "IsPresenceFlag");
        }

        [TestMethod]
        public void AReadThatFails_IsAFinding_AndTheOthersAreStillCompared()
        {
            var api = Fake(Row((".id", "*1"), ("name", "a")));
            var broken = new TikFakeConnection();   // answers nothing: the read throws
            var fine = Fake(Row((".id", "*1"), ("name", "a")));

            var report = TikEntityTransportCheck.Compare<Box>(api, broken, fine);

            Assert.AreEqual(TikEntityTransportFindingKind.ReadFailed, report.Findings.Single().Kind);
            Assert.AreEqual(2, report.Compared.Count);
        }

        [TestMethod]
        public void TheConnectionsOwnDiagnosticsSink_IsPutBack()
        {
            var api = Fake(Row((".id", "*1"), ("name", "a")));
            var other = Fake(Row((".id", "*1"), ("name", "a")));
            var reports = new List<TikEntityReadReport>();
            other.SetEntityDiagnostics(reports.Add);
            var sink = other.GetEntityDiagnostics();

            TikEntityTransportCheck.Compare<Box>(api, other);

            Assert.AreSame(sink, other.GetEntityDiagnostics());
            Assert.IsNull(api.GetEntityDiagnostics(), "a connection without a sink is left without one");
            Assert.AreEqual(0, reports.Count, "the check's own read is not reported to the caller's sink");
        }

        [TestMethod]
        public void ASecretTheCliLeavesOut_SuggestsIsSensitive()
        {
            // RouterOS 7 prints a secret over a terminal only when asked with show-sensitive.
            var api = Fake(Row((".id", "*1"), ("name", "a"), ("secret", "s")), Row((".id", "*2"), ("name", "b"), ("secret", "t")));
            using (var telnet = new HidingSecretsRouter())
            {
                telnet.OpenScripted();

                var finding = TikEntityTransportCheck.Compare<Box>(api, telnet).Findings.Single();

                Assert.AreEqual(TikEntityTransportFindingKind.AbsentFromEveryRow, finding.Kind);
                Assert.AreEqual(TikConnectionType.Telnet, finding.ConnectionType);
                Assert.AreEqual("Telnet", finding.Transport);
                StringAssert.Contains(finding.Suggestion, "IsSensitive");
                StringAssert.Contains(finding.Suggestion, "IncludeCliStats");

                // Declared, the same entity reads its secret and the check is clean.
                var declared = TikEntityTransportCheck.Compare<SensitiveBox>(api, telnet);
                Assert.IsFalse(declared.HasFindings, declared.ToString());
            }
        }

        [TestMethod]
        public void Arguments_AreChecked()
        {
            var api = new TikFakeConnection();
            Assert.ThrowsException<ArgumentNullException>(() => TikEntityTransportCheck.Compare<Box>(null!, api));
            Assert.ThrowsException<ArgumentException>(() => TikEntityTransportCheck.Compare<Box>(api));
        }

        // A RouterOS 7 /box over Telnet: two rows, whose secret it prints only with show-sensitive.
        private sealed class HidingSecretsRouter : ScriptedCliConnectionBase
        {
            public HidingSecretsRouter() : base(TikConnectionType.Telnet)
            {
                CliFieldSeparator = null;   // scripts the as-value read
            }

            public void OpenScripted()
                => OpenScripted(_ => Task.FromResult(0), SendAsync, (raw, ct) => Task.FromResult(string.Empty), () => { });

            private static Task<string> SendAsync(string cliText, CancellationToken ct)
            {
                if (cliText == CliCommandBuilder.FlagsProbe)
                    return Task.FromResult(".id=*1;disabled=false;name=ftp");
                if (!cliText.Contains("/box print"))
                    return Task.FromResult("expected end of command (line 1 column 40)");
                bool secret = cliText.Contains("show-sensitive");
                var rows = new[] { ".id=*1;name=a" + (secret ? ";secret=s" : ""), ".id=*2;name=b" + (secret ? ";secret=t" : "") };
                var pick = Regex.Match(cliText, @":pick \[[^\[\]]*? find(?: \([^)]*\))?\] (?<from>\d+) (?<to>\d+)\]");
                if (pick.Success)
                {
                    int from = int.Parse(pick.Groups["from"].Value), to = int.Parse(pick.Groups["to"].Value);
                    var window = rows.Skip(from).Take(to - from).ToList();
                    return Task.FromResult(string.Join(";", window) + (char)10 + "#w=" + window.Count);
                }
                return Task.FromResult(CountedReadFake.Answer(cliText, string.Join(";", rows)));
            }

            public override void Open(string host, string user, string password) => OpenScripted();
            public override void Open(string host, int port, string user, string password) => OpenScripted();
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
        }
    }
}
