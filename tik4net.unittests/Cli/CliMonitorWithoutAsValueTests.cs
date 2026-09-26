// Nullable-enabled on its own: the test project as a whole is not (see the note in Directory.Build.props).
#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Objects;
using tik4net.Objects.Tool;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// A one-shot monitor on a router whose command has no <c>as-value</c>. RouterOS 6.49.13 answers
    /// <c>:put [/ping address=… count=2 as-value]</c> with <c>expected end of command (line 1 column 43)</c> —
    /// column 43 is <c>as-value</c> — and <c>/tool traceroute</c> the same; the plain command prints its table.
    /// So every CLI transport failed a ping on 6.x (Docs/findings-routeros-6.md §8). The double answers with the
    /// table measured on 6.49.13.
    /// </summary>
    [TestClass]
    public class CliMonitorWithoutAsValueTests
    {
        // Measured on 6.49.13 over Telnet: '/ping address=192.168.4.237 count=2'.
        private const string SixPingTable =
            "/ping address=10.0.0.1 count=2\r\n"
            + "  SEQ HOST                                     SIZE TTL TIME  STATUS           \r\n"
            + "    0 10.0.0.1                                   56  64 0ms  \r\n"
            + "    1 10.0.0.1                                   56  64 0ms  \r\n"
            + "    sent=2 received=2 packet-loss=0% min-rtt=0ms avg-rtt=0ms max-rtt=0ms \r\n";

        [TestMethod]
        public void RouterOs6_APingIsReadFromThePlainTable()
        {
            using (var conn = new SixPingRouter())
            {
                conn.OpenScripted();

                var rows = conn.LoadList<ToolPing>(
                    conn.CreateParameter("address", "10.0.0.1"),
                    conn.CreateParameter("count", "2")).ToList();

                Assert.AreEqual(2, rows.Count, string.Join(" | ", conn.Sent));
                CollectionAssert.AreEqual(new[] { "10.0.0.1", "10.0.0.1" }, rows.Select(r => r.Host.Value).ToList());
            }
        }

        [TestMethod]
        public void RouterOs6_TheRefusalIsRemembered_SoTheNextPingGoesStraightToThePlainForm()
        {
            using (var conn = new SixPingRouter())
            {
                conn.OpenScripted();
                conn.LoadList<ToolPing>(conn.CreateParameter("address", "10.0.0.1"), conn.CreateParameter("count", "2")).ToList();
                int first = conn.Sent.Count;

                conn.LoadList<ToolPing>(conn.CreateParameter("address", "10.0.0.1"), conn.CreateParameter("count", "2")).ToList();

                Assert.AreEqual(1, conn.Sent.Count - first, string.Join(" | ", conn.Sent.Skip(first)));
                Assert.IsFalse(conn.Sent.Last().Contains("as-value"), conn.Sent.Last());
            }
        }

        [TestMethod]
        public void ARefusalOfTheInputsThemselves_StillReachesTheCaller()
        {
            // The plain form refused as well: the as-value form was not the problem, and the caller sees why.
            using (var conn = new SixPingRouter { RefuseEverything = true })
            {
                conn.OpenScripted();

                try
                {
                    conn.LoadList<ToolPing>(conn.CreateParameter("address", "10.0.0.1"), conn.CreateParameter("count", "2")).ToList();
                    Assert.Fail("a refused command returned rows");
                }
                catch (TikCommandTrapException ex)
                {
                    StringAssert.Contains(ex.Message, "expected end of command");
                }
            }
        }

        private sealed class SixPingRouter : CliConnectionBase
        {
            public readonly List<string> Sent = new List<string>();
            public bool RefuseEverything;

            protected override string TransportName => "Six";

            public void OpenScripted()
                => OpenWith(_ => Task.FromResult(0), SendAsync, (raw, ct) => Task.FromResult(string.Empty), () => { });

            private Task<string> SendAsync(string cliText, CancellationToken ct)
            {
                Sent.Add(cliText);
                if (RefuseEverything || cliText.Contains("as-value"))
                    return Task.FromResult("expected end of command (line 1 column 43)");
                if (cliText == "/ping address=10.0.0.1 count=2")
                    return Task.FromResult(SixPingTable);
                return Task.FromResult("bad command name " + cliText + " (line 1 column 2)");
            }

            public override void Open(string host, string user, string password) => OpenScripted();
            public override void Open(string host, int port, string user, string password) => OpenScripted();
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
        }
    }
}
