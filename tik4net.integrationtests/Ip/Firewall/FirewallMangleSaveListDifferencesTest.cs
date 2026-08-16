using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;

namespace tik4net.integrationtests
{
    /// <summary>
    /// Live-router coverage for <c>SaveListDifferences</c> on an <b>ordered</b> menu.
    /// <para>
    /// The unit tests for this run against an in-memory table that applies <c>/move</c> the way the router is
    /// believed to. This one checks the belief: that a <c>/move</c> with a <c>destination</c> really does put
    /// the row <em>before</em> that destination, that a created rule really is appended, and that the whole
    /// sequence the method emits lands a real chain in the requested order.
    /// </para>
    /// <para>
    /// Rules live in a private chain named after a per-run stamp, so nothing jumps into them from
    /// <c>prerouting</c>/<c>postrouting</c> and a leftover is always attributable to this test.
    /// </para>
    /// </summary>
    [TestClass]
    public class FirewallMangleSaveListDifferencesTest : TestBase
    {
        private string _chain;

        protected override void OnInitialize()
        {
            _chain = "T4N" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
        }

        /// <summary>
        /// The save creates rules itself, so <see cref="TestBase.SaveTracked{T}"/> cannot see them —
        /// teardown sweeps by chain name.
        /// </summary>
        protected override void OnCleanup()
        {
            try
            {
                foreach (var rule in LoadOwnRules())
                {
                    try { Connection.Delete(rule); }
                    catch { /* best effort — never mask the test outcome */ }
                }
            }
            catch { /* connection already gone */ }
        }

        private List<FirewallMangle> LoadOwnRules()
            => Connection.LoadAll<FirewallMangle>()
                .Where(m => string.Equals(m.Chain, _chain, StringComparison.Ordinal))
                .ToList();

        private FirewallMangle Rule(string mark) => new FirewallMangle
        {
            Chain = _chain,
            Action = FirewallMangle.ActionType.MarkPacket,
            NewPacketMark = mark,
            Passthrough = true,
        };

        private string[] Marks(IEnumerable<FirewallMangle> rules)
            => rules.Select(r => r.NewPacketMark).ToArray();

        [TestMethod]
        public void SaveListDifferencesWillApplyTheListOrder()
        {
            EnsureCommandAvailable("/ip/firewall/mangle");

            // Seed A, B, C, D in a chain of their own.
            foreach (var rule in new[] { Rule("A"), Rule("B"), Rule("C"), Rule("D") })
                Connection.Save(rule);

            var loaded = LoadOwnRules();
            Assert.AreEqual(4, loaded.Count, "seed");
            CollectionAssert.AreEqual(new[] { "A", "B", "C", "D" }, Marks(loaded).ToArray(), "the router keeps insertion order");

            var backup = loaded.CloneEntityList().ToList();

            // Reorder, delete one, add one in the middle, and rename one — all in a single save.
            loaded[0].NewPacketMark = "A2";
            var modified = new List<FirewallMangle> { loaded[3], Rule("N"), loaded[0], loaded[1] };  // D, N, A2, B (C deleted)

            Connection.SaveListDifferences(modified, backup);

            CollectionAssert.AreEqual(new[] { "D", "N", "A2", "B" }, Marks(LoadOwnRules()).ToArray(),
                "the chain must end up in the order of the modified list, with the created rule in the middle rather than appended");
        }

        [TestMethod]
        public void SaveListDifferencesWithNoChangesWillNotReorder()
        {
            EnsureCommandAvailable("/ip/firewall/mangle");

            foreach (var rule in new[] { Rule("A"), Rule("B"), Rule("C") })
                Connection.Save(rule);

            var loaded = LoadOwnRules();
            var backup = loaded.CloneEntityList().ToList();

            Connection.SaveListDifferences(loaded, backup);

            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, Marks(LoadOwnRules()).ToArray());
        }
    }
}
