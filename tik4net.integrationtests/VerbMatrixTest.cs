using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;

namespace tik4net.integrationtests
{
    /// <summary>
    /// One test per RouterOS <b>verb</b>, driven at the low <see cref="ITikCommand"/> level and asserting the
    /// <b>effect on the router</b> — not merely that the call did not throw.
    /// <para>
    /// The rest of the suite is organised by entity, which makes verb coverage incidental: <c>print</c>,
    /// <c>add</c>, <c>set</c> and <c>remove</c> are exercised constantly because every CRUD test needs them,
    /// while <c>unset</c>, <c>move</c>, <c>comment</c>, <c>enable</c>/<c>disable</c> and the filter/proplist
    /// forms were covered only where an entity test happened to want one. That blind spot is what let
    /// <c>unset</c> ship emitting outright invalid syntax on every CLI transport (the binary API's
    /// <c>value-name=</c> spelling passed through untranslated) — loud when the router rejects it, silent
    /// when it does not.
    /// </para>
    /// <para>
    /// Because the class runs under every <c>*.runsettings</c>, a verb that one transport gets wrong shows up
    /// as one red test naming that verb, instead of as absent coverage. A verb a transport genuinely cannot
    /// carry must fail here first and be skipped only once the ROUTER has refused a correctly-formed request
    /// (CLAUDE.md: assume feature parity until proven otherwise).
    /// </para>
    /// <para>
    /// The probe entity is <c>/ip/firewall/filter</c>: it is ordered (so <c>move</c> is meaningful), carries
    /// <c>comment</c> and <c>disabled</c>, is reachable on every transport including native WinBox M2, and a
    /// disabled rule matching a TEST-NET-1 address (RFC 5737 <c>192.0.2.0/24</c>) cannot affect the router.
    /// </para>
    /// </summary>
    [TestClass]
    public class VerbMatrixTest : TestBase
    {
        private const string Path = "/ip/firewall/filter";

        /// <summary>Rules created by the running test, newest first — drained in <see cref="OnCleanup"/>.</summary>
        private readonly Stack<string> _createdIds = new Stack<string>();

        /// <summary>Per-run stamp so a leftover is attributable and two runs never collide.</summary>
        private string _stamp;

        protected override void OnInitialize()
        {
            _stamp = "t4n-verb-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        protected override void OnCleanup()
        {
            while (_createdIds.Count > 0)
            {
                string id = _createdIds.Pop();
                try
                {
                    var cmd = Connection.CreateCommand(Path + "/remove", TikCommandParameterFormat.NameValue);
                    cmd.AddParameter(TikSpecialProperties.Id, id, TikCommandParameterFormat.NameValue);
                    cmd.ExecuteNonQuery();
                }
                catch { /* already removed by the test, or the connection is gone — never mask the outcome */ }
            }
        }

        // ── add ───────────────────────────────────────────────────────────────

        [TestMethod]
        public void Verb_Add_CreatesTheRuleAndReturnsItsId()
        {
            string comment = _stamp + "-add";
            string id = AddRule("192.0.2.70", comment);

            Assert.IsFalse(string.IsNullOrEmpty(id), "add did not return the new record's .id");

            var row = ReadByComment(comment);
            Assert.IsNotNull(row, "add returned an .id but the rule is not on the router");
            Assert.AreEqual(id, row.GetId(), "add returned an .id that does not identify the created rule");
        }

        // ── print ─────────────────────────────────────────────────────────────

        [TestMethod]
        public void Verb_Print_ReturnsTheRule()
        {
            string comment = _stamp + "-print";
            string id = AddRule("192.0.2.71", comment);

            var all = Connection.CreateCommand(Path + "/print").ExecuteList().ToList();
            Assert.IsTrue(all.Any(r => r.GetId() == id),
                "an unfiltered print did not return the rule that was just created");
        }

        [TestMethod]
        public void Verb_Print_WithFilter_ReturnsOnlyTheMatchingRule()
        {
            string comment = _stamp + "-filter";
            string id = AddRule("192.0.2.72", comment);
            AddRule("192.0.2.73", _stamp + "-filter-other");

            var cmd = Connection.CreateCommand(Path + "/print");
            cmd.AddParameter("comment", comment, TikCommandParameterFormat.Filter);
            var rows = cmd.ExecuteList().ToList();

            Assert.AreEqual(1, rows.Count,
                "a ?comment= filter returned " + rows.Count + " rows instead of the one matching rule "
                + "(a dropped filter reads as success but returns the whole table)");
            Assert.AreEqual(id, rows[0].GetId());
        }

        [TestMethod]
        public void Verb_Print_WithProplist_ReturnsTheRequestedFields()
        {
            string comment = _stamp + "-proplist";
            string id = AddRule("192.0.2.74", comment);

            var cmd = Connection.CreateCommand(Path + "/print");
            cmd.AddParameter("comment", comment, TikCommandParameterFormat.Filter);
            var rows = cmd.ExecuteList(".id", "comment", "chain").ToList();

            Assert.AreEqual(1, rows.Count, "proplist print did not return the rule");
            // The requested fields must be present. Absence of the OTHERS is deliberately not asserted:
            // the CLI's as-value form always emits every field (proplist trimming is not expressible there),
            // so requiring a trimmed row would fail for a reason that is not a defect.
            Assert.AreEqual(id, rows[0].GetId());
            Assert.AreEqual(comment, rows[0].GetResponseField("comment"));
            Assert.AreEqual("forward", rows[0].GetResponseField("chain"));
        }

        // ── set ───────────────────────────────────────────────────────────────

        [TestMethod]
        public void Verb_Set_ChangesTheField()
        {
            string comment = _stamp + "-set";
            string id = AddRule("192.0.2.75", comment);

            var cmd = Connection.CreateCommand(Path + "/set", TikCommandParameterFormat.NameValue);
            cmd.AddParameter(TikSpecialProperties.Id, id, TikCommandParameterFormat.NameValue);
            cmd.AddParameter("connection-mark", "t4n-mark");
            cmd.ExecuteNonQuery();

            Assert.AreEqual("t4n-mark", ReadField(id, "connection-mark"),
                "set reported success but the field on the router is unchanged");
        }

        // ── unset ─────────────────────────────────────────────────────────────

        [TestMethod]
        public void Verb_Unset_ClearsTheField()
        {
            string comment = _stamp + "-unset";
            string id = AddRule("192.0.2.76", comment);

            var set = Connection.CreateCommand(Path + "/set", TikCommandParameterFormat.NameValue);
            set.AddParameter(TikSpecialProperties.Id, id, TikCommandParameterFormat.NameValue);
            set.AddParameter("connection-mark", "t4n-mark");
            set.ExecuteNonQuery();
            Assert.AreEqual("t4n-mark", ReadField(id, "connection-mark"), "precondition: the field is set");

            var cmd = Connection.CreateCommand(Path + "/unset", TikCommandParameterFormat.NameValue);
            cmd.AddParameter(TikSpecialProperties.Id, id, TikCommandParameterFormat.NameValue);
            cmd.AddParameter(TikSpecialProperties.UnsetValueName, "connection-mark");
            cmd.ExecuteNonQuery();

            Assert.AreEqual(string.Empty, ReadField(id, "connection-mark"),
                "unset reported success but the field is still set on the router");
        }

        /// <summary>
        /// The production path for <c>unset</c>: the O/R mapper emits it on <c>Save</c> for a property marked
        /// <c>UnsetOnDefault</c> whose value has gone back to the default. This is how a consumer actually
        /// reaches the verb — <see cref="Verb_Unset_ClearsTheField"/> covers the same wire call directly.
        /// </summary>
        [TestMethod]
        public void Verb_Unset_ViaMapperSaveOfAnUnsetOnDefaultProperty()
        {
            var rule = new FirewallFilter
            {
                Chain = "forward",
                Action = FirewallFilter.ActionType.Accept,
                SrcAddress = "192.0.2.79",
                ConnectionMark = "t4n-mark",   // [TikProperty("connection-mark", UnsetOnDefault = true)]
                Disabled = true,
                Comment = _stamp + "-unset-mapper",
            };
            SaveTracked(rule);

            Assert.AreEqual("t4n-mark", Connection.LoadById<FirewallFilter>(rule.Id).ConnectionMark,
                "precondition: connection-mark was stored");

            rule.ConnectionMark = null;   // back to the default → Save must emit /unset value-name=connection-mark
            Connection.Save(rule);

            Assert.IsTrue(string.IsNullOrEmpty(Connection.LoadById<FirewallFilter>(rule.Id).ConnectionMark),
                "Save() of a defaulted UnsetOnDefault property left the value on the router");
        }

        // ── comment ───────────────────────────────────────────────────────────

        [TestMethod]
        public void Verb_Comment_SetsTheComment()
        {
            string comment = _stamp + "-comment";
            string id = AddRule("192.0.2.80", comment);

            string updated = comment + "-updated";
            var cmd = Connection.CreateCommand(Path + "/comment", TikCommandParameterFormat.NameValue);
            cmd.AddParameter(TikSpecialProperties.Id, id, TikCommandParameterFormat.NameValue);
            cmd.AddParameter("comment", updated);
            cmd.ExecuteNonQuery();

            Assert.AreEqual(updated, ReadField(id, "comment"),
                "the 'comment' verb reported success but the comment on the router is unchanged");
        }

        // ── enable / disable ──────────────────────────────────────────────────

        [TestMethod]
        public void Verb_DisableAndEnable_ToggleTheDisabledFlag()
        {
            string comment = _stamp + "-toggle";
            string id = AddRule("192.0.2.81", comment, disabled: false);
            Assert.AreEqual("false", ReadField(id, "disabled"), "precondition: the rule starts enabled");

            SimpleVerb("disable", id);
            Assert.AreEqual("true", ReadField(id, "disabled"),
                "disable reported success but the rule is still enabled");

            SimpleVerb("enable", id);
            Assert.AreEqual("false", ReadField(id, "disabled"),
                "enable reported success but the rule is still disabled");
        }

        // ── move ──────────────────────────────────────────────────────────────

        [TestMethod]
        public void Verb_Move_ReordersTheRules()
        {
            string first = AddRule("192.0.2.82", _stamp + "-move-1");
            string second = AddRule("192.0.2.83", _stamp + "-move-2");

            Assert.IsTrue(IndexOf(first) < IndexOf(second), "precondition: the rules are in creation order");

            var cmd = Connection.CreateCommand(Path + "/move", TikCommandParameterFormat.NameValue);
            cmd.AddParameter(TikSpecialProperties.Id, second, TikCommandParameterFormat.NameValue);
            cmd.AddParameter("destination", first);
            cmd.ExecuteNonQuery();

            Assert.IsTrue(IndexOf(second) < IndexOf(first),
                "move reported success but the rule order on the router is unchanged");
        }

        // ── remove ────────────────────────────────────────────────────────────

        [TestMethod]
        public void Verb_Remove_DeletesTheRule()
        {
            string comment = _stamp + "-remove";
            string id = AddRule("192.0.2.84", comment);

            var cmd = Connection.CreateCommand(Path + "/remove", TikCommandParameterFormat.NameValue);
            cmd.AddParameter(TikSpecialProperties.Id, id, TikCommandParameterFormat.NameValue);
            cmd.ExecuteNonQuery();

            Assert.IsNull(ReadByComment(comment), "remove reported success but the rule is still on the router");
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private string AddRule(string srcAddress, string comment, bool disabled = true)
        {
            var cmd = Connection.CreateCommand(Path + "/add", TikCommandParameterFormat.NameValue);
            cmd.AddParameter("chain", "forward");
            cmd.AddParameter("action", "accept");
            cmd.AddParameter("src-address", srcAddress);
            cmd.AddParameter("disabled", disabled ? "yes" : "no");
            cmd.AddParameter("comment", comment);

            // Registered BEFORE the call: 'add' can answer without the new .id on some transports while the
            // row IS created (mikrotik-tests skill, gotcha A), and those leftovers are what make a LATER run
            // fail against a record it never created. Resolved by comment in that case.
            string id = null;
            _createdIds.Push(null);   // placeholder replaced below; ensures cleanup runs even if the call throws
            try
            {
                id = cmd.ExecuteScalar();
            }
            finally
            {
                _createdIds.Pop();
                _createdIds.Push(id ?? ResolveIdByComment(comment));
            }
            return id;
        }

        private string ResolveIdByComment(string comment)
        {
            var row = ReadByComment(comment);
            return row?.GetId();
        }

        private ITikReSentence ReadByComment(string comment)
        {
            var cmd = Connection.CreateCommand(Path + "/print");
            cmd.AddParameter("comment", comment, TikCommandParameterFormat.Filter);
            return cmd.ExecuteList().FirstOrDefault();
        }

        private string ReadField(string id, string fieldName)
        {
            var row = Connection.CreateCommand(Path + "/print").ExecuteList()
                .FirstOrDefault(r => r.GetId() == id);
            Assert.IsNotNull(row, "the rule " + id + " is no longer on the router");
            return row.GetResponseFieldOrDefault(fieldName, string.Empty);
        }

        private int IndexOf(string id)
        {
            var rows = Connection.CreateCommand(Path + "/print").ExecuteList().ToList();
            int idx = rows.FindIndex(r => r.GetId() == id);
            Assert.IsTrue(idx >= 0, "the rule " + id + " is no longer on the router");
            return idx;
        }

        private void SimpleVerb(string verb, string id)
        {
            var cmd = Connection.CreateCommand(Path + "/" + verb, TikCommandParameterFormat.NameValue);
            cmd.AddParameter(TikSpecialProperties.Id, id, TikCommandParameterFormat.NameValue);
            cmd.ExecuteNonQuery();
        }
    }
}
