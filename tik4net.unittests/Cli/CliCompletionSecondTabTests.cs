// Nullable-enabled on its own: the test project as a whole is not (see the note in Directory.Build.props).
#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// Completion of a stem that already is the candidates' common prefix. RouterOS 7 lists on the first Tab;
    /// RouterOS 6.49.13 only echoes the line and lists on a SECOND Tab (measured over raw Telnet,
    /// Docs/findings-routeros-6.md open problem 6). The double answers with the bytes measured on each.
    /// </summary>
    [TestClass]
    public class CliCompletionSecondTabTests
    {
        private const string Typed = "/interface bridge add frame-types=admit-";
        private const string Prompt = "[admin@MikroTik] > ";
        private const string Listing = "admit-all  admit-only-untagged-and-priority-tagged  admit-only-vlan-tagged";
        private static readonly string[] Expected =
            { "admit-all", "admit-only-untagged-and-priority-tagged", "admit-only-vlan-tagged" };

        // 6.49.13 repaints the whole line from the prompt after every typed character; the Tab adds nothing.
        private static string SixEcho()
        {
            var sb = new StringBuilder(Typed.Substring(0, 1));
            for (int i = 1; i < Typed.Length; i++)
                sb.Append('\r').Append(Prompt).Append(Typed, 0, i).Append("\x1b[K").Append(Typed[i]);
            return sb.Append('\r').Append(Prompt).Append(Typed).Append("\x1b[K").ToString();
        }

        [TestMethod]
        public void RouterOs6_ListsOnTheSecondTab()
        {
            using var router = new TabRouter(listsOnFirstTab: false);
            router.OpenScripted();

            CollectionAssert.AreEqual(Expected, router.CompleteCli(Typed).ToArray());
            Assert.AreEqual(2, router.Tabs, "the first Tab changed nothing, so a second one is sent");
        }

        [TestMethod]
        public void RouterOs7_ListsOnTheFirstTab_AndNoSecondIsSent()
        {
            using var router = new TabRouter(listsOnFirstTab: true);
            router.OpenScripted();

            CollectionAssert.AreEqual(Expected, router.CompleteCli(Typed).ToArray());
            Assert.AreEqual(1, router.Tabs);
        }

        [TestMethod]
        public void NothingToComplete_StaysEmpty_AfterTheSecondTab()
        {
            using var router = new TabRouter(listsOnFirstTab: false, nothing: true);
            router.OpenScripted();

            Assert.AreEqual(0, router.CompleteCli(Typed).Count);
            Assert.AreEqual("", router.CompleteCliRaw(Typed));
        }

        private sealed class TabRouter : CliConnectionBase
        {
            private readonly bool _listsOnFirstTab;
            private readonly bool _nothing;
            public int Tabs;

            public TabRouter(bool listsOnFirstTab, bool nothing = false)
            {
                _listsOnFirstTab = listsOnFirstTab;
                _nothing = nothing;
            }

            protected override string TransportName => "Tab";

            public void OpenScripted()
            {
                OpenWith(_ => Task.FromResult(0), (cli, ct) => Task.FromResult(string.Empty),
                         (raw, ct) => Task.FromResult("\r\n" + Prompt), () => { });
                RegisterCompletionDriver(Settle);
            }

            private Task<string> Settle(byte[] bytes, int quietMs, CancellationToken ct)
            {
                Tabs++;
                string listed = "\r\n" + Listing + "\r\n\r\x1b[9999B" + Prompt + Typed + "\x1b[K";
                if (bytes.Length > 1)   // the stem and its Tab
                    return Task.FromResult(_listsOnFirstTab && !_nothing ? Typed + listed : SixEcho());
                return Task.FromResult(_nothing ? string.Empty : listed);
            }

            public override void Open(string host, string user, string password) => OpenScripted();
            public override void Open(string host, int port, string user, string password) => OpenScripted();
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
        }
    }
}
