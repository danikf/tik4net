// Nullable-enabled on its own: the test project as a whole is not (see the note in
// Directory.Build.props), but this file implements ITikCommandParameter's annotated surface.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;
using tik4net.Connection;
using tik4net.Cli;
using tik4net.Objects;
using tik4net.Rest;
using tik4net.unittests.Api;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// Flag fields (<c>disabled</c>, <c>running</c>, <c>dynamic</c>, …) on a router whose <c>print as-value</c>
    /// leaves them out — RouterOS before 7.20. The router double answers the way 7.19.6 and 7.24.4 were measured
    /// to: see <see cref="TikSpecialProperties.CliFlags"/> and Docs/findings-cli.md.
    /// </summary>
    [TestClass]
    public class CliFlagFieldsTests
    {
        // The measured refusal of a proplist naming a field the menu does not have (7.17, 7.19.6, 7.24.4).
        private const string ProplistRefusal = "input does not match any value of value-name";

        [TikEntity("/iface")]
        private sealed class FlagProbe
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; set; }

            [TikProperty("name")]
            public string? Name { get; set; }

            [TikProperty("disabled")]
            public bool? Disabled { get; set; }

            [TikProperty("running", IsReadOnly = true)]
            public bool Running { get; set; }

            // A flag this entity maps but the menu does not have on this version: the router refuses a proplist
            // naming it, so it must be left out of the flags read rather than sink it.
            [TikProperty("slave", IsReadOnly = true)]
            public bool Slave { get; set; }

            // A writable bool is an ordinary field: every version prints it, so it is not a flag.
            [TikProperty("log")]
            public bool? Log { get; set; }
        }

        // A singleton with a flag. RouterOS takes no proplist= on a singleton at all — 7.19.6 answers "expected end of
        // command", 7.24.4 "bad parameter proplist" — and its plain print as-value carries the flag on both
        // (/system clock dst-active, /ip settings ipv4-fast-path-active; measured).
        [TikEntity("/clock", IsSingleton = true)]
        private sealed class SingletonFlagProbe
        {
            [TikProperty("dst-active", IsReadOnly = true)]
            public bool DstActive { get; set; }

            [TikProperty("time-zone-name")]
            public string? TimeZoneName { get; set; }
        }

        [TikEntity("/iface")]
        private sealed class NoFlagProbe
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; set; }

            [TikProperty("name")]
            public string? Name { get; set; }
        }

        // ── RouterOS before 7.20 ─────────────────────────────────────────────

        [TestMethod]
        public void BeforeRouterOs720_TheFlagsAreReadByName_AndLandOnTheEntity()
        {
            using (var conn = new FlagRouter(printsFlags: false))
            {
                conn.OpenScripted();

                var rows = conn.LoadAll<FlagProbe>().ToList();

                Assert.AreEqual(3, rows.Count);
                CollectionAssert.AreEqual(new[] { true, false, true }, rows.Select(r => r.Running).ToList(),
                    "running must come from the flags read, not default to false");
                CollectionAssert.AreEqual(new bool?[] { false, true, false }, rows.Select(r => r.Disabled).ToList());
                Assert.IsTrue(rows.All(r => r.Log == false), "the ordinary field still comes from the first read");
            }
        }

        [TestMethod]
        public void BeforeRouterOs720_ANameTheMenuRefuses_IsLeftOut_AndTheOthersStillArrive()
        {
            using (var conn = new FlagRouter(printsFlags: false))
            {
                conn.OpenScripted();

                conn.LoadAll<FlagProbe>().ToList();

                string flagsRead = conn.Sent.Single(s => s.Contains(" proplist=") && !s.Contains("where false"));
                StringAssert.Matches(flagsRead, new Regex(@"proplist=disabled,running[\] ]"));
                Assert.IsFalse(flagsRead.Contains("slave"), "a refused name must never reach the read: " + flagsRead);
                Assert.IsFalse(flagsRead.Contains("log"), "a writable bool is not a flag: " + flagsRead);
            }
        }

        [TestMethod]
        public void BeforeRouterOs720_TheNameCheckIsThePrintItselfWithNoRows()
        {
            using (var conn = new FlagRouter(printsFlags: false))
            {
                conn.OpenScripted();

                conn.LoadAll<FlagProbe>().ToList();

                Assert.AreEqual(":put [/iface print as-value proplist=disabled,running,slave where false]",
                    conn.Sent.First(s => s.Contains("where false")));
            }
        }

        [TestMethod]
        public void BeforeRouterOs720_ANameCheckTheRouterCannotParse_Throws_RatherThanDroppingEveryFlag()
        {
            using (var conn = new FlagRouter(printsFlags: false) { SyntaxErrorOnCheck = true })
            {
                conn.OpenScripted();

                var ex = Assert.ThrowsException<TikCommandTrapException>(() => conn.LoadAll<FlagProbe>().ToList());
                StringAssert.Contains(ex.Message, "expected end of command");
            }
        }

        [TestMethod]
        public void BeforeRouterOs720_TheProbeAndTheNameCheckAreAskedOnce_ThenEachReadCostsOneMoreCommand()
        {
            using (var conn = new FlagRouter(printsFlags: false))
            {
                conn.OpenScripted();
                conn.LoadAll<FlagProbe>().ToList();
                int first = conn.Sent.Count;

                conn.LoadAll<FlagProbe>().ToList();

                Assert.AreEqual(1, conn.Sent.Count(s => s == CliCommandBuilder.FlagsProbe));
                Assert.AreEqual(2, conn.Sent.Count - first,
                    "a later read is the plain print plus the flags print: " + string.Join(" | ", conn.Sent.Skip(first)));
            }
        }

        [TestMethod]
        public void BeforeRouterOs720_APagedRead_ReadsTheFlagsOfEveryWindow()
        {
            using (var conn = new FlagRouter(printsFlags: false))
            {
                conn.OpenScripted();
                conn.CliReadPageSize = 2;

                var rows = conn.LoadAll<FlagProbe>().ToList();

                CollectionAssert.AreEqual(new[] { true, false, true }, rows.Select(r => r.Running).ToList());
                Assert.AreEqual(2, conn.Sent.Count(s => s.Contains(":pick") && s.Contains(" proplist=")),
                    "three rows at page size 2 are two windows, and the flags read has the same two");
            }
        }

        [TestMethod]
        public void BeforeRouterOs720_AFilteredRead_ReadsTheFlagsOfTheSameRows()
        {
            using (var conn = new FlagRouter(printsFlags: false))
            {
                conn.OpenScripted();

                var row = conn.LoadList<FlagProbe>(conn.CreateParameter("name", "e1", TikCommandParameterFormat.Filter)).Single();

                Assert.IsFalse(row.Running);
                Assert.AreEqual(true, row.Disabled);
                string flagsRead = conn.Sent.Single(s => s.Contains(" proplist=") && !s.Contains("where false"));
                StringAssert.Contains(flagsRead, "where name=e1");
            }
        }

        /// <summary>
        /// A singleton's flags are in its plain read, and it refuses proplist= outright — so no flags read is asked
        /// for, which would otherwise fail the whole load (every singleton with a read-only bool, on 7.19.6).
        /// </summary>
        [TestMethod]
        public void BeforeRouterOs720_ASingletonIsReadInOneCommand_WithItsFlags()
        {
            using (var conn = new FlagRouter(printsFlags: false))
            {
                conn.OpenScripted();

                var clock = conn.LoadSingle<SingletonFlagProbe>();

                Assert.IsTrue(clock.DstActive);
                Assert.AreEqual("Europe/Prague", clock.TimeZoneName);
                Assert.IsFalse(conn.Sent.Any(s => s.Contains("proplist=")), string.Join(" | ", conn.Sent));
            }
        }

        /// <summary>
        /// A low-level print that names a flag in <c>.proplist</c> gets it, as it does from the binary API. RouterOS
        /// before 7.20 leaves the flag out of the plain read, so the named ones are read by name like the mapper's.
        /// </summary>
        [TestMethod]
        public void BeforeRouterOs720_AFlagNamedInProplist_IsReadByName()
        {
            using (var conn = new FlagRouter(printsFlags: false))
            {
                conn.OpenScripted();

                var rows = conn.CreateCommand("/iface/print",
                        conn.CreateParameter(TikSpecialProperties.Proplist, ".id,name,running", TikCommandParameterFormat.NameValue))
                    .ExecuteList().ToList();

                CollectionAssert.AreEqual(new[] { "true", "false", "true" },
                    rows.Select(r => r.GetResponseFieldOrDefault("running", "(missing)")).ToList());
                CollectionAssert.AreEqual(new[] { "*1", "*2", "*3" }, rows.Select(r => r.GetId()).ToList());
            }
        }

        /// <summary>
        /// A name in <c>.proplist</c> the menu does not have is ignored, as the API ignores it — the router's refusal
        /// of that one name must not sink the read or the flags beside it.
        /// </summary>
        [TestMethod]
        public void BeforeRouterOs720_AnUnknownNameInProplist_IsIgnored_AndTheFlagStillArrives()
        {
            using (var conn = new FlagRouter(printsFlags: false))
            {
                conn.OpenScripted();

                var rows = conn.CreateCommand("/iface/print",
                        conn.CreateParameter(TikSpecialProperties.Proplist, ".id,name,running,no-such-field", TikCommandParameterFormat.NameValue))
                    .ExecuteList().ToList();

                CollectionAssert.AreEqual(new[] { "true", "false", "true" },
                    rows.Select(r => r.GetResponseFieldOrDefault("running", "(missing)")).ToList());
                Assert.IsFalse(rows.Any(r => r.Words.ContainsKey("no-such-field")));
            }
        }

        /// <summary>A low-level print without a <c>.proplist</c> gets what the router prints, and asks nothing more.</summary>
        [TestMethod]
        public void BeforeRouterOs720_APlainLowLevelPrint_AsksNothingMore()
        {
            using (var conn = new FlagRouter(printsFlags: false))
            {
                conn.OpenScripted();

                conn.CreateCommand("/iface/print").ExecuteList().ToList();

                Assert.AreEqual(1, conn.Sent.Count, string.Join(" | ", conn.Sent));
            }
        }

        // ── RouterOS 7.20 and later ──────────────────────────────────────────

        [TestMethod]
        public void FromRouterOs720_TheFlagsAreAlreadyThere_AndNothingMoreIsAsked()
        {
            using (var conn = new FlagRouter(printsFlags: true))
            {
                conn.OpenScripted();
                var rows = conn.LoadAll<FlagProbe>().ToList();
                int first = conn.Sent.Count;
                conn.LoadAll<FlagProbe>().ToList();

                CollectionAssert.AreEqual(new[] { true, false, true }, rows.Select(r => r.Running).ToList());
                Assert.IsFalse(conn.Sent.Any(s => s.Contains(" proplist=")), string.Join(" | ", conn.Sent));
                Assert.AreEqual(1, conn.Sent.Count - first, "a later read is the one print and nothing else");
            }
        }

        // ── What does not ask ────────────────────────────────────────────────

        [TestMethod]
        public void AnEntityWithoutFlags_SendsNoProbe()
        {
            using (var conn = new FlagRouter(printsFlags: false))
            {
                conn.OpenScripted();

                conn.LoadAll<NoFlagProbe>().ToList();

                Assert.AreEqual(1, conn.Sent.Count, string.Join(" | ", conn.Sent));
            }
        }

        [TestMethod]
        public void TheMarkerNeverBecomesACliWord()
        {
            var pars = new List<ITikCommandParameter>
            {
                new TikCommandParameter(TikSpecialProperties.CliFlags, "disabled,running", TikCommandParameterFormat.NameValue),
                new TikCommandParameter("name", "e1", TikCommandParameterFormat.Filter),
            };

            Assert.AreEqual(":put [/iface print as-value where name=e1]", CliCommandBuilder.BuildPrint("/iface/print", pars));
        }

        [TestMethod]
        public void RestNeverSendsTheMarker()
        {
            var req = RestRequestBuilder.Build("/interface/print", new List<ITikCommandParameter>
            {
                new TikCommandParameter(TikSpecialProperties.CliFlags, "disabled,running", TikCommandParameterFormat.NameValue),
            });

            Assert.IsFalse(req.RelativePath.Contains("cli-flags"), req.RelativePath);
            Assert.IsFalse((req.JsonBody ?? string.Empty).Contains("cli-flags"), req.JsonBody);
        }

        [TestMethod]
        public void TheBinaryApiNeverSendsTheMarker()
        {
            using var server = new FakeRouterServer();
            List<string>? command = null;
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();               // login
                server.WriteSentence("!done");
                command = server.ReadSentence();
                server.WriteSentence("!re", "=.id=*1", "=name=e0", "=running=true", "=disabled=false");
                server.WriteSentence("!done");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.ReceiveTimeout = 2000;
                connection.Open("127.0.0.1", server.Port, "admin", "secret");

                var row = connection.LoadAll<FlagProbe>().Single();

                Assert.IsTrue(row.Running);
            }

            Assert.IsTrue(serverTask.Wait(5000));
            Assert.IsFalse(command!.Any(w => w.Contains("cli-flags")), string.Join(" ", command!));
        }

        // ── The router double ────────────────────────────────────────────────

        /// <summary>
        /// A menu <c>/iface</c> with three rows. <paramref name="printsFlags"/> chooses the version: 7.20+ prints
        /// <c>disabled</c> and <c>running</c> in every as-value answer, earlier versions only when asked by
        /// name. Either way the menu has no <c>slave</c> field, and a proplist naming one is refused.
        /// </summary>
        private sealed class FlagRouter : CliConnectionBase
        {
            private static readonly string[] Names = { "e0", "e1", "e2" };
            private static readonly bool[] RunningValues = { true, false, true };
            private static readonly bool[] DisabledValues = { false, true, false };
            private static readonly HashSet<string> KnownFields =
                new HashSet<string>(StringComparer.Ordinal) { "name", "disabled", "running", "log", "comment" };

            private const string SyntaxError = "expected end of command (line 1 column 24)";

            private readonly bool _printsFlags;
            public readonly List<string> Sent = new List<string>();

            /// <summary>When set, the name check is answered with a syntax error instead of its real answer.</summary>
            public bool SyntaxErrorOnCheck;

            public FlagRouter(bool printsFlags) => _printsFlags = printsFlags;

            protected override string TransportName => "Flags";

            public void OpenScripted()
                => OpenWith(_ => Task.FromResult(0), SendAsync, (raw, ct) => Task.FromResult(string.Empty), () => { });

            private Task<string> SendAsync(string cliText, CancellationToken ct)
            {
                Sent.Add(cliText);

                // The singleton: its plain read carries every field, and it takes no proplist= at all.
                if (cliText.Contains("/clock print"))
                    return Task.FromResult(cliText.Contains("proplist=")
                        ? "expected end of command (line 1 column 36)"
                        : CountedReadFake.Answer(cliText, "date=2026-09-19;dst-active=true;gmt-offset=7200;time-zone-name=Europe/Prague"));

                // Only the shapes this menu really answers; anything else gets the router's syntax error, as
                // '/interface print print …' did on 7.19.6.
                if (!Regex.IsMatch(cliText, @"/iface print (detail |stats )?as-value") && cliText != CliCommandBuilder.FlagsProbe)
                    return Task.FromResult(SyntaxError);
                if (SyntaxErrorOnCheck && cliText.Contains(" where false]"))
                    return Task.FromResult(SyntaxError);

                if (cliText == CliCommandBuilder.FlagsProbe)
                    return Task.FromResult(_printsFlags
                        ? ".id=*1;address=;disabled=false;invalid=false;name=ftp;port=21"
                        : ".id=*1;address=;name=ftp;port=21");

                var proplist = Regex.Match(cliText, @" proplist=(?<names>[^ \]]+)");
                string[]? asked = proplist.Success ? proplist.Groups["names"].Value.Split(',') : null;
                if (asked != null && asked.Any(n => !KnownFields.Contains(n)))
                    return Task.FromResult(ProplistRefusal);
                if (cliText.Contains(" where false]"))
                    return Task.FromResult(string.Empty);

                var filter = Regex.Match(cliText, @"(?:where |find \()name=(?<name>e\d)");
                var rows = Enumerable.Range(0, Names.Length)
                    .Where(i => !filter.Success || Names[i] == filter.Groups["name"].Value)
                    .ToList();

                var pick = Regex.Match(cliText, @":pick \[[^\[\]]*? find(?: \([^)]*\))?\] (?<from>\d+) (?<to>\d+)\]");
                if (pick.Success)
                {
                    int from = int.Parse(pick.Groups["from"].Value), to = int.Parse(pick.Groups["to"].Value);
                    var window = rows.Skip(from).Take(to - from).ToList();
                    return Task.FromResult(Rows(window, asked) + (char)10 + "#w=" + window.Count);
                }
                return Task.FromResult(Rows(rows, asked) + (char)10 + "#n=" + rows.Count + "/num");
            }

            private string Rows(IEnumerable<int> rows, string[]? asked)
                => string.Join(";", rows.Select(i => Row(i, asked)));

            private string Row(int i, string[]? asked)
            {
                var fields = new List<string> { ".id=*" + (i + 1), "comment=" };
                bool Wants(string f) => asked == null || asked.Contains(f);
                if (asked == null || _printsFlags)
                {
                    if (Wants("name")) fields.Add("name=" + Names[i]);
                    if (Wants("log")) fields.Add("log=false");
                }
                // A flag is printed when the version prints flags, or when the proplist names it.
                if ((asked == null && _printsFlags) || (asked != null && asked.Contains("disabled")))
                    fields.Add("disabled=" + (DisabledValues[i] ? "true" : "false"));
                if ((asked == null && _printsFlags) || (asked != null && asked.Contains("running")))
                    fields.Add("running=" + (RunningValues[i] ? "true" : "false"));
                return string.Join(";", fields);
            }

            public override void Open(string host, string user, string password) => OpenScripted();
            public override void Open(string host, int port, string user, string password) => OpenScripted();
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
        }
    }
}
