// CliVerbTranslationTests.cs — what each verb actually puts on the wire.
//
// This is the coverage that was missing, and its absence is why `get` stayed broken: the O/R mapper never
// issues `get` (every entity loads through `/print`), ExecuteScalar reads through `print … where .id=` as
// well, and nothing in the integration suite named the verb itself — so a whole branch of the translator
// was reachable only by a caller writing `/interface/get` by hand, which nothing did until the MCP server
// started dispatching verbs from user input. The suite was green because it never asked the question.
//
// These run without a router, in CI, on every push. They assert the COMMAND TEXT rather than the answer,
// because the defect was entirely in what was sent: `/interface/get =.id=*2 =value-name=name` went out as
// `:put [/interface get as-value]`, with both inputs silently dropped.
//
// The last section is the other half of the same recording harness and does look at an answer: what `add`
// must do when the router's reply carries no .id. That one is not a translation bug — the command sent is
// right — but it is the same class of silence, and it is the one that leaves rows on the router.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    [TestClass]
    public class CliVerbTranslationTests
    {
        /// <summary>A CLI connection that records the command text instead of sending it.</summary>
        private sealed class RecordingCliConnection : CliConnectionBase
        {
            internal readonly List<string> Sent = new List<string>();
            internal string Reply = string.Empty;

            protected override string TransportName => "Recording";

            internal void OpenScripted()
                => OpenWith(_ => Task.FromResult(0),
                    (cliText, ct) => { Sent.Add(cliText); return Task.FromResult(Reply); },
                    (rawBytes, ct) => Task.FromResult(string.Empty),
                    () => { });

            public override void Open(string host, string user, string password) => OpenScripted();
            public override void Open(string host, int port, string user, string password) => OpenScripted();
            public override Task OpenAsync(string host, string user, string password) { OpenScripted(); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password) { OpenScripted(); return Task.FromResult(0); }
        }

        private static RecordingCliConnection Open(string reply = "")
        {
            var conn = new RecordingCliConnection { Reply = reply };
            conn.OpenScripted();
            return conn;
        }

        private static ITikCommandParameter NameValue(ITikConnection c, string name, string value)
            => c.CreateParameter(name, value, TikCommandParameterFormat.NameValue);

        // ── get ───────────────────────────────────────────────────────────────

        [TestMethod]
        public void GetByIdSelectsTheRowWithNumberNotDotId()
        {
            // Measured on RouterOS 7.24: `get .id=*2 value-name=name` is answered "bad parameter .id", and so
            // is `get [find .id=*2] name`. `number=` is the one that works, and it accepts an .id despite
            // being named for the ordinal.
            using (var conn = Open("ether1"))
            {
                conn.CreateCommand("/interface/get",
                    NameValue(conn, ".id", "*2"),
                    NameValue(conn, "value-name", "name")).ExecuteScalar();

                Assert.AreEqual(":put [/interface get number=*2 value-name=name]", conn.Sent.Single());
            }
        }

        [TestMethod]
        public void ASingletonGetNamesTheFieldAndAppendsNoAsValue()
        {
            // `get` takes the value name positionally, so a trailing `as-value` is read as the name of the
            // field to return — which is how /system/identity/get came to be refused with "input does not
            // match any value of value-name" rather than with anything about an id.
            using (var conn = Open("CHR"))
            {
                conn.CreateCommand("/system/identity/get",
                    NameValue(conn, "value-name", "name")).ExecuteScalar();

                string sent = conn.Sent.Single();
                Assert.AreEqual(":put [/system identity get value-name=name]", sent);
                StringAssert.DoesNotMatch(sent, new System.Text.RegularExpressions.Regex("as-value"));
            }
        }

        [TestMethod]
        public void GetWithNoValueNameAsksForTheWholeRow()
        {
            using (var conn = Open(".id=*2;name=ether1"))
            {
                conn.CreateCommand("/interface/get", NameValue(conn, ".id", "*2")).ExecuteScalar();

                Assert.AreEqual(":put [/interface get number=*2]", conn.Sent.Single());
            }
        }

        // ── the other verbs ───────────────────────────────────────────────────

        [TestMethod]
        public void PrintReadsAsValue()
        {
            using (var conn = Open(".id=*2;name=ether1"))
            {
                conn.CreateCommand("/interface/print").ExecuteList();
                Assert.AreEqual(":put [/interface print as-value]", conn.Sent.Single());
            }
        }

        [TestMethod]
        public void AddIsWrappedSoTheNewIdComesBack()
        {
            using (var conn = Open("*3"))
            {
                conn.CreateCommand("/ip/address/add",
                    NameValue(conn, "address", "10.0.0.1/24"),
                    NameValue(conn, "interface", "ether1")).ExecuteScalar();

                Assert.AreEqual(":put [/ip address add address=\"10.0.0.1/24\" interface=ether1]",
                    conn.Sent.Single());
            }
        }

        [DataTestMethod]
        [DataRow("set", "=comment=lan", "/ip address set [find where .id=*1] comment=lan")]
        [DataRow("remove", null, "/ip address remove [find where .id=*1]")]
        [DataRow("enable", null, "/ip address enable [find where .id=*1]")]
        [DataRow("disable", null, "/ip address disable [find where .id=*1]")]
        public void TheRowTargetingVerbsSelectWithFind(string verb, string extra, string expected)
        {
            using (var conn = Open())
            {
                var parameters = new List<ITikCommandParameter> { NameValue(conn, ".id", "*1") };
                if (extra != null)
                {
                    var parts = extra.TrimStart('=').Split('=');
                    parameters.Add(NameValue(conn, parts[0], parts[1]));
                }

                conn.CreateCommand("/ip/address/" + verb, parameters.ToArray()).ExecuteNonQuery();

                Assert.AreEqual(expected, conn.Sent.Single());
            }
        }

        [TestMethod]
        public void UnsetNamesItsTargetField()
        {
            using (var conn = Open())
            {
                conn.CreateCommand("/ip/firewall/filter/unset",
                    NameValue(conn, ".id", "*1"),
                    NameValue(conn, "value-name", "connection-mark")).ExecuteNonQuery();

                Assert.AreEqual("/ip firewall filter unset [find where .id=*1] value-name=connection-mark",
                    conn.Sent.Single());
            }
        }

        [TestMethod]
        public void MoveCarriesItsDestination()
        {
            using (var conn = Open())
            {
                conn.CreateCommand("/ip/firewall/filter/move",
                    NameValue(conn, ".id", "*1"),
                    NameValue(conn, "destination", "*3")).ExecuteNonQuery();

                StringAssert.Contains(conn.Sent.Single(), "[find where .id=*1]");
                StringAssert.Contains(conn.Sent.Single(), "destination=\"*3\"");   // move quotes its target id
            }
        }

        // ── what add must do when no .id comes back ───────────────────────────
        //
        // An `add` over a terminal can answer with no id. The command reached the router and the row was
        // created; what was lost is the reply naming it, because a terminal has no framing and a reply that
        // arrives after the prompt has gone quiet is not read at all. The library used to hand that back as
        // an empty id, and the caller's cleanup then had nothing to delete — which is how a test run
        // leaves an EoIP interface or an IPsec peer behind, and why the NEXT run on another transport fails
        // with a name collision rather than with the original error.

        [TestMethod]
        public void AnAddWhoseAnswerCarriesNoIdIsReported()
        {
            using (var conn = Open(reply: string.Empty))
            {
                var cmd = conn.CreateCommand("/interface/eoip/add",
                    NameValue(conn, "name", "test-eoip"),
                    NameValue(conn, "remote-address", "10.0.0.2"));

                var ex = Assert.ThrowsException<TikAddIdNotReadException>(() => cmd.ExecuteScalar());

                // The message has to say the row is probably THERE. "Add failed" would be the wrong thing to
                // believe: retrying on that belief makes a second row.
                StringAssert.Contains(ex.Message, "created");
            }
        }

        [TestMethod]
        public void AnAnswerThatIsNotAnIdIsNotUsedAsOne()
        {
            // The old fallback took the last non-empty line whatever it was, so a stray line of terminal
            // noise became the entity's .id. Nothing rejects it at that point — it travels into the
            // `[find where .id=…]` of the next set or remove and matches nothing there, which surfaces far
            // from the add that caused it. '3' is the shape that makes the point: an id is '*' plus hex, so
            // a lone number is not one however plausible it looks.
            using (var conn = Open(reply: "3"))
            {
                var cmd = conn.CreateCommand("/interface/eoip/add", NameValue(conn, "name", "test-eoip"));

                Assert.ThrowsException<TikAddIdNotReadException>(() => cmd.ExecuteScalar());
            }
        }

        [TestMethod]
        public void AnAddTheRouterREFUSEDIsNotReportedAsALostId()
        {
            // The distinction the new exception has to preserve: an add the router rejected did NOT create a
            // row, and saying "it probably exists, go find it" about one would send the caller looking for
            // something that is not there. A recognised router error still surfaces as itself, because the
            // error check runs before the id is looked for at all.
            using (var conn = Open(reply: "bad command name eoip (line 1 column 12)"))
            {
                var cmd = conn.CreateCommand("/interface/eoip/add", NameValue(conn, "name", "test-eoip"));

                Assert.ThrowsException<TikNoSuchCommandException>(() => cmd.ExecuteScalar());
            }
        }

        [TestMethod]
        public void TheAnswerTheRouterNormallyGivesIsStillRead()
        {
            using (var conn = Open(reply: "*3"))
                Assert.AreEqual("*3", conn.CreateCommand("/interface/eoip/add",
                    NameValue(conn, "name", "test-eoip")).ExecuteScalar());
        }

        [TestMethod]
        public void AnIdAfterContinuationLinesIsStillFound()
        {
            // A parameter value containing newlines (a script `source`) puts the line editor into
            // bracket-continuation mode, and the continuation lines are echoed BEFORE the result — so the id
            // is not the only line, and it is the last one.
            string continued = "[\"... " + Environment.NewLine + "[\"... " + Environment.NewLine + "*A";

            using (var conn = Open(reply: continued))
                Assert.AreEqual("*A", conn.CreateCommand("/system/script/add",
                    NameValue(conn, "name", "s")).ExecuteScalar());
        }

        [TestMethod]
        public void WhatTheRouterSaidIsCarriedWithTheFailure()
        {
            // A timeout or a truncation that does not say what the other side sent leaves nothing to
            // diagnose from — the difference between "nothing arrived" and "something arrived that was not
            // an id" is the whole diagnosis.
            using (var conn = Open(reply: "interface with such name already exists"))
            {
                var cmd = conn.CreateCommand("/interface/eoip/add", NameValue(conn, "name", "test-eoip"));
                var ex = Assert.ThrowsException<TikAddIdNotReadException>(() => cmd.ExecuteScalar());

                StringAssert.Contains(ex.Message, "already exists");
            }
        }

        // ── the guard that would have caught the get bug ──────────────────────

        [TestMethod]
        public void NoVerbSilentlyLosesTheRowItWasGiven()
        {
            // The shape of the original defect: a verb with no branch of its own fell through to the print
            // builder, which understands only print modifiers and a where-clause — so it dropped the .id and
            // appended `as-value`, turning a per-row command into a read of the whole menu. That failure is
            // invisible in the command's RESULT (the router answers something plausible), so it is asserted
            // here on what was sent.
            var verbs = new[] { "get", "set", "remove", "enable", "disable", "comment", "unset", "move" };
            var lost = new List<string>();

            foreach (string verb in verbs)
            {
                using (var conn = Open("x"))
                {
                    var cmd = conn.CreateCommand("/ip/firewall/filter/" + verb,
                        NameValue(conn, ".id", "*7"),
                        NameValue(conn, "value-name", "comment"),
                        NameValue(conn, "destination", "*9"),
                        NameValue(conn, "comment", "c"));
                    try
                    {
                        if (verb == "get") cmd.ExecuteScalar(); else cmd.ExecuteNonQuery();
                    }
                    catch (TikCommandException)
                    {
                        // What the router would say is not the subject; what we sent is.
                    }

                    string sent = conn.Sent.SingleOrDefault() ?? "(nothing sent)";
                    if (!sent.Contains("*7"))
                        lost.Add($"{verb} → {sent}");
                }
            }

            Assert.AreEqual(0, lost.Count,
                "these verbs dropped the .id they were given, so the command reached the router aimed at the "
                + "whole menu instead of at one row:" + System.Environment.NewLine
                + string.Join(System.Environment.NewLine, lost));
        }

        [TestMethod]
        public void NoVerbIsTranslatedAsIfItWerePrint()
        {
            // The other half of the same defect. Losing the .id was the visible symptom; the cause was that
            // an unbranched verb reached BuildPrint, which rewrites the command into a read of the menu —
            // `:put [/path <verb> as-value]`. That text is well-formed and the router answers it, so nothing
            // upstream can notice. Any verb whose translation still looks like a print is either untranslated
            // or translated wrongly.
            var offenders = new List<string>();

            foreach (string verb in new[] { "get", "set", "remove", "enable", "disable", "unset", "move" })
            {
                using (var conn = Open("x"))
                {
                    var cmd = conn.CreateCommand("/ip/firewall/filter/" + verb,
                        NameValue(conn, ".id", "*7"),
                        NameValue(conn, "value-name", "comment"),
                        NameValue(conn, "destination", "*9"));
                    try
                    {
                        if (verb == "get") cmd.ExecuteScalar(); else cmd.ExecuteNonQuery();
                    }
                    catch (TikCommandException) { }

                    string sent = conn.Sent.SingleOrDefault() ?? "(nothing sent)";
                    if (sent.Contains(" as-value"))
                        offenders.Add($"{verb} → {sent}");
                }
            }

            Assert.AreEqual(0, offenders.Count,
                "these verbs were rendered as a print of the whole menu:" + System.Environment.NewLine
                + string.Join(System.Environment.NewLine, offenders));
        }
    }
}
