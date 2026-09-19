using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;   // FirewallConnection, FirewallFilter
using tik4net.Objects.Tool;
using tik4net.Testing;   // WithId - sets the private .id setter on an entity the test never loaded

namespace tik4net.integrationtests
{
    /// <summary>
    /// Checks the per-verb declarations on <c>[TikEntity]</c> against what the router actually offers, and
    /// exercises the operation the mapper used to refuse — the defect behind
    /// <see href="https://github.com/danikf/tik4net/issues/84">issue #84</see>.
    /// <para>
    /// Without this class the enum is decoration: 25 hand-written claims about RouterOS menus become ~100,
    /// and nothing measures them. With it, a RouterOS release that adds or drops a verb is a red test naming
    /// the menu instead of a support question, and a wrong declaration cannot ship.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <b>Which probe answers depends on the transport, and that is a router property, not a capability
    /// gate.</b> Measured on 7.24 while writing this:
    /// <list type="bullet">
    /// <item>On the <b>CLI family</b> the router will list a menu's verbs on Tab — one round-trip per menu,
    /// exact, and no command is executed. The execution probe below is useless there: the CLI translates
    /// <c>/ppp/active/remove =.id=*7FFFFFFF</c> into a <c>[find]</c> form that matches nothing and answers
    /// <b>OK</b>, and answers a genuinely absent verb (<c>/log/remove</c>) with <i>bad parameter .id</i>
    /// rather than <i>no such command</i> — so both outcomes are indistinguishable from success.</item>
    /// <item>On <b>API and REST</b> there is no completion, and the execution probe is exact: an absent verb
    /// is <see cref="TikNoSuchCommandException"/> and a present one is anything else.</item>
    /// <item>On <b>native WinBox</b> neither exists. M2 has no verb list — <c>Docs/jg-catalog-format.md</c>
    /// records that generic <c>map</c> windows carry <c>cmds={}</c> and inherit list/get/set/add/remove as
    /// WinBox builtins, so the catalog reports all five for every menu — and an unsupported operation comes
    /// back as an opaque <c>0xFE00xx</c>, not as "no such command".</item>
    /// </list>
    /// <see cref="DeleteWorksOnAStatusMenuThatOnlyOffersRemove"/> needs none of that and runs everywhere: it
    /// deletes a row and looks for it again.
    /// </remarks>
    [TestClass]
    public class EntityOperationMatrixTest : TestBase
    {
        /// <summary>
        /// An <c>.id</c> no row can have. <c>*7FFFFFFF</c> is well-formed — the router parses it and then
        /// looks for it — so what comes back is "no such item" and not a syntax complaint.
        /// </summary>
        private const string NonexistentId = "*7FFFFFFF";

        /// <summary>The four verbs the mapper decides on, and the token RouterOS spells each with.</summary>
        private static readonly (TikEntityOperations Operation, string Verb)[] Verbs =
        {
            (TikEntityOperations.Add,    "add"),
            (TikEntityOperations.Set,    "set"),
            (TikEntityOperations.Remove, "remove"),
            (TikEntityOperations.Move,   "move"),
        };

        // ── The regression itself ─────────────────────────────────────────────────────────────────

        [TestMethod]
        public void DeleteWorksOnAStatusMenuThatOnlyOffersRemove()
        {
            // Issue #84, end to end and on every transport. Delete<PppActive> used to throw
            // InvalidOperationException("Can not save R/O entity.") before a command was built, for a
            // /remove the router performs happily.
            //
            // /ip/firewall/connection is the menu from that set that can be given a row on an idle lab
            // router — no PPP session, HotSpot login or associated wireless client needed. The row is made
            // by asking the router to ping a TEST-NET-1 address (RFC 5737, 192.0.2.0/24): nothing answers,
            // so exactly one conntrack entry appears, it is identifiable by its destination, and it cannot
            // come back once removed.
            //
            // That last part is why the subject is not just "any tracked connection". Conntrack recycles
            // .id aggressively - the first version of this test deleted a live UDP row, re-read the same
            // .id, found a DIFFERENT flow that had been handed the freed id, and reported the delete as a
            // no-op. Addressing the row by destination instead of by .id removes the ambiguity, and a
            // destination nothing routes to removes the churn.
            EnsureCommandAvailable("/ip/firewall/connection");

            // Two addresses, because these entries also expire on their own (~10 s for ICMP). Only the
            // first is deleted; the second is the control that says the first went away because it was
            // removed and not because the test outwaited it. Without it a long enough poll passes on any
            // build, including one where Delete does nothing.
            var random = new Random();
            string target = "192.0.2." + random.Next(2, 126).ToString(CultureInfo.InvariantCulture);
            string control = "192.0.2." + random.Next(128, 250).ToString(CultureInfo.InvariantCulture);

            ToolPing.Execute(Connection, target, 2);
            ToolPing.Execute(Connection, control, 2);

            var victim = TrackedConnectionTo(target);
            if (victim == null || TrackedConnectionTo(control) == null)
                Assert.Inconclusive($"Pinging {target}/{control} left no tracked connection - either "
                                  + "connection tracking is off on this router, or the entry expired "
                                  + "before it could be read.");

            // The call that used to throw. Not wrapped: an InvalidOperationException here IS the regression.
            Connection.Delete(victim);

            // Polled, not asserted outright: RouterOS 7.24 answers this /remove with !done and reaps the
            // entry a moment later - a re-read 58 ms after the !done still returned the row, unchanged
            // down to its timeout and icmp-id. That is the router's own latency, not a lost command, and
            // the control below is what keeps the wait from being able to manufacture a pass.
            var waited = Stopwatch.StartNew();
            while (TrackedConnectionTo(target) != null && waited.ElapsedMilliseconds < 4000)
                Thread.Sleep(100);

            bool controlSurvived = TrackedConnectionTo(control) != null;

            Assert.IsNull(TrackedConnectionTo(target),
                $"Delete on /ip/firewall/connection was accepted but the row for {target} was still there "
                + $"{waited.ElapsedMilliseconds} ms later - the command did nothing, which is worse than a "
                + "refusal.");

            if (!controlSurvived)
                Assert.Inconclusive($"The row for {target} is gone, but so is the untouched control row for "
                                  + $"{control} - both entries simply expired during the {waited.ElapsedMilliseconds} ms "
                                  + "wait, so this run proves nothing about Delete.");
        }

        [TestMethod]
        public void SaveIsRefusedOnTheSameMenuAndNamesTheVerb()
        {
            // The other half of the same declaration, and the reason Delete being allowed is not simply
            // "the entity is writable now": /ip/firewall/connection has neither /add nor /set, so both
            // halves of Save must still be refused - client-side, before anything reaches the router,
            // which is why this needs no traffic and no row.
            //
            // Both branches, because Save decides which verb it wants from whether the entity carries an
            // .id, and the guard now sits inside that decision rather than in front of it.
            var onCreate = Assert.ThrowsException<InvalidOperationException>(
                () => Connection.Save(new FirewallConnection()));
            StringAssert.Contains(onCreate.Message, "'add'");
            StringAssert.Contains(onCreate.Message, "/ip/firewall/connection");

            var onUpdate = Assert.ThrowsException<InvalidOperationException>(
                () => Connection.Save(new FirewallConnection().WithId("*1A")));
            StringAssert.Contains(onUpdate.Message, "'set'");
            StringAssert.Contains(onUpdate.Message, "/ip/firewall/connection");
        }

        [TestMethod]
        public void DeletingARowThatIsNotThereRaisesNoSuchItemOnEveryTransport()
        {
            // The other half of "Delete works on a status menu": on menus whose rows come and go on their
            // own - which is precisely the set issue #84 unlocked - the row you loaded a moment ago may be
            // gone by the time you delete it. That has to be an exception, not a shrug, and it has to be
            // the SAME exception everywhere, or a caller cannot write one catch block.
            //
            // This is the test that found the CLI family reporting success for it. The mapper's Delete
            // sent `remove [find where .id=*N]`, and a [find] that matches nothing is not an error - the
            // router printed nothing and the CLI had nothing to raise on. Fixed by addressing the row with
            // `numbers=*N`, which answers "no such item (4)" exactly as the API does.
            var ghost = new FirewallFilter().WithId(NonexistentId);

            var ex = Assert.ThrowsException<TikNoSuchItemException>(() => Connection.Delete(ghost));

            // Not TikCommandTrapException: the whole point is that a caller can tell "the row is gone"
            // from "the command failed", and ThrowsException is exact about the type (a subclass fails it).
            Assert.IsNotNull(ex);
        }

        [TestMethod]
        public void SavingARowThatIsNotThereRaisesNoSuchItemOnEveryTransport()
        {
            // Same for the update half. An .id the caller supplies takes Save down the /set path, so this
            // is `set numbers=*7FFFFFFF …` - which was the same silent no-op on the CLI family, and worse
            // than the Delete case: the caller believes the field was written.
            var ghost = new FirewallFilter { Comment = "t4n-ghost" }.WithId(NonexistentId);

            Assert.ThrowsException<TikNoSuchItemException>(
                () => Connection.Save(ghost, new[] { "comment" }));
        }

        [TestMethod]
        public void MovingARowThatIsNotThereRaisesNoSuchItemOnEveryTransport()
        {
            // /ip/firewall/filter is ordered, so Move is legal on it and only the row is missing.
            var ghost = new FirewallFilter().WithId(NonexistentId);

            Assert.ThrowsException<TikNoSuchItemException>(() => Connection.MoveToEnd(ghost));
        }

        [TestMethod]
        public void UnsettingAFieldOnARowThatIsNotThereRaisesNoSuchItemOnEveryTransport()
        {
            // The fourth row-addressing verb, named explicitly because verb coverage is never incidental:
            // `unset` reaches the router only from inside Save (a nullable field the caller cleared), so
            // nothing else in the suite would exercise it against a missing row - and it shares the record
            // selector that was the defect. Driven at the command level for the same reason VerbMatrixTest
            // is: constructing the Save path needs a loaded snapshot, and this is about the translation.
            //
            // `connection-mark` and not `comment`: /ip/firewall/filter has no unsettable comment, and the
            // router complains about the value-name BEFORE it looks for the row - on the binary API too -
            // which would have made this test pass for the wrong reason.
            var cmd = Connection.CreateCommand("/ip/firewall/filter/unset", TikCommandParameterFormat.NameValue);
            cmd.AddParameter(TikSpecialProperties.Id, NonexistentId, TikCommandParameterFormat.NameValue);
            cmd.AddParameter("value-name", "connection-mark", TikCommandParameterFormat.NameValue);

            Assert.ThrowsException<TikNoSuchItemException>(() => cmd.ExecuteNonQuery());
        }

        // ── The declarations, measured ────────────────────────────────────────────────────────────

        [TestMethod]
        public void TabCompletionAgreesWithEveryNarrowedDeclaration()
        {
            // Every entity that claims to lack a verb, checked against the menu's own Tab listing. Only the
            // four tokens we care about are tested for membership: no RouterOS submenu is called add/set/
            // remove/move, so membership is unambiguous and needs no heuristic — while parsing the listing
            // INTO menus and verbs would need one, and would trip over `edit` (present on several read-only
            // menus) and `unset` (present on /ip/neighbor, which has nothing to write).
            if (!(Connection is ITikCliCompletion completion))
            {
                Assert.Inconclusive($"Transport '{ResolveConnectionType()}' has no terminal to complete on. "
                                  + $"{nameof(AnExecutionProbeAgreesWithEveryNarrowedDeclaration)} covers "
                                  + "API and REST; see this class's remarks for native WinBox.");
                return;
            }

            var offenders = new List<string>();
            var skipped = new List<string>();

            foreach (var entity in NarrowedEntities())
            {
                IReadOnlyList<string> tokens = completion.CompleteCli(entity.Path + " ");
                if (tokens.Count == 0)
                {
                    // Never record an empty listing as "no verbs": /system/routerboard and
                    // /ip/accounting/* complete to nothing on the CHR because the menu is not there at
                    // all. Empty is unknown, and unknown is a skip.
                    skipped.Add(entity.Path + " (menu completes to nothing - absent on this router?)");
                    continue;
                }

                foreach (var (operation, verb) in Verbs)
                {
                    if (IsListedButBroken(entity.Path, verb))
                    {
                        skipped.Add($"{entity.Path}/{verb} (listed by the router, measured non-functional)");
                        continue;
                    }

                    bool onRouter = tokens.Contains(verb, StringComparer.Ordinal);
                    bool declared = entity.Operations.HasFlag(operation);
                    if (onRouter != declared)
                        offenders.Add($"{entity.Name} ({entity.Path}): declares {(declared ? "" : "no ")}"
                                    + $"'{verb}', router {(onRouter ? "offers" : "does not offer")} it");
                }
            }

            AssertAgreement(offenders, skipped, "tab completion");
        }

        [TestMethod]
        public void AnExecutionProbeAgreesWithEveryNarrowedDeclaration()
        {
            // The arbiter on the transports that have no completion. A verb that does not exist answers
            // "no such command"; one that does answers something else — and it really is "something else",
            // not "no such item": /ip/ipsec/active-peers/set answers the supout-file error, and a REST
            // remove of a nonexistent .id answers 404. Asserting on TikNoSuchItemException specifically
            // would fail those two while the declaration is right.
            var type = ResolveConnectionType();
            bool probeDiscriminates = type == TikConnectionType.Api || type == TikConnectionType.ApiSsl
                                   || type == TikConnectionType.Rest || type == TikConnectionType.RestSsl;
            if (!probeDiscriminates)
            {
                Assert.Inconclusive($"The execution probe cannot tell present from absent on '{type}' — see "
                                  + "this class's remarks. Tab completion covers the CLI family.");
                return;
            }

            var offenders = new List<string>();
            var skipped = new List<string>();

            foreach (var entity in NarrowedEntities())
            {
                if (!MenuExists(entity.Path))
                {
                    skipped.Add(entity.Path + " (menu not present on this router)");
                    continue;
                }

                foreach (var (operation, verb) in Verbs)
                {
                    if (IsListedButBroken(entity.Path, verb))
                    {
                        skipped.Add($"{entity.Path}/{verb} (listed by the router, measured non-functional)");
                        continue;
                    }
                    if (!ProbeCanAnswer(verb))
                    {
                        skipped.Add($"{entity.Path}/{verb} (the probe cannot answer '{verb}' on {type})");
                        continue;
                    }

                    bool onRouter = VerbExists(entity.Path, verb);
                    bool declared = entity.Operations.HasFlag(operation);
                    if (onRouter != declared)
                        offenders.Add($"{entity.Name} ({entity.Path}): declares {(declared ? "" : "no ")}"
                                    + $"'{verb}', router {(onRouter ? "offers" : "does not offer")} it");
                }
            }

            AssertAgreement(offenders, skipped, "the execution probe");
        }

        // ── Probes ────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Whether <paramref name="path"/>/<paramref name="verb"/> exists, without changing anything.
        /// </summary>
        /// <remarks>
        /// Two forms, because <c>add</c> takes no <c>.id</c>:
        /// <list type="bullet">
        /// <item><c>set</c>/<c>remove</c>/<c>move</c> address <c>.id=*7FFFFFFF</c>, an id no row can have.</item>
        /// <item><c>add</c> carries one parameter no menu has. RouterOS validates parameters <b>before</b>
        /// it creates anything, so an existing <c>add</c> answers "unknown parameter" and leaves no row —
        /// verified both ways round (<c>/ip/address/add</c> answers it, <c>/ppp/active/add</c> answers
        /// "no such command").</item>
        /// </list>
        /// A probe that unexpectedly SUCCEEDS is reported rather than swallowed: on the <c>add</c> form that
        /// would mean a row was created, and a silently-created row is exactly the residue that makes the
        /// next run fail somewhere else.
        /// </remarks>
        private bool VerbExists(string path, string verb)
        {
            var cmd = Connection.CreateCommand(path + "/" + verb, TikCommandParameterFormat.NameValue);
            if (verb == "add")
                cmd.AddParameter("t4n-probe-nonexistent", "x", TikCommandParameterFormat.NameValue);
            else
                cmd.AddParameter(TikSpecialProperties.Id, "*7FFFFFFF", TikCommandParameterFormat.NameValue);

            try
            {
                cmd.ExecuteNonQuery();
            }
            catch (TikNoSuchCommandException)
            {
                return false;
            }
            catch (TikCommandTrapException)
            {
                return true;   // "no such item", "unknown parameter", a supout error - the verb is there
            }

            Assert.Fail($"The safe probe '{path}/{verb}' SUCCEEDED, which it must never do. If this was the "
                      + "add form, a row may have been created on the router - check and remove it.");
            return true;   // unreachable
        }

        /// <summary>
        /// Whether the execution probe can tell present from absent for <paramref name="verb"/> on the
        /// active transport.
        /// </summary>
        /// <remarks>
        /// Only one case, and it is a property of the REST interface rather than of tik4net: REST has no
        /// verb called <c>add</c>. Creating is <c>PUT /rest/&lt;menu&gt;</c>, so a menu that cannot create
        /// is not "no such command" but <i>"missing or invalid resource identifier"</i> — measured on 7.24,
        /// <c>PUT /rest/log</c> against <c>PUT /rest/ip/address</c>, which answers <i>"unknown parameter
        /// t4n-probe-nonexistent"</i>. Both are plain 400s, so the distinction lives only in the message
        /// text, and a test that keyed on message text would be pinning RouterOS's wording rather than its
        /// behaviour.
        /// <para>
        /// <c>set</c>, <c>remove</c> and <c>move</c> are unaffected: they address a row
        /// (<c>PATCH /rest/log/*7FFFFFFF</c>) and a menu without them answers <i>no such command</i>, which
        /// maps to <see cref="TikNoSuchCommandException"/> as everywhere else. And <c>add</c> is not left
        /// uncovered — the API pair probes it, and the five CLI transports read it off the menu's own Tab
        /// listing.
        /// </para>
        /// </remarks>
        private bool ProbeCanAnswer(string verb)
        {
            if (verb != "add")
                return true;
            var type = ResolveConnectionType();
            return type != TikConnectionType.Rest && type != TikConnectionType.RestSsl;
        }

        /// <summary>
        /// Verbs RouterOS lists on a menu that do not work when invoked — the one place a declaration is
        /// allowed to disagree with the command tree, and only with the measurement written down.
        /// </summary>
        /// <remarks>
        /// <c>/ip/ipsec/active-peers</c> lists <c>add</c> and <c>set</c>: RouterOS's generic list machinery
        /// showing through on a status table. Neither performs. On 7.24 an <c>add</c> carrying the only
        /// parameter the menu accepts (<c>comment</c> — the completion after <c>add </c> offers only
        /// <c>comment</c> and <c>copy-from</c>) answers <i>"error - contact MikroTik support and send a
        /// supout file (3)"</i> and creates no row, and <c>set</c> answers the same instead of the
        /// <i>no such item</i> every other menu gives for an unused <c>.id</c>.
        /// <para>
        /// So the entity declares <see cref="TikEntityOperations.Remove"/>. Declaring the tree instead would
        /// make <see cref="TikEntityMetadata.AreFieldsReadOnly"/> false and turn every status field on the
        /// row into something <c>Save</c> would send, for two verbs that cannot accept them.
        /// </para>
        /// <para>
        /// <c>/ip/hotspot/active</c> is the same shape on RouterOS <b>before 7.20</b>, which still lists the two:
        /// on 7.19.6 <c>add</c> answers the supout-file error and <c>set 0</c> parses and answers <i>no such
        /// item</i>, while 7.24.4 does not list either (<c>bad command name add</c> / <c>set</c>). The entity
        /// declares what works on both.
        /// </para>
        /// </remarks>
        private static bool IsListedButBroken(string path, string verb)
            => (path == "/ip/ipsec/active-peers" || path == "/ip/hotspot/active")
               && (verb == "add" || verb == "set");

        /// <summary>The tracked connection to <paramref name="address"/>, or null. Addressed by destination, not by .id — see the test.</summary>
        private FirewallConnection TrackedConnectionTo(string address)
            => Connection.LoadList<FirewallConnection>(
                    Connection.CreateParameter("dst-address", address, TikCommandParameterFormat.Filter))
                .FirstOrDefault();

        /// <summary>Whether the menu itself is on this router — <c>/system/routerboard</c> is not, on a CHR.</summary>
        private bool MenuExists(string path)
        {
            try
            {
                Connection.CreateCommand(path + "/print").ExecuteList();
                return true;
            }
            catch (TikNoSuchCommandException)
            {
                return false;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Every mapped entity that claims to offer less than all four verbs, i.e. every claim this class
        /// can falsify. Action-style entities (<c>LoadCommand = ""</c> — ping, torch, the monitors) are not
        /// menus with rows and have no verbs to list, so they are not probed.
        /// </summary>
        private static IEnumerable<(string Name, string Path, TikEntityOperations Operations)> NarrowedEntities()
            => typeof(TikEntityAttribute).Assembly.GetTypes()
                .Where(t => t.IsClass && t.IsPublic)
                .Select(t => new { Type = t, Attribute = t.GetCustomAttribute<TikEntityAttribute>(false) })
                .Where(x => x.Attribute != null)
                .Where(x => x.Attribute.SupportedOperations != TikEntityOperations.All)
                .Where(x => !string.IsNullOrEmpty(x.Attribute.LoadCommand))
                .Select(x => (x.Type.Name,
                              x.Attribute.EntityPath.StartsWith("/", StringComparison.Ordinal)
                                  ? x.Attribute.EntityPath
                                  : "/" + x.Attribute.EntityPath,
                              x.Attribute.SupportedOperations))
                .OrderBy(x => x.Item2, StringComparer.Ordinal);

        private static void AssertAgreement(IList<string> offenders, IList<string> skipped, string mechanism)
        {
            if (offenders.Count == 0 && skipped.Count == NarrowedEntities().Count())
                Assert.Inconclusive($"Every narrowed menu was skipped, so {mechanism} measured nothing:"
                                  + Environment.NewLine + string.Join(Environment.NewLine, skipped));

            Assert.AreEqual(0, offenders.Count,
                $"SupportedOperations disagrees with the router, as reported by {mechanism}. Fix the "
                + "declaration on the entity (and re-run the unit tests, which pin some of these by name):"
                + Environment.NewLine + string.Join(Environment.NewLine, offenders)
                + (skipped.Count == 0 ? "" : Environment.NewLine + "Not measured: "
                                             + string.Join("; ", skipped)));
        }
    }
}
