// Nullable-enabled on its own: the test project as a whole is not (see the note in Directory.Build.props).
#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// A list menu whose <c>print</c> has no <c>as-value</c>. RouterOS 6.49.13's <c>/routing bgp advertisements print</c>
    /// completes only file, interval, peer and where, so the read's <c>as-value</c> is taken for a peer name and the
    /// router answers <c>input does not match any value of peer</c> (Docs/findings-routeros-6.md, open problem 9). The
    /// plain print is a table; its header is the one measured on 6.49.13.
    /// </summary>
    [TestClass]
    public class CliPrintWithoutAsValueTests
    {
        private const string Menu = "/routing/bgp/advertisements/print";
        private const string Refusal = "input does not match any value of peer";
        private const string Header = "PEER     PREFIX               NEXTHOP          AS-PATH    ORIGIN     LOCAL-PREF";

        [TestMethod]
        public void TheRefusal_IsReadAsTheTable_AndRememberedForTheConnection()
        {
            using var router = new AdvertisementsRouter(Header + "\r\n"
                + "peer1    192.0.2.0/24         10.0.0.1                    igp        100\r\n");
            router.OpenScripted();

            var rows = router.CreateCommand(Menu).ExecuteList().ToList();
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual("peer1", rows[0].GetResponseField("peer"));
            Assert.AreEqual("192.0.2.0/24", rows[0].GetResponseField("prefix"));
            Assert.AreEqual("10.0.0.1", rows[0].GetResponseField("nexthop"));
            Assert.AreEqual("igp", rows[0].GetResponseField("origin"));
            Assert.AreEqual("100", rows[0].GetResponseField("local-pref"));

            router.Sent.Clear();
            router.CreateCommand(Menu).ExecuteList();
            CollectionAssert.AreEqual(new[] { ":put [/routing bgp advertisements print]" }, router.Sent,
                "the as-value form is not asked again on this connection");
        }

        [TestMethod]
        public void AnEmptyTable_IsNoRows()
        {
            using var router = new AdvertisementsRouter(Header + "\r\n");
            router.OpenScripted();

            Assert.AreEqual(0, router.CreateCommand(Menu).ExecuteList().Count());
        }

        [TestMethod]
        public void NoTable_IsTheRoutersRefusal_NotAnIncompleteRead()
        {
            using var router = new AdvertisementsRouter(Refusal);
            router.OpenScripted();

            var ex = Assert.ThrowsException<TikCommandTrapException>(() => router.CreateCommand(Menu).ExecuteList());
            StringAssert.Contains(ex.Message, Refusal);
        }

        [TestMethod]
        public void AFilteredRead_KeepsTheRefusal()
        {
            using var router = new AdvertisementsRouter(Header + "\r\n");
            router.OpenScripted();

            var ex = Assert.ThrowsException<TikCommandTrapException>(
                () => router.CreateCommandAndParameters(Menu, "peer", "peer1").ExecuteList());
            StringAssert.Contains(ex.Message, Refusal);
        }

        private sealed class AdvertisementsRouter : CliConnectionBase
        {
            private readonly string _plain;
            public readonly List<string> Sent = new List<string>();

            public AdvertisementsRouter(string plain)
            {
                CliFieldSeparator = null;   // scripts the as-value read; the DSV read is CliDsvReadTests
                _plain = plain;
            }

            protected override string TransportName => "Six";

            public void OpenScripted()
                => OpenWith(_ => Task.FromResult(0), SendAsync, (raw, ct) => Task.FromResult(string.Empty), () => { });

            private Task<string> SendAsync(string cliText, CancellationToken ct)
            {
                Sent.Add(cliText);
                if (cliText == ":put [/routing bgp advertisements print]")
                    return Task.FromResult(_plain);
                return Task.FromResult(cliText.Contains("advertisements") ? Refusal : "bad command name (line 1 column 2)");
            }

            public override void Open(string host, string user, string password) => OpenScripted();
            public override void Open(string host, int port, string user, string password) => OpenScripted();
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
        }
    }
}
