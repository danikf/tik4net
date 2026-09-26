// CliProplistReadTests.cs — a CLI read honours .proplist the way the binary API does.
//
// Measured on RouterOS 7.24.2 over Telnet before being encoded:
//   :put [/interface ethernet print as-value]                      -> summary columns, no disable-running-check
//   :put [/interface ethernet print detail as-value]               -> every field
//   :put [/interface ethernet print as-value proplist=name,bogus]  -> input does not match any value of value-name
//   :put [/ip dns print detail as-value]                           -> bad parameter detail (line 1 column 27)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    [TestClass]
    public class CliProplistReadTests
    {
        private static readonly string Lf = ((char)10).ToString();

        [TestMethod]
        public void AProplistReadAsksForDetailAndReturnsOnlyTheListedFields()
        {
            using (var conn = Open(cli => cli.Contains(" detail ")
                ? ".id=*2;arp=enabled;disable-running-check=false;name=ether1;running=true" + Lf + "#n=1/num"
                : ".id=*2;arp=enabled;name=ether1;running=true" + Lf + "#n=1/num"))
            {
                var cmd = conn.CreateCommand("/interface/ethernet/print");
                cmd.AddParameter(TikSpecialProperties.Proplist, "name,disable-running-check,no-such-field",
                    TikCommandParameterFormat.NameValue);

                var row = cmd.ExecuteList().Single();

                Assert.AreEqual("ether1", row.GetResponseField("name"));
                Assert.AreEqual("false", row.GetResponseField("disable-running-check"),
                    "a field outside the summary columns is there because the read asked for detail");
                Assert.IsFalse(row.Words.ContainsKey("arp"), "a field the caller did not list is not returned");
                Assert.IsFalse(row.Words.ContainsKey(".id"), "nor is .id unless listed — the API's contract");
                Assert.IsFalse(conn.Sent.Any(s => s.Contains("proplist")),
                    "the CLI's proplist= would refuse the unknown name, so it never reaches the wire");
            }
        }

        [TestMethod]
        public void ASingletonThatRefusesDetailIsReadWithoutIt()
        {
            using (var conn = Open(cli => cli.Contains(" detail ")
                ? "bad parameter detail (line 1 column 27)"
                : "cache-size=2048;servers=;allow-remote-requests=false" + Lf + "#n=3/nil"))
            {
                var cmd = conn.CreateCommand("/ip/dns/print");
                cmd.AddParameter(TikSpecialProperties.Proplist, "cache-size", TikCommandParameterFormat.NameValue);

                var row = cmd.ExecuteList().Single();

                Assert.AreEqual("2048", row.GetResponseField("cache-size"));
                Assert.AreEqual(1, row.Words.Count);
                Assert.AreEqual(2, conn.Sent.Count, "one refused read with detail, one without");
            }
        }

        [TestMethod]
        public void AnIncompleteAnswerThatIsNotTheDetailRefusalIsStillRefused()
        {
            // The retry is bound to the router's refusal of 'detail', not to any short answer.
            using (var conn = Open(cli => ".id=*1;name=a"))
            {
                var cmd = conn.CreateCommand("/probe/print");
                cmd.AddParameter(TikSpecialProperties.Proplist, "name", TikCommandParameterFormat.NameValue);

                Assert.ThrowsException<TikConnectionResponseIncompleteException>(() => cmd.ExecuteList().ToList());
                Assert.AreEqual(1, conn.Sent.Count);
            }
        }

        [TestMethod]
        public void AReadWithoutProplistIsUnchanged()
        {
            using (var conn = Open(cli => ".id=*2;arp=enabled;name=ether1" + Lf + "#n=1/num"))
            {
                var row = conn.CreateCommand("/interface/ethernet/print").ExecuteList().Single();

                Assert.AreEqual("enabled", row.GetResponseField("arp"));
                Assert.IsFalse(conn.Sent.Single().Contains(" detail "));
            }
        }

        // ── The double ────────────────────────────────────────────────────────

        private static AnsweringCliConnection Open(Func<string, string> answer)
        {
            var conn = new AnsweringCliConnection(answer);
            conn.OpenScripted();
            return conn;
        }

        /// <summary>
        /// Answers each command with whatever <c>answer</c> makes of its text — except the once-per-connection flags
        /// probe, answered as 7.24 does (flags in as-value), since that is the version these tests were measured on.
        /// </summary>
        private sealed class AnsweringCliConnection : CliConnectionBase
        {
            private readonly Func<string, string> _answer;
            public readonly List<string> Sent = new List<string>();

            public AnsweringCliConnection(Func<string, string> answer) => _answer = answer;

            protected override string TransportName => "Answering";

            public void OpenScripted()
                => OpenWith(_ => Task.FromResult(0),
                    (cliText, ct) =>
                    {
                        Sent.Add(cliText);
                        return Task.FromResult(cliText == CliCommandBuilder.FlagsProbe
                            ? ".id=*1;address=;disabled=false;invalid=false;name=ftp;port=21"
                            : _answer(cliText));
                    },
                    (raw, ct) => Task.FromResult(string.Empty), () => { });

            public override void Open(string host, string user, string password) => OpenScripted();
            public override void Open(string host, int port, string user, string password) => OpenScripted();
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
        }
    }
}
