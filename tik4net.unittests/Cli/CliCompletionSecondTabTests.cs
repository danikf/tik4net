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
            Assert.AreEqual(3, router.Tabs, "the first Tab changed nothing, so a second one is sent; the third asks for more");
        }

        [TestMethod]
        public void RouterOs7_ListsOnTheFirstTab_AndOneMoreTabAsksForWhatItLeftOut()
        {
            using var router = new TabRouter(listsOnFirstTab: true);
            router.OpenScripted();

            CollectionAssert.AreEqual(Expected, router.CompleteCli(Typed).ToArray(), "a repeat of the listing adds nothing");
            Assert.AreEqual(2, router.Tabs);
        }

        [TestMethod]
        public void WhatTheNextTabLists_IsAddedToTheListing()
        {
            // 7.24.4 '/tool ping ': 'interface' and 'vrf' come only on the second Tab.
            using var router = new TabRouter(listsOnFirstTab: true, more: "interface  vrf");
            router.OpenScripted();

            CollectionAssert.AreEqual(Expected.Concat(new[] { "interface", "vrf" }).ToArray(), router.CompleteCli(Typed).ToArray());
            StringAssert.Contains(router.CompleteCliRaw(Typed), "vrf");
        }

        [TestMethod]
        public void ASlowLink_WidensTheSettleWindow_AFastOneKeepsTheFloor()
        {
            using var slow = new TabRouter(listsOnFirstTab: true) { DelayMs = 400 };
            slow.OpenScripted();
            slow.CompleteCli(Typed);

            Assert.AreEqual(300, slow.QuietWindows[0], "nothing measured yet: the floor");
            Assert.IsTrue(slow.QuietWindows[1] >= 700, "twice a ~400 ms response: " + string.Join(",", slow.QuietWindows));

            using var fast = new TabRouter(listsOnFirstTab: true);
            fast.OpenScripted();
            fast.CompleteCli(Typed);
            CollectionAssert.AreEqual(new[] { 300, 300 }, fast.QuietWindows);
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
            private readonly string? _more;
            private bool _listed;
            public int Tabs;
            public int DelayMs;
            public readonly List<int> QuietWindows = new List<int>();

            public TabRouter(bool listsOnFirstTab, bool nothing = false, string? more = null)
            {
                CliFieldSeparator = null;   // scripts the as-value read; the DSV read is CliDsvReadTests
                _listsOnFirstTab = listsOnFirstTab;
                _nothing = nothing;
                _more = more;
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
                QuietWindows.Add(quietMs);
                if (DelayMs > 0)
                    Thread.Sleep(DelayMs + quietMs);   // the response, then the quiet window a real read waits out
                string listed = "\r\n" + Listing + "\r\n\r\x1b[9999B" + Prompt + Typed + "\x1b[K";
                if (bytes.Length > 1)   // the stem and its Tab
                {
                    _listed = _listsOnFirstTab && !_nothing;
                    return Task.FromResult(_listed ? Typed + listed : SixEcho());
                }
                if (_nothing)
                    return Task.FromResult(string.Empty);
                // After a listing, the next Tab lists what it left out, or (on a real router: the third) repeats it.
                if (_listed && _more != null)
                    return Task.FromResult("\r\n" + _more + "\r\n\r\x1b[9999B" + Prompt + Typed + "\x1b[K");
                _listed = true;
                return Task.FromResult(listed);
            }

            public override void Open(string host, string user, string password) => OpenScripted();
            public override void Open(string host, int port, string user, string password) => OpenScripted();
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
        }
    }
}
