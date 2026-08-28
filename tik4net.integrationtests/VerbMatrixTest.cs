using System;
using System.Collections.Generic;
using System.Configuration;
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

        // ── get ───────────────────────────────────────────────────────────────
        //
        // Added late, and that is the point worth recording: this class exists so verb coverage is not
        // incidental, and `get` was still missing from it. Nothing else reaches the verb either — every
        // entity loads through `/print`, and ExecuteScalar reads through `print … where .id=` — so the one
        // way in was a caller naming `/path/get` by hand. The translation was consequently wrong on all
        // five CLI transports (both inputs dropped, the command sent as a singleton read of the menu),
        // absent on REST, and wrong-but-silent on native WinBox, with a green suite throughout. All three
        // are fixed and none of these is skipped anywhere: `get` is a narrowing of a read, so REST addresses
        // the row by URL, native WinBox filters the window it already read, and only the CLI family has a
        // `get` of its own to send.

        [TestMethod]
        public void Verb_Get_ByIdAndValueNameReturnsThatField()
        {
            string comment = _stamp + "-get";
            string id = AddRule("192.0.2.81", comment);

            var cmd = Connection.CreateCommand(Path + "/get");
            cmd.AddParameter(TikSpecialProperties.Id, id, TikCommandParameterFormat.NameValue);
            cmd.AddParameter("value-name", "comment", TikCommandParameterFormat.NameValue);

            Assert.AreEqual(comment, cmd.ExecuteScalar());
        }

        [TestMethod]
        public void Verb_Get_WithoutValueNameReturnsTheWholeRowAsOneValue()
        {
            // Not a shortcoming of any transport: RouterOS answers a get with no value-name by putting the
            // entire row in =ret= as one as-value string, with no !re record at all, and the CLI prints the
            // same. So this is a scalar everywhere — use print when you want a record.
            string comment = _stamp + "-getrow";
            string id = AddRule("192.0.2.82", comment);

            var cmd = Connection.CreateCommand(Path + "/get");
            cmd.AddParameter(TikSpecialProperties.Id, id, TikCommandParameterFormat.NameValue);

            string whole = cmd.ExecuteScalar();
            StringAssert.Contains(whole, "comment=" + comment);
            StringAssert.Contains(whole, ".id=" + id);
        }

        [TestMethod]
        public void Verb_Get_OnASingletonMenuReturnsTheField()
        {
            // No id to give. This shape looked safe and was broken too: with the inputs dropped the CLI
            // appended `as-value`, which `get` reads as the NAME of the field to return, so the router
            // refused it with "input does not match any value of value-name".

            var cmd = Connection.CreateCommand("/system/identity/get");
            cmd.AddParameter("value-name", "name", TikCommandParameterFormat.NameValue);

            Assert.AreEqual(Connection.LoadSingle<tik4net.Objects.System.SystemIdentity>().Name,
                cmd.ExecuteScalar());
        }

        /// <summary>
        /// A <c>get</c> for a row that does not exist yields nothing usable — but <b>which</b> nothing
        /// differs, and the binary API cannot do better.
        /// </summary>
        /// <remarks>
        /// Measured on RouterOS 7.24, over the API these two are byte-for-byte identical on the wire:
        /// <c>get .id=*7FFFFFFF value-name=comment</c> for a row that is not there, and the same command
        /// for a real row whose comment is empty. Both answer <c>!done</c> with an empty <c>=ret=</c>. So
        /// "not found" and "that field is empty" are indistinguishable there, and reporting the first would
        /// misreport the second — which is why the API returns the empty value rather than raising.
        /// <para>
        /// The CLI and REST transports CAN tell the two apart (the router refuses the id outright, and REST
        /// answers 404), so they report not-found. This asserts what all three agree on — that nothing
        /// usable comes back — rather than papering over a distinction the API genuinely does not have.
        /// </para>
        /// </remarks>
        [TestMethod]
        public void Verb_Get_ByAnUnknownIdYieldsNothing()
        {

            var cmd = Connection.CreateCommand(Path + "/get");
            cmd.AddParameter(TikSpecialProperties.Id, "*7FFFFFFF", TikCommandParameterFormat.NameValue);
            cmd.AddParameter("value-name", "comment", TikCommandParameterFormat.NameValue);

            string result = cmd.ExecuteScalarOrDefault();

            Assert.IsTrue(string.IsNullOrEmpty(result),
                "a get for a row that does not exist returned '" + result + "' — whichever way this "
                + "transport reports the miss, it must not return another row's value");
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

        // ── asking a silent write for a value (P2.34) ─────────────────────────

        /// <summary>
        /// Every verb above answers with nothing when it succeeds. Asking such a command for a scalar used to
        /// raise <see cref="TikNoSuchItemException"/> — "no such item" for a record that was right there, on a
        /// command that had already applied. It sent this very investigation looking for a missing rule while
        /// the router had faithfully executed each write. The error must name the real cause, and (the second
        /// assert) the write must still have taken effect: an exception that reads as "nothing happened" over
        /// a change that did happen is worse than no exception at all.
        /// </summary>
        [TestMethod]
        public void Scalar_OnASilentWrite_ReportsAnEmptyResponseAndTheWriteStillApplied()
        {
            EnsureCapability(TikConnectionCapability.RawCommand, "raw command pass-through");

            string comment = _stamp + "-silent";
            string id = AddRule("192.0.2.80", comment);
            string newComment = comment + "-applied";

            var cmd = RawSilentSetComment(id, comment, newComment);

            // TikCommandEmptyResponseException deliberately does NOT derive from TikNoSuchItemException — a
            // 'catch (TikNoSuchItemException)' must no longer swallow this as "the record is missing". The
            // compiler enforces that separation, so there is nothing to assert here at run time.
            Assert.ThrowsException<TikCommandEmptyResponseException>(() => cmd.ExecuteScalar(),
                "a write that succeeds silently must say so, not report a missing item");

            Assert.AreEqual(newComment, ReadField(id, "comment"),
                "the set threw but did apply — the exception must not be read as 'the command did nothing'");
        }

        /// <summary>The same call is not an error at all when the caller says the output is optional.</summary>
        [TestMethod]
        public void ScalarOrDefault_OnASilentWrite_ReturnsTheDefault()
        {
            EnsureCapability(TikConnectionCapability.RawCommand, "raw command pass-through");

            string comment = _stamp + "-silent-default";
            string id = AddRule("192.0.2.81", comment);

            var cmd = RawSilentSetComment(id, comment, comment + "-ok");

            Assert.AreEqual("<none>", cmd.ExecuteScalarOrDefault("<none>"));
            Assert.AreEqual(comment + "-ok", ReadField(id, "comment"));
        }

        /// <summary>
        /// A raw <c>set</c> that changes the rule's comment, in the transport's OWN raw dialect — that is the
        /// whole point of raw pass-through, so there is no neutral spelling: the binary API takes a
        /// <c>\n</c>-separated API sentence, the terminal transports take a CLI line.
        /// </summary>
        private ITikCommand RawSilentSetComment(string id, string currentComment, string newComment)
        {
            var t = ResolveConnectionType();
            if (t == TikConnectionType.Api || t == TikConnectionType.ApiSsl)
                return Connection.CreateRawCommand(
                    Path + "/set\n=" + TikSpecialProperties.Id + "=" + id + "\n=comment=" + newComment);

            return Connection.CreateRawCommand(
                Path + " set [find comment=\"" + currentComment + "\"] comment=\"" + newComment + "\"");
        }

        // ── cross-transport value parity ──────────────────────────────────────

        /// <summary>
        /// The same record, read over the transport under test and over the binary API, must yield the same
        /// FIELD VALUES — not merely the same rows.
        /// </summary>
        /// <remarks>
        /// P2.33: WinBox native rendered <c>src-address=192.0.2.74</c> as <c>192.0.2.74/6</c>, because the
        /// range-END sibling of a <c>range:1</c> network field was being bit-counted as a netmask. Nothing in
        /// the suite compared a value ACROSS transports, so eleven green runs said nothing about the eleven
        /// answers agreeing. The three forms below are the three the router renders (host / CIDR block /
        /// span), which is exactly where the rendering rule can go wrong one form at a time.
        /// </remarks>
        [TestMethod]
        public void CrossTransport_AddressValues_MatchTheBinaryApi()
        {
            var expected = new Dictionary<string, string>();
            foreach (var probe in new[]
                     {
                         new { Value = "192.0.2.74", Tag = "host" },
                         new { Value = "192.0.2.0/24", Tag = "block" },
                         new { Value = "192.0.2.10-192.0.2.20", Tag = "span" },
                     })
            {
                string comment = _stamp + "-parity-" + probe.Tag;
                AddRule(probe.Value, comment);
                expected[comment] = probe.Value;
            }

            string host = ConfigurationManager.AppSettings["host"];
            string user = ConfigurationManager.AppSettings["user"];
            string pass = ConfigurationManager.AppSettings["pass"] ?? "";

            foreach (var kv in expected)
            {
                var mine = ReadByComment(kv.Key);
                Assert.IsNotNull(mine, "the rule '" + kv.Key + "' is not readable over the transport under test");

                string reference;
                using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                {
                    var cmd = api.CreateCommand(Path + "/print");
                    cmd.AddParameter("comment", kv.Key, TikCommandParameterFormat.Filter);
                    var apiRow = cmd.ExecuteList().FirstOrDefault();
                    Assert.IsNotNull(apiRow, "the rule '" + kv.Key + "' is not on the router at all");
                    reference = apiRow.GetResponseFieldOrDefault("src-address", string.Empty);
                }

                // The API's own rendering is the canonical form, so it is both the reference AND the check
                // that the value survived the write: a transport that dropped src-address on 'add' shows up
                // here as an empty reference rather than as a passing comparison of two empty strings.
                Assert.AreEqual(kv.Value, reference,
                    "the value written over the transport under test did not reach the router intact");
                Assert.AreEqual(reference, mine.GetResponseFieldOrDefault("src-address", string.Empty),
                    "src-address reads differently over this transport than over the binary API");
            }
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
