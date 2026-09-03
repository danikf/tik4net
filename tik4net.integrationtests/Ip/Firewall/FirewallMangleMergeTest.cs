using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;

namespace tik4net.integrationtests
{
    /// <summary>
    /// Live-router coverage for reordering <c>/ip/firewall/mangle</c> and for
    /// <see cref="TikListMerge{TEntity}"/> against it.
    /// <para>
    /// Mangle is an <b>ordered</b> menu: the first matching rule wins, so a rule in the wrong position is a rule
    /// that does the wrong thing while every field on it reads correct. That makes <c>/move</c> the one operation
    /// whose result cannot be checked by looking at the entity — only by reading the chain back — and the reason
    /// the shaper-style merge (build expected state from a database, merge, apply) depends on it: every inserted
    /// rule is appended by the router and owes its position entirely to a follow-up move.
    /// </para>
    /// <para>
    /// Everything is created inside private chains named after a per-run stamp, so the rules are unreachable from
    /// any real traffic path (nothing jumps into them from <c>prerouting</c>/<c>postrouting</c>) and a leftover is
    /// always attributable to this test.
    /// </para>
    /// </summary>
    [TestClass]
    public class FirewallMangleMergeTest : TestBase
    {
        private string _prefix;
        private string _rootChain;
        private string _upChain;
        private string _downChain;

        private const string Subnet = "192.0.2.0/24";      // TEST-NET-1, never routed
        private const string CustomerA = "192.0.2.11";
        private const string CustomerB = "192.0.2.12";
        private const string CustomerC = "192.0.2.13";

        protected override void OnInitialize()
        {
            _prefix = "T4N" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
            _rootChain = _prefix + "_ROOT";
            _upChain = _prefix + "_UP";
            _downChain = _prefix + "_DOWN";
        }

        /// <summary>
        /// Removes every rule this test's chains contain. The merge creates rules itself, so
        /// <see cref="TestBase.SaveTracked{T}"/> cannot see them — teardown has to sweep by chain name.
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

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// The rules in this run's private chains, in router order.
        /// <para>
        /// Filtered <b>on the router</b> by <c>comment</c>, not client-side out of a full table. Every rule this
        /// test creates is stamped with <see cref="_prefix"/>, which makes the filter a single equality the router
        /// can answer — and the test independent of how big <c>/ip/firewall/mangle</c> happens to be. Loading the
        /// whole menu and picking three chains out of it worked only while the lab router was nearly empty: against
        /// a table carrying ~1700 unrelated rules the repeated full loads pushed one of these tests past the 30 s
        /// receive timeout, failing on the router's size rather than on anything the test is about.
        /// </para>
        /// <para>
        /// The chain check stays as a safety net. It is now cheap, and it still catches the one thing the comment
        /// filter cannot: a rule of ours that somehow landed outside our chains.
        /// </para>
        /// </summary>
        private List<FirewallMangle> LoadOwnRules()
            => Connection.LoadList<FirewallMangle>(
                    Connection.CreateParameter("comment", _prefix, TikCommandParameterFormat.Filter))
                .Where(m => !string.IsNullOrEmpty(m.Chain) && m.Chain.StartsWith(_prefix, StringComparison.Ordinal))
                .ToList();

        private FirewallMangle Jump(bool upload) => new FirewallMangle
        {
            Chain = _rootChain,
            SrcAddress = upload ? Subnet : null,
            DstAddress = upload ? null : Subnet,
            Action = FirewallMangle.ActionType.Jump,
            JumpTarget = upload ? _upChain : _downChain,
            Passthrough = true,
            Comment = _prefix,
        };

        private FirewallMangle Mark(string customerIp, string packetMark, bool upload) => new FirewallMangle
        {
            Chain = upload ? _upChain : _downChain,
            SrcAddress = upload ? customerIp : null,
            DstAddress = upload ? null : customerIp,
            Action = FirewallMangle.ActionType.MarkPacket,
            NewPacketMark = packetMark,
            Passthrough = false,
            Comment = _prefix,
        };

        private FirewallMangle Return(bool upload) => new FirewallMangle
        {
            Chain = upload ? _upChain : _downChain,
            Action = FirewallMangle.ActionType.Return,
            Passthrough = true,
            Comment = _prefix,
        };

        /// <summary>The merge setup a shaper uses: identity from the rule's role, not from its position.</summary>
        private TikListMerge<FirewallMangle> Merge(IEnumerable<FirewallMangle> expected, IEnumerable<FirewallMangle> actual)
            => Connection.CreateMerge(expected, actual)
                .WithKey(m =>
                {
                    switch (m.Action)
                    {
                        case FirewallMangle.ActionType.Jump:
                        case FirewallMangle.ActionType.MarkPacket:
                            return string.Format("{0}:Src:{1};Dst{2}", m.Action, m.SrcAddress, m.DstAddress);
                        case FirewallMangle.ActionType.Return:
                            return string.Format("{0}:{1}", m.Action, m.Chain);
                        default:
                            return string.Format("{0}:{1}", m.Action, m.Comment);
                    }
                })
                .JustForInsertField(m => m.Action)
                .Field(m => m.SrcAddress)
                .Field(m => m.DstAddress)
                .Field(m => m.Chain)
                .Field(m => m.Comment)
                .Field(m => m.JumpTarget)
                .Field(m => m.NewPacketMark)
                .Field(m => m.Passthrough);

        /// <summary>Identity of a rule for order comparison — everything the merge is responsible for.</summary>
        private static string Describe(FirewallMangle m)
            => string.Format("{0}|{1}|src={2}|dst={3}|jump={4}|mark={5}",
                m.Chain, m.Action, Address(m.SrcAddress), Address(m.DstAddress),
                m.JumpTarget ?? "", m.NewPacketMark ?? "");

        /// <summary>
        /// A single host address, in whichever of the two equivalent spellings the transport reports.
        /// <para>
        /// The native WinBox transports render a host address as <c>192.0.2.11/32</c> where the API, REST and CLI
        /// transports report <c>192.0.2.11</c>. The two mean the same rule, so the ordering assertions here — whose
        /// subject is which rule sits where — must not be decided by that difference. The discrepancy itself is a
        /// real defect and belongs to a test of its own; <see cref="Merge_ReRunAgainstAnUnchangedExpectation_ChangesNothing"/>
        /// is the one that reports it, because there a value that does not survive the round trip unchanged is
        /// exactly the failure being looked for.
        /// </para>
        /// </summary>
        private static string Address(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.EndsWith("/32", StringComparison.Ordinal)
                ? value.Substring(0, value.Length - 3)
                : value;
        }

        private static string Dump(IEnumerable<FirewallMangle> rules)
            => Environment.NewLine + string.Join(Environment.NewLine, rules.Select(Describe));

        // ── move ──────────────────────────────────────────────────────────────

        /// <summary>
        /// <see cref="TikConnectionExtensions.Move{TEntity}"/> on a mangle rule: the router must actually
        /// reorder the chain, not merely answer !done.
        /// </summary>
        [TestMethod]
        public void Move_BeforeAnotherRule_ReordersTheMangleChain()
        {
            SaveTracked(Mark(CustomerA, _prefix + "-A", upload: true));
            SaveTracked(Mark(CustomerB, _prefix + "-B", upload: true));
            SaveTracked(Mark(CustomerC, _prefix + "-C", upload: true));

            var rules = LoadOwnRules();
            Assert.AreEqual(3, rules.Count, "precondition: the three rules were created" + Dump(rules));
            CollectionAssert.AreEqual(
                new[] { CustomerA, CustomerB, CustomerC },
                rules.Select(r => Address(r.SrcAddress)).ToArray(),
                "precondition: new rules are appended in creation order" + Dump(rules));

            Connection.Move(rules[2], rules[0]);   // C before A

            var reordered = LoadOwnRules();
            CollectionAssert.AreEqual(
                new[] { CustomerC, CustomerA, CustomerB },
                reordered.Select(r => Address(r.SrcAddress)).ToArray(),
                "move reported success but the chain order on the router is unchanged" + Dump(reordered));
        }

        /// <summary>
        /// <see cref="TikConnectionExtensions.MoveToEnd{TEntity}"/> — the same command with no destination.
        /// It is the only way the merge can express "last", and a transport that silently drops the missing
        /// destination would move the rule nowhere while reporting success.
        /// </summary>
        [TestMethod]
        public void MoveToEnd_MakesTheRuleLastInTheChain()
        {
            SaveTracked(Mark(CustomerA, _prefix + "-A", upload: true));
            SaveTracked(Mark(CustomerB, _prefix + "-B", upload: true));
            SaveTracked(Mark(CustomerC, _prefix + "-C", upload: true));

            var rules = LoadOwnRules();
            Connection.MoveToEnd(rules[0]);   // A to the end

            var reordered = LoadOwnRules();
            CollectionAssert.AreEqual(
                new[] { CustomerB, CustomerC, CustomerA },
                reordered.Select(r => Address(r.SrcAddress)).ToArray(),
                "MoveToEnd reported success but the rule is not last" + Dump(reordered));
        }

        /// <summary>
        /// Moving a rule to where it already is must be harmless. The merge issues a move for every entity whose
        /// recorded index does not line up, so this runs far more often than the reordering case does.
        /// </summary>
        [TestMethod]
        public void Move_ToItsCurrentPosition_LeavesTheChainUnchanged()
        {
            SaveTracked(Mark(CustomerA, _prefix + "-A", upload: true));
            SaveTracked(Mark(CustomerB, _prefix + "-B", upload: true));

            var rules = LoadOwnRules();
            Connection.Move(rules[0], rules[1]);   // A is already immediately before B

            var reordered = LoadOwnRules();
            CollectionAssert.AreEqual(
                new[] { CustomerA, CustomerB },
                reordered.Select(r => Address(r.SrcAddress)).ToArray(),
                "a no-op move disturbed the chain" + Dump(reordered));
        }

        // ── merge ─────────────────────────────────────────────────────────────

        /// <summary>
        /// The full shaper shape against a real router: an empty section grows a complete set of jump / mark /
        /// return rules, and the chain must end up in exactly the expected order. Everything the router adds is
        /// appended, so this passes only if the merge's move sequence is right end to end.
        /// </summary>
        [TestMethod]
        public void Merge_BuildsTheWholeSectionOnTheRouterInTheExpectedOrder()
        {
            var expected = BuildSection(CustomerA, CustomerB);

            int insertCnt, updateCnt, deleteCnt, moveCnt;
            Merge(expected, LoadOwnRules()).Simulate(out insertCnt, out updateCnt, out deleteCnt, out moveCnt);
            Assert.AreEqual(expected.Count, insertCnt, "every rule in an empty section is new");
            Assert.AreEqual(0, updateCnt);
            Assert.AreEqual(0, deleteCnt);

            Merge(expected, LoadOwnRules()).Save();

            var actual = LoadOwnRules();
            CollectionAssert.AreEqual(
                expected.Select(Describe).ToArray(),
                actual.Select(Describe).ToArray(),
                "the merge applied but the chain on the router is not in the expected order" + Dump(actual));
        }

        /// <summary>
        /// Re-running the same merge against an unchanged expectation must do nothing at all. This is the
        /// assertion that catches a value the router stores differently from how we sent it (an address it
        /// normalizes, a flag it defaults): the merge would then see a difference every run and rewrite a live
        /// firewall on every tick, and nothing else in the suite would notice.
        /// </summary>
        [TestMethod]
        public void Merge_ReRunAgainstAnUnchangedExpectation_ChangesNothing()
        {
            var expected = BuildSection(CustomerA, CustomerB);
            Merge(expected, LoadOwnRules()).Save();

            var afterFirstRun = LoadOwnRules();
            var idsAfterFirstRun = afterFirstRun.Select(r => r.Id).ToArray();

            int insertCnt, updateCnt, deleteCnt, moveCnt;
            Merge(expected, afterFirstRun).Simulate(out insertCnt, out updateCnt, out deleteCnt, out moveCnt);

            Assert.AreEqual(0, insertCnt, "a re-run wants to insert rules that are already there" + Dump(afterFirstRun));
            Assert.AreEqual(0, updateCnt, "a re-run wants to update rules it just wrote — a field does not survive the "
                                        + "round trip unchanged" + Dump(afterFirstRun));
            Assert.AreEqual(0, deleteCnt, "a re-run wants to delete rules it just wrote" + Dump(afterFirstRun));
            Assert.AreEqual(0, moveCnt, "a re-run wants to reorder a chain it just ordered" + Dump(afterFirstRun));

            Merge(expected, LoadOwnRules()).Save();
            CollectionAssert.AreEqual(idsAfterFirstRun, LoadOwnRules().Select(r => r.Id).ToArray(),
                "the second run recreated rules instead of leaving them alone");
        }

        /// <summary>
        /// A customer arrives, another leaves, a third changes tariff — one merge, and the chain must come out
        /// both correct and correctly ordered, with the surviving rules keeping their identity.
        /// </summary>
        [TestMethod]
        public void Merge_CustomerAddedRemovedAndRetariffed_AppliesAllThreeInOnePass()
        {
            Merge(BuildSection(CustomerA, CustomerB), LoadOwnRules()).Save();

            string keptId = LoadOwnRules()
                .Single(m => m.Action == FirewallMangle.ActionType.MarkPacket && Address(m.SrcAddress) == CustomerA)
                .Id;

            // A keeps its rule but changes packet mark; B leaves; C arrives — and C sorts before A.
            var expected = BuildSection(CustomerC, CustomerA);
            foreach (var mark in expected.Where(m => m.Action == FirewallMangle.ActionType.MarkPacket
                                                  && (m.SrcAddress == CustomerA || m.DstAddress == CustomerA)))
                mark.NewPacketMark = _prefix + "-A-NEW";

            int insertCnt, updateCnt, deleteCnt, moveCnt;
            Merge(expected, LoadOwnRules()).Simulate(out insertCnt, out updateCnt, out deleteCnt, out moveCnt);
            Assert.AreEqual(2, insertCnt, "C's upload and download mark rules are new");
            Assert.AreEqual(2, updateCnt, "A's upload and download mark rules changed packet mark");
            Assert.AreEqual(2, deleteCnt, "B's upload and download mark rules are gone");

            Merge(expected, LoadOwnRules()).Save();

            var actual = LoadOwnRules();
            CollectionAssert.AreEqual(
                expected.Select(Describe).ToArray(),
                actual.Select(Describe).ToArray(),
                "the chain does not match the expected state after a mixed insert/update/delete merge" + Dump(actual));
            Assert.AreEqual(keptId,
                actual.Single(m => m.Action == FirewallMangle.ActionType.MarkPacket && Address(m.SrcAddress) == CustomerA).Id,
                "the retariffed rule was recreated instead of updated in place");
            Assert.IsFalse(actual.Any(m => Address(m.SrcAddress) == CustomerB || Address(m.DstAddress) == CustomerB),
                "the departed customer's rules are still on the router" + Dump(actual));
        }

        /// <summary>
        /// Only the order changed. The merge must fix it with moves alone — a reordering that goes through
        /// delete+insert would renumber the whole section and, on a production shaper, drop traffic while it does.
        /// </summary>
        [TestMethod]
        public void Merge_OnlyTheOrderChanged_ReordersWithoutRecreatingTheRules()
        {
            Merge(BuildSection(CustomerA, CustomerB, CustomerC), LoadOwnRules()).Save();
            var idsBefore = LoadOwnRules().OrderBy(r => r.Id).Select(r => r.Id).ToArray();

            var expected = BuildSection(CustomerC, CustomerA, CustomerB);

            int insertCnt, updateCnt, deleteCnt, moveCnt;
            Merge(expected, LoadOwnRules()).Simulate(out insertCnt, out updateCnt, out deleteCnt, out moveCnt);
            Assert.AreEqual(0, insertCnt, "reordering must not create rules");
            Assert.AreEqual(0, updateCnt, "reordering must not rewrite rules");
            Assert.AreEqual(0, deleteCnt, "reordering must not delete rules");
            Assert.IsTrue(moveCnt > 0, "the expected order differs, so the merge must plan at least one move");

            Merge(expected, LoadOwnRules()).Save();

            var actual = LoadOwnRules();
            CollectionAssert.AreEqual(
                expected.Select(Describe).ToArray(),
                actual.Select(Describe).ToArray(),
                "the merge reported moves but the chain order on the router is still wrong" + Dump(actual));
            CollectionAssert.AreEqual(idsBefore, actual.OrderBy(r => r.Id).Select(r => r.Id).ToArray(),
                "the rules were recreated rather than moved");
        }

        /// <summary>
        /// One shaper section: the jumps, then one packet-mark pair per customer, then the returns —
        /// the layout <c>CentralShapePump</c> builds.
        /// </summary>
        private List<FirewallMangle> BuildSection(params string[] customerIps)
        {
            var result = new List<FirewallMangle> { Jump(upload: true), Jump(upload: false) };
            foreach (string ip in customerIps)
            {
                result.Add(Mark(ip, _prefix + "-" + ip.Split('.').Last(), upload: true));
                result.Add(Mark(ip, _prefix + "-" + ip.Split('.').Last(), upload: false));
            }
            result.Add(Return(upload: false));
            result.Add(Return(upload: true));
            return result;
        }
    }
}
