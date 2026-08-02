using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;
using tik4net.Objects.Queue;
using tik4net.Testing;

namespace tik4net.unittests
{
    /// <summary>
    /// Covers <see cref="TikListMerge{TEntity}"/> the way a shaper-style consumer uses it: build the expected
    /// firewall/mangle and queue/tree state from a database, merge it against what the router currently holds, and
    /// apply the difference.
    /// <para>
    /// The tests do not stop at <c>Simulate</c>. Each merge is applied against <see cref="FakeRouterTable{TEntity}"/>
    /// and the resulting router state is read back, because the two halves fail independently: the counts can be
    /// right while the commands leave the mangle chain in an order that changes which rule matches first.
    /// </para>
    /// </summary>
    [TestClass]
    public class MergeShaperScenarioTest
    {
        private const string ChainUp = "prerouting";
        private const string ChainDown = "postrouting";
        private const string SectionStart = "MIKROTIKMANAGER-START";
        private const string SectionEnd = "MIKROTIKMANAGER-END";
        private const string QueueParent = "=Cas=ZAKAZNICI";

        // ── Entity builders ───────────────────────────────────────────────────

        private static FirewallMangle Marker(string comment) => new FirewallMangle
        {
            Chain = ChainUp,
            Action = FirewallMangle.ActionType.Accept,
            Comment = comment,
            Disabled = true,
            Passthrough = true,
        };

        private static FirewallMangle Jump(string subnet, bool upload) => new FirewallMangle
        {
            Chain = upload ? ChainUp : ChainDown,
            SrcAddress = upload ? subnet : null,
            DstAddress = upload ? null : subnet,
            Action = FirewallMangle.ActionType.Jump,
            JumpTarget = ChainName(subnet, upload),
            Passthrough = true,
        };

        private static FirewallMangle Mark(string subnet, string customerIp, string packetMark, bool upload) => new FirewallMangle
        {
            Chain = ChainName(subnet, upload),
            SrcAddress = upload ? customerIp : null,
            DstAddress = upload ? null : customerIp,
            Action = FirewallMangle.ActionType.MarkPacket,
            NewPacketMark = packetMark,
            Passthrough = false,
        };

        private static FirewallMangle Return(string subnet, bool upload) => new FirewallMangle
        {
            Chain = ChainName(subnet, upload),
            Action = FirewallMangle.ActionType.Return,
            Passthrough = true,
        };

        private static string ChainName(string subnet, bool upload)
            => string.Format("SCHAIN_{0}_{1}", subnet, upload ? "UP" : "DOWN");

        private static QueueTree Queue(string name, string packetMark, long maxLimit, string comment = null) => new QueueTree
        {
            Name = name,
            PacketMark = packetMark,
            Parent = QueueParent,
            LimitAt = 50 * 1000,
            MaxLimit = maxLimit,
            Comment = comment,
            Queue = "default",
            Priority = 7,
            Disabled = false,
            BurstLimit = 0,
            BurstThreshold = 0,
            BurstTime = "0s",
        };

        // ── Merge builders — the exact fluent setup the shaper uses ───────────

        private static TikListMerge<FirewallMangle> MangleMerge(
            ITikConnection connection, IEnumerable<FirewallMangle> expected, IEnumerable<FirewallMangle> actual)
            => connection.CreateMerge(expected, actual)
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

        private static TikListMerge<QueueTree> QueueMerge(
            ITikConnection connection, IEnumerable<QueueTree> expected, IEnumerable<QueueTree> actual)
            => connection.CreateMerge(expected, actual)
                .WithKey(q => q.Name)
                .JustForInsertField(q => q.Name)
                .Field(q => q.PacketMark)
                .Field(q => q.LimitAt)
                .Field(q => q.MaxLimit)
                .Field(q => q.Comment)
                .JustForInsertField(q => q.Parent)
                .JustForInsertField(q => q.Queue)
                .JustForInsertField(q => q.Priority)
                .JustForInsertField(q => q.Disabled)
                .JustForInsertField(q => q.BurstLimit)
                .JustForInsertField(q => q.BurstThreshold)
                .JustForInsertField(q => q.BurstTime);

        /// <summary>The managed slice of the mangle table for one customer subnet, in shaper order.</summary>
        private static List<FirewallMangle> Section(params IEnumerable<FirewallMangle>[] parts)
        {
            var result = new List<FirewallMangle> { Marker(SectionStart) };
            foreach (var part in parts)
                result.AddRange(part);
            result.Add(Marker(SectionEnd));
            return result;
        }

        private static string Describe(FirewallMangle m)
            => string.Format("{0}|{1}|src={2}|dst={3}|jump={4}|mark={5}|{6}",
                m.Chain, m.Action, m.SrcAddress ?? "", m.DstAddress ?? "",
                m.JumpTarget ?? "", m.NewPacketMark ?? "", m.Comment ?? "");

        // ══ Mangle merge ══════════════════════════════════════════════════════

        /// <summary>
        /// The shaper runs every few minutes and the database usually has not changed. A merge that reports work in
        /// that case would rewrite the whole mangle chain on a live router on every tick.
        /// </summary>
        [TestMethod]
        public void MangleMerge_RouterAlreadyMatchesTheDatabase_ReportsNoWorkAndSendsNoWrites()
        {
            var section = Section(
                new[] { Jump("10.43.101.0/24", true), Jump("10.43.101.0/24", false) },
                new[] { Mark("10.43.101.0/24", "10.43.101.5", "PM-5", true), Mark("10.43.101.0/24", "10.43.101.5", "PM-5", false) },
                new[] { Return("10.43.101.0/24", false), Return("10.43.101.0/24", true) });

            var table = new FakeRouterTable<FirewallMangle>().Seed(section.ToArray());
            var conn = table.AttachTo(new TikFakeConnection());
            var actual = table.Load(conn);

            int insertCnt, updateCnt, deleteCnt, moveCnt;
            MangleMerge(conn, section, actual).Simulate(out insertCnt, out updateCnt, out deleteCnt, out moveCnt);

            Assert.AreEqual(0, insertCnt, "nothing changed in the database, yet the merge wants to insert");
            Assert.AreEqual(0, updateCnt, "nothing changed in the database, yet the merge wants to update");
            Assert.AreEqual(0, deleteCnt, "nothing changed in the database, yet the merge wants to delete");
            Assert.AreEqual(0, moveCnt, "the rules are already in the expected order, yet the merge wants to move them");

            MangleMerge(conn, section, actual).Save();
            Assert.AreEqual(0, table.AppliedCommands.Count,
                "a no-op merge must not touch the router at all: " + string.Join(" / ", table.AppliedCommands));
        }

        /// <summary>
        /// A new customer subnet appears in the database. Every rule is appended by <c>/add</c>, so the ordering is
        /// carried entirely by the <c>/move</c> commands — the assertion that matters is the final chain order, not
        /// the insert count.
        /// </summary>
        [TestMethod]
        public void MangleMerge_NewSubnetInDatabase_InsertsTheRulesAndOrdersTheChain()
        {
            var before = Section(
                new[] { Jump("10.43.101.0/24", true), Jump("10.43.101.0/24", false) },
                new[] { Mark("10.43.101.0/24", "10.43.101.5", "PM-5", true), Mark("10.43.101.0/24", "10.43.101.5", "PM-5", false) },
                new[] { Return("10.43.101.0/24", false), Return("10.43.101.0/24", true) });

            var after = Section(
                new[] { Jump("10.43.101.0/24", true), Jump("10.43.101.0/24", false), Jump("10.43.102.0/24", true), Jump("10.43.102.0/24", false) },
                new[]
                {
                    Mark("10.43.101.0/24", "10.43.101.5", "PM-5", true), Mark("10.43.101.0/24", "10.43.101.5", "PM-5", false),
                    Mark("10.43.102.0/24", "10.43.102.9", "PM-9", true), Mark("10.43.102.0/24", "10.43.102.9", "PM-9", false),
                },
                new[]
                {
                    Return("10.43.102.0/24", false), Return("10.43.102.0/24", true),
                    Return("10.43.101.0/24", false), Return("10.43.101.0/24", true),
                });

            var table = new FakeRouterTable<FirewallMangle>().Seed(before.ToArray());
            var conn = table.AttachTo(new TikFakeConnection());

            int insertCnt, updateCnt, deleteCnt, moveCnt;
            MangleMerge(conn, after, table.Load(conn)).Simulate(out insertCnt, out updateCnt, out deleteCnt, out moveCnt);

            Assert.AreEqual(6, insertCnt, "two jumps, two packet marks and two returns are new");
            Assert.AreEqual(0, updateCnt);
            Assert.AreEqual(0, deleteCnt);

            MangleMerge(conn, after, table.Load(conn)).Save();

            CollectionAssert.AreEqual(
                after.Select(Describe).ToArray(),
                table.Load(conn).Select(Describe).ToArray(),
                "the merge applied but the mangle chain is not in the expected order — appended rules were not moved into place");
        }

        /// <summary>
        /// A customer changes tariff: same rule identity, different packet mark. The merge must update in place —
        /// a delete+insert would renumber the chain and briefly leave the customer unshaped.
        /// </summary>
        [TestMethod]
        public void MangleMerge_ChangedPacketMark_UpdatesInPlaceWithoutReinsertingTheRule()
        {
            var before = Section(
                new[] { Jump("10.43.101.0/24", true) },
                new[] { Mark("10.43.101.0/24", "10.43.101.5", "PM-OLD", true) },
                new[] { Return("10.43.101.0/24", true) });

            var after = Section(
                new[] { Jump("10.43.101.0/24", true) },
                new[] { Mark("10.43.101.0/24", "10.43.101.5", "PM-NEW", true) },
                new[] { Return("10.43.101.0/24", true) });

            var table = new FakeRouterTable<FirewallMangle>().Seed(before.ToArray());
            var conn = table.AttachTo(new TikFakeConnection());
            string[] idsBefore = table.Ids.ToArray();

            int insertCnt, updateCnt, deleteCnt, moveCnt;
            MangleMerge(conn, after, table.Load(conn)).Simulate(out insertCnt, out updateCnt, out deleteCnt, out moveCnt);

            Assert.AreEqual(1, updateCnt, "only the packet mark changed");
            Assert.AreEqual(0, insertCnt);
            Assert.AreEqual(0, deleteCnt);
            Assert.AreEqual(0, moveCnt, "an in-place update must not reorder the chain");

            MangleMerge(conn, after, table.Load(conn)).Save();

            Assert.AreEqual("PM-NEW", table.Load(conn).Single(m => m.Action == FirewallMangle.ActionType.MarkPacket).NewPacketMark);
            CollectionAssert.AreEqual(idsBefore, table.Ids.ToArray(),
                "the rule was recreated instead of updated — the .id changed");
        }

        /// <summary>
        /// The merge only writes the fields it was told to merge. The shaper relies on this: it never lists
        /// <c>disabled</c>, so a rule an operator disabled by hand must survive an update of its neighbours.
        /// </summary>
        [TestMethod]
        public void MangleMerge_Update_TouchesOnlyTheMergedFields()
        {
            var disabledByOperator = Mark("10.43.101.0/24", "10.43.101.5", "PM-OLD", true);
            disabledByOperator.Disabled = true;

            var before = Section(new[] { disabledByOperator });
            var after = Section(new[] { Mark("10.43.101.0/24", "10.43.101.5", "PM-NEW", true) });

            var table = new FakeRouterTable<FirewallMangle>().Seed(before.ToArray());
            var conn = table.AttachTo(new TikFakeConnection());

            MangleMerge(conn, after, table.Load(conn)).Save();

            var reloaded = table.Load(conn).Single(m => m.Action == FirewallMangle.ActionType.MarkPacket);
            Assert.AreEqual("PM-NEW", reloaded.NewPacketMark, "the merged field was not updated");
            Assert.IsTrue(reloaded.Disabled,
                "'disabled' is not a merged field, so the merge must not have re-enabled the rule");
        }

        /// <summary>A customer leaves: their rules go, everyone else's stay, and the chain order still holds.</summary>
        [TestMethod]
        public void MangleMerge_CustomerRemovedFromDatabase_DeletesOnlyTheirRules()
        {
            var before = Section(
                new[] { Jump("10.43.101.0/24", true) },
                new[]
                {
                    Mark("10.43.101.0/24", "10.43.101.5", "PM-5", true),
                    Mark("10.43.101.0/24", "10.43.101.6", "PM-6", true),
                },
                new[] { Return("10.43.101.0/24", true) });

            var after = Section(
                new[] { Jump("10.43.101.0/24", true) },
                new[] { Mark("10.43.101.0/24", "10.43.101.5", "PM-5", true) },
                new[] { Return("10.43.101.0/24", true) });

            var table = new FakeRouterTable<FirewallMangle>().Seed(before.ToArray());
            var conn = table.AttachTo(new TikFakeConnection());

            int insertCnt, updateCnt, deleteCnt, moveCnt;
            MangleMerge(conn, after, table.Load(conn)).Simulate(out insertCnt, out updateCnt, out deleteCnt, out moveCnt);

            Assert.AreEqual(1, deleteCnt, "exactly the departed customer's rule");
            Assert.AreEqual(0, insertCnt);
            Assert.AreEqual(0, updateCnt);

            MangleMerge(conn, after, table.Load(conn)).Save();

            CollectionAssert.AreEqual(
                after.Select(Describe).ToArray(),
                table.Load(conn).Select(Describe).ToArray());
        }

        /// <summary>
        /// The database order changed but nothing else did. This is the reordering case the shaper depends on:
        /// mangle is an ordered menu, so the merge must fix the order with <c>/move</c> and issue no DML at all.
        /// </summary>
        [TestMethod]
        public void MangleMerge_OnlyTheOrderChanged_MovesTheRulesAndWritesNothing()
        {
            var markA = Mark("10.43.101.0/24", "10.43.101.5", "PM-5", true);
            var markB = Mark("10.43.101.0/24", "10.43.101.6", "PM-6", true);
            var markC = Mark("10.43.101.0/24", "10.43.101.7", "PM-7", true);

            var before = Section(new[] { markA, markB, markC });
            var after = Section(new[] { markC, markA, markB });

            var table = new FakeRouterTable<FirewallMangle>().Seed(before.ToArray());
            var conn = table.AttachTo(new TikFakeConnection());

            int insertCnt, updateCnt, deleteCnt, moveCnt;
            MangleMerge(conn, after, table.Load(conn)).Simulate(out insertCnt, out updateCnt, out deleteCnt, out moveCnt);

            Assert.AreEqual(0, insertCnt, "reordering must not recreate rules");
            Assert.AreEqual(0, updateCnt);
            Assert.AreEqual(0, deleteCnt);
            Assert.IsTrue(moveCnt > 0, "the order differs, so the merge must report at least one move");

            MangleMerge(conn, after, table.Load(conn)).Save();

            CollectionAssert.AreEqual(
                after.Select(Describe).ToArray(),
                table.Load(conn).Select(Describe).ToArray(),
                "the merge reported moves but the chain order on the router is still wrong");
            Assert.IsFalse(table.AppliedCommands.Any(c => c.StartsWith("/ip/firewall/mangle/add")
                                                       || c.StartsWith("/ip/firewall/mangle/remove")),
                "a pure reordering must not add or remove anything: " + string.Join(" / ", table.AppliedCommands));
        }

        /// <summary>
        /// A whole section reordered: three customers rotated, each contributing an upload and a download rule in a
        /// different chain, with the jumps and returns around them.
        /// <para>
        /// This is the case that exposed the merge's move bookkeeping. The decision "does this rule need moving?"
        /// used to be made against the rules' <em>original</em> positions, which stop being true the moment the
        /// first move is applied — so every rule that had been adjacent to an already-moved neighbour had its move
        /// skipped. The merge applied 3 of the 7 moves it needed and left the chain in an order matching neither
        /// the router's previous state nor the requested one, while reporting success. Counts alone cannot catch
        /// this; only reading the chain back can.
        /// </para>
        /// </summary>
        [TestMethod]
        public void MangleMerge_WholeSectionRotated_AppliesEveryMoveTheNewOrderNeeds()
        {
            Func<string[], List<FirewallMangle>> section = ips => Section(
                new[] { Jump("10.43.101.0/24", true), Jump("10.43.101.0/24", false) },
                ips.SelectMany(ip => new[]
                {
                    Mark("10.43.101.0/24", ip, "PM-" + ip, true),
                    Mark("10.43.101.0/24", ip, "PM-" + ip, false),
                }),
                new[] { Return("10.43.101.0/24", false), Return("10.43.101.0/24", true) });

            var before = section(new[] { "10.43.101.11", "10.43.101.12", "10.43.101.13" });
            var after = section(new[] { "10.43.101.13", "10.43.101.11", "10.43.101.12" });

            var table = new FakeRouterTable<FirewallMangle>().Seed(before.ToArray());
            var conn = table.AttachTo(new TikFakeConnection());

            MangleMerge(conn, after, table.Load(conn)).Save();

            CollectionAssert.AreEqual(
                after.Select(Describe).ToArray(),
                table.Load(conn).Select(Describe).ToArray(),
                "the merge reported success but the chain is in neither the old nor the new order — "
                    + "moves were skipped against stale positions");
            Assert.IsFalse(table.AppliedCommands.Any(c => c.StartsWith("/ip/firewall/mangle/add")
                                                       || c.StartsWith("/ip/firewall/mangle/remove")
                                                       || c.StartsWith("/ip/firewall/mangle/set")),
                "a pure reordering must be carried by moves alone: " + string.Join(" / ", table.AppliedCommands));
        }

        /// <summary>
        /// <c>Simulate</c> is what guards the shaper's DML limit — it runs before the operator has approved anything,
        /// so it must be able to answer "how much would change" without changing it.
        /// </summary>
        [TestMethod]
        public void MangleMerge_Simulate_DoesNotTouchTheRouter()
        {
            var before = Section(new[] { Mark("10.43.101.0/24", "10.43.101.5", "PM-5", true) });
            var after = Section(
                new[] { Jump("10.43.102.0/24", true) },
                new[] { Mark("10.43.102.0/24", "10.43.102.9", "PM-9", true) });

            var table = new FakeRouterTable<FirewallMangle>().Seed(before.ToArray());
            var conn = table.AttachTo(new TikFakeConnection());
            var actual = table.Load(conn);
            string[] stateBefore = table.Load(conn).Select(Describe).ToArray();

            int insertCnt, updateCnt, deleteCnt, moveCnt;
            MangleMerge(conn, after, actual).Simulate(out insertCnt, out updateCnt, out deleteCnt, out moveCnt);

            Assert.IsTrue(insertCnt + updateCnt + deleteCnt > 0, "precondition: this merge has work to do");
            Assert.AreEqual(0, table.AppliedCommands.Count,
                "Simulate wrote to the router: " + string.Join(" / ", table.AppliedCommands));
            CollectionAssert.AreEqual(stateBefore, table.Load(conn).Select(Describe).ToArray());
        }

        /// <summary>
        /// The merge counts reported by <c>Simulate</c> are the same ones the DML limit is compared against, so they
        /// have to match what <c>Save</c> actually does — not merely be non-zero.
        /// </summary>
        [TestMethod]
        public void MangleMerge_SimulateCounts_MatchWhatSaveApplies()
        {
            var before = Section(
                new[] { Jump("10.43.101.0/24", true) },
                new[]
                {
                    Mark("10.43.101.0/24", "10.43.101.5", "PM-OLD", true),
                    Mark("10.43.101.0/24", "10.43.101.6", "PM-6", true),
                });

            var after = Section(
                new[] { Jump("10.43.101.0/24", true) },
                new[]
                {
                    Mark("10.43.101.0/24", "10.43.101.5", "PM-NEW", true),   // update
                    Mark("10.43.101.0/24", "10.43.101.7", "PM-7", true),     // insert (…6 deleted)
                });

            var table = new FakeRouterTable<FirewallMangle>().Seed(before.ToArray());
            var conn = table.AttachTo(new TikFakeConnection());

            int insertCnt, updateCnt, deleteCnt, moveCnt;
            MangleMerge(conn, after, table.Load(conn)).Simulate(out insertCnt, out updateCnt, out deleteCnt, out moveCnt);
            Assert.AreEqual(1, insertCnt);
            Assert.AreEqual(1, updateCnt);
            Assert.AreEqual(1, deleteCnt);

            MangleMerge(conn, after, table.Load(conn)).Save();

            Assert.AreEqual(insertCnt, table.AppliedCommands.Count(c => c.StartsWith("/ip/firewall/mangle/add")),
                "Save performed a different number of inserts than Simulate promised");
            Assert.AreEqual(deleteCnt, table.AppliedCommands.Count(c => c.StartsWith("/ip/firewall/mangle/remove")),
                "Save performed a different number of deletes than Simulate promised");
            Assert.AreEqual(updateCnt, table.AppliedCommands.Count(c => c.StartsWith("/ip/firewall/mangle/set")),
                "Save performed a different number of updates than Simulate promised");
        }

        /// <summary>
        /// The shaper's operation filter is the last line of defence before a bad database run wipes a chain.
        /// A vetoed operation must be neither counted nor sent.
        /// </summary>
        [TestMethod]
        public void MangleMerge_OperationFilterVetoesDeletes_LeavesTheRulesOnTheRouter()
        {
            var before = Section(
                new[] { Mark("10.43.101.0/24", "10.43.101.5", "PM-5", true) },
                new[] { Mark("10.43.101.0/24", "10.43.101.6", "PM-6", true) });
            var after = Section();

            var table = new FakeRouterTable<FirewallMangle>().Seed(before.ToArray());
            var conn = table.AttachTo(new TikFakeConnection());

            int insertCnt, updateCnt, deleteCnt, moveCnt;
            MangleMerge(conn, after, table.Load(conn))
                .WithOperationFilter((operation, oldE, newE) => operation != TikListMerge<FirewallMangle>.MergeOperation.Delete)
                .Simulate(out insertCnt, out updateCnt, out deleteCnt, out moveCnt);

            Assert.AreEqual(0, deleteCnt, "vetoed deletes must not be counted towards the DML limit either");

            MangleMerge(conn, after, table.Load(conn))
                .WithOperationFilter((operation, oldE, newE) => operation != TikListMerge<FirewallMangle>.MergeOperation.Delete)
                .Save();

            Assert.AreEqual(4, table.Load(conn).Count, "the filter vetoed the deletes but they happened anyway");
        }

        /// <summary>Both log callbacks fire on Save (the shaper's only audit trail) and stay silent on Simulate.</summary>
        [TestMethod]
        public void MangleMerge_LogCallbacks_ReportEveryAppliedOperationAndNothingOnSimulate()
        {
            var before = Section(new[] { Mark("10.43.101.0/24", "10.43.101.5", "PM-OLD", true) });
            var after = Section(
                new[] { Mark("10.43.101.0/24", "10.43.101.5", "PM-NEW", true) },
                new[] { Mark("10.43.101.0/24", "10.43.101.6", "PM-6", true) });

            var table = new FakeRouterTable<FirewallMangle>().Seed(before.ToArray());
            var conn = table.AttachTo(new TikFakeConnection());

            var simulatedDml = new List<string>();
            int insertCnt, updateCnt, deleteCnt, moveCnt;
            MangleMerge(conn, after, table.Load(conn))
                .WithDmlLogCallback((op, oldE, newE) => simulatedDml.Add(op.ToString()))
                .WithMoveLogCallback((e, oldIdx, newIdx) => simulatedDml.Add("Move"))
                .Simulate(out insertCnt, out updateCnt, out deleteCnt, out moveCnt);
            Assert.AreEqual(0, simulatedDml.Count, "Simulate logged operations it never performed");

            var appliedDml = new List<string>();
            int moves = 0;
            MangleMerge(conn, after, table.Load(conn))
                .WithDmlLogCallback((op, oldE, newE) => appliedDml.Add(op.ToString()))
                .WithMoveLogCallback((e, oldIdx, newIdx) => moves++)
                .Save();

            Assert.AreEqual(insertCnt + updateCnt + deleteCnt, appliedDml.Count,
                "the DML log must report exactly the operations that were applied");
            Assert.AreEqual(moveCnt, moves, "the move log must report exactly the moves that were applied");
        }

        // ══ Queue tree merge ══════════════════════════════════════════════════

        /// <summary>
        /// <c>queue/tree</c> is not an ordered menu. The merge must therefore never issue <c>/move</c> — on several
        /// transports that command does not exist for this path at all.
        /// </summary>
        [TestMethod]
        public void QueueMerge_UnorderedMenu_NeverIssuesMove()
        {
            var before = new[] { Queue("cust-1", "PM-1", 10000000) };
            var after = new[] { Queue("cust-2", "PM-2", 20000000), Queue("cust-1", "PM-1", 10000000) };

            var table = new FakeRouterTable<QueueTree>().Seed(before);
            var conn = table.AttachTo(new TikFakeConnection());

            int insertCnt, updateCnt, deleteCnt, moveCnt;
            QueueMerge(conn, after, table.Load(conn)).Simulate(out insertCnt, out updateCnt, out deleteCnt, out moveCnt);
            Assert.AreEqual(0, moveCnt, "queue/tree is not ordered — the merge must not plan any move");

            QueueMerge(conn, after, table.Load(conn)).Save();

            Assert.IsFalse(table.AppliedCommands.Any(c => c.StartsWith("/queue/tree/move")),
                "an unordered menu received a /move: " + string.Join(" / ", table.AppliedCommands));
        }

        /// <summary>
        /// The shaper's whole tariff/debt logic ends in one number: <c>max-limit</c>. A changed limit must reach the
        /// router, and an unchanged queue must not be rewritten.
        /// </summary>
        [TestMethod]
        public void QueueMerge_ChangedSpeedLimit_UpdatesOnlyTheAffectedQueue()
        {
            var before = new[]
            {
                Queue("cust-1", "PM-1", 10000000),
                Queue("cust-2", "PM-2", 20000000),
            };
            var after = new[]
            {
                Queue("cust-1", "PM-1", 10000000),
                Queue("cust-2", "PM-2", 1000000, "dluh 1234,- po splatnosti"),   // throttled for debt
            };

            var table = new FakeRouterTable<QueueTree>().Seed(before);
            var conn = table.AttachTo(new TikFakeConnection());

            int insertCnt, updateCnt, deleteCnt, moveCnt;
            QueueMerge(conn, after, table.Load(conn)).Simulate(out insertCnt, out updateCnt, out deleteCnt, out moveCnt);
            Assert.AreEqual(1, updateCnt, "exactly one queue changed");
            Assert.AreEqual(0, insertCnt);
            Assert.AreEqual(0, deleteCnt);

            QueueMerge(conn, after, table.Load(conn)).Save();

            var reloaded = table.Load(conn);
            Assert.AreEqual(1000000, reloaded.Single(q => q.Name == "cust-2").MaxLimit);
            Assert.AreEqual("dluh 1234,- po splatnosti", reloaded.Single(q => q.Name == "cust-2").Comment);
            Assert.AreEqual(10000000, reloaded.Single(q => q.Name == "cust-1").MaxLimit,
                "the untouched queue must keep its limit");
        }

        /// <summary>
        /// <c>JustForInsertField</c> carries the defaults a new queue needs (parent, priority, burst…). They must be
        /// sent on <c>/add</c> — otherwise the queue lands outside the shaper's parent — and must never be sent on
        /// <c>/set</c>, which would silently revert an operator's manual tuning on every shaper run.
        /// </summary>
        [TestMethod]
        public void QueueMerge_JustForInsertFields_AreSentOnInsertAndWithheldOnUpdate()
        {
            var table = new FakeRouterTable<QueueTree>().Seed(Queue("cust-1", "PM-1", 10000000));
            var conn = table.AttachTo(new TikFakeConnection());

            // an operator raised the priority by hand on the existing queue
            var tuned = table.Load(conn).Single();
            tuned.Priority = 1;
            table.AppliedCommands.Clear();

            var after = new[]
            {
                Queue("cust-1", "PM-1", 15000000),    // update: max-limit changed
                Queue("cust-2", "PM-2", 20000000),    // insert
            };

            QueueMerge(conn, after, table.Load(conn)).Save();

            string addCommand = table.AppliedCommands.Single(c => c.StartsWith("/queue/tree/add"));
            StringAssert.Contains(addCommand, "=parent=" + QueueParent, "a new queue must be created under the shaper's parent");
            StringAssert.Contains(addCommand, "=priority=7", "insert-only defaults must be sent on /add");
            StringAssert.Contains(addCommand, "=burst-time=0s");

            string setCommand = table.AppliedCommands.Single(c => c.StartsWith("/queue/tree/set"));
            Assert.IsFalse(setCommand.Contains("=priority="),
                "an insert-only field was sent on /set — the shaper would overwrite manual tuning on every run: " + setCommand);
            Assert.IsFalse(setCommand.Contains("=parent="),
                "an insert-only field was sent on /set: " + setCommand);
            StringAssert.Contains(setCommand, "=max-limit=15000000", "the merged field must still be updated");
        }

        /// <summary>A queue whose customer disappeared from the database is removed.</summary>
        [TestMethod]
        public void QueueMerge_CustomerRemovedFromDatabase_DeletesTheQueue()
        {
            var table = new FakeRouterTable<QueueTree>().Seed(
                Queue("cust-1", "PM-1", 10000000),
                Queue("cust-2", "PM-2", 20000000));
            var conn = table.AttachTo(new TikFakeConnection());

            var after = new[] { Queue("cust-1", "PM-1", 10000000) };

            QueueMerge(conn, after, table.Load(conn)).Save();

            var reloaded = table.Load(conn);
            Assert.AreEqual(1, reloaded.Count);
            Assert.AreEqual("cust-1", reloaded.Single().Name);
        }

        // ══ Whole shaper run ══════════════════════════════════════════════════

        /// <summary>
        /// One full shaper tick — mangle and queue merged against the same router, then a second tick with an
        /// unchanged database. The second tick is the real assertion: a merge that is not idempotent rewrites the
        /// firewall on a production router every few minutes.
        /// </summary>
        [TestMethod]
        public void ShaperRun_AppliedTwice_IsIdempotent()
        {
            var mangleTable = new FakeRouterTable<FirewallMangle>().Seed(Section().ToArray());   // markers only
            var queueTable = new FakeRouterTable<QueueTree>();
            var conn = new TikFakeConnection();
            mangleTable.AttachTo(conn);
            queueTable.AttachTo(conn);

            var expectedMangles = Section(
                new[] { Jump("10.43.101.0/24", true), Jump("10.43.101.0/24", false) },
                new[]
                {
                    Mark("10.43.101.0/24", "10.43.101.5", "PM-5", true), Mark("10.43.101.0/24", "10.43.101.5", "PM-5", false),
                    Mark("10.43.101.0/24", "10.43.101.6", "PM-6", true), Mark("10.43.101.0/24", "10.43.101.6", "PM-6", false),
                },
                new[] { Return("10.43.101.0/24", false), Return("10.43.101.0/24", true) });

            var expectedQueues = new[]
            {
                Queue("cust-5", "PM-5", 10000000),
                Queue("cust-6", "PM-6", 20000000),
            };

            // ── first run: everything is new ──
            MangleMerge(conn, expectedMangles, mangleTable.Load(conn)).Save();
            QueueMerge(conn, expectedQueues, queueTable.Load(conn)).Save();

            CollectionAssert.AreEqual(
                expectedMangles.Select(Describe).ToArray(),
                mangleTable.Load(conn).Select(Describe).ToArray(),
                "the first shaper run did not produce the expected mangle chain");
            CollectionAssert.AreEqual(
                expectedQueues.Select(q => q.Name).ToArray(),
                queueTable.Load(conn).Select(q => q.Name).OrderBy(n => n).ToArray());

            // ── second run: the database has not changed ──
            mangleTable.AppliedCommands.Clear();
            queueTable.AppliedCommands.Clear();

            int mi, mu, md, mm;
            MangleMerge(conn, expectedMangles, mangleTable.Load(conn)).Simulate(out mi, out mu, out md, out mm);
            int qi, qu, qd, qm;
            QueueMerge(conn, expectedQueues, queueTable.Load(conn)).Simulate(out qi, out qu, out qd, out qm);

            Assert.AreEqual(0, mi + mu + md + mm,
                string.Format("a re-run against an unchanged database wants to change the mangle chain ({0}/{1}/{2}/{3})", mi, mu, md, mm));
            Assert.AreEqual(0, qi + qu + qd + qm,
                string.Format("a re-run against an unchanged database wants to change the queues ({0}/{1}/{2})", qi, qu, qd));

            MangleMerge(conn, expectedMangles, mangleTable.Load(conn)).Save();
            QueueMerge(conn, expectedQueues, queueTable.Load(conn)).Save();

            Assert.AreEqual(0, mangleTable.AppliedCommands.Count + queueTable.AppliedCommands.Count,
                "the second shaper run wrote to the router: "
                    + string.Join(" / ", mangleTable.AppliedCommands.Concat(queueTable.AppliedCommands)));
        }
    }
}
