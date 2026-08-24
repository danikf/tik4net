// CliQuotingTests.cs — router-free tests for P2.38: the CLI value quoting must survive RouterOS's own
// string rules instead of being rewritten by them.
//
// Every expectation below was measured against RouterOS 7.23.2 over telnet before being encoded here
// (probe: Tools/probes/telnet-cli-probe.ps1). What the router does inside a double-quoted value:
//
//   /system script add name=X source="x\$y z"     -> stored  x$y z      (\$ is the literal-dollar escape)
//   /system script add name=X source="x$y z"      -> stored  x z        (SILENT substitution — the bug)
//   /system script add name=X source="x\\y z"     -> stored  x\y z      (\\ is the literal-backslash escape)
//   /system script add name=X source="x\"b"       -> stored  x"b
//   /system script add name=X source="C:\temp\new w"  -> stored  C:<TAB>emp<LF>ew w   (SILENT)
//   /system script add name=X source="x\y z"      -> syntax error       (unknown escape)
//   /system script add name=X source=x$y          -> syntax error       (unquoted $ is never legal)
//   /system script add name=X source=x\y          -> syntax error       (unquoted \ is never legal)
//   :put 'a$b'                                    -> syntax error       (RouterOS has no single-quoted string)
//
// So: a value carrying '$' or '\' must be quoted (unquoted is a hard error), and once quoted both must
// be escaped (unquoted-inside-quotes is silent corruption).

using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    [TestClass]
    public class CliQuotingTests
    {
        // ── QuoteIfNeeded: name=value arguments for add/set ────────────────────

        /// <summary>A bare word goes out bare — letters, digits, and the three joiners RouterOS builds
        /// nearly every value from.</summary>
        [TestMethod]
        public void QuoteIfNeeded_PlainValue_IsNotQuoted()
        {
            Assert.AreEqual("ether1", CliCommandBuilder.QuoteIfNeeded("ether1"));
            Assert.AreEqual("wpa2-psk", CliCommandBuilder.QuoteIfNeeded("wpa2-psk"));
            Assert.AreEqual("1.5", CliCommandBuilder.QuoteIfNeeded("1.5"));
            Assert.AreEqual("10.99.0.10-10.99.0.20", CliCommandBuilder.QuoteIfNeeded("10.99.0.10-10.99.0.20"));
            Assert.AreEqual("tik4net_x", CliCommandBuilder.QuoteIfNeeded("tik4net_x"));
        }

        /// <summary>
        /// Punctuation is quoted even where an unquoted value would have been accepted.
        /// </summary>
        /// <remarks>
        /// <c>192.168.1.1/24</c> used to go out bare and works bare — for an IP-prefix parameter. RouterOS
        /// parses an unquoted value by the PARAMETER'S type, so the same characters are a syntax error on a
        /// script-typed one: <c>/system/script</c>'s <c>source=:nothing</c> was refused on every CLI
        /// transport while the API accepted it. Since the builder does not know the parameter's type, the
        /// rule is an allow-list, and quoting is free — each of these round-trips unchanged on the router.
        /// </remarks>
        [TestMethod]
        public void QuoteIfNeeded_Punctuation_IsQuotedEvenWhereBareWouldHaveWorked()
        {
            Assert.AreEqual("\"192.168.1.1/24\"", CliCommandBuilder.QuoteIfNeeded("192.168.1.1/24"));
            Assert.AreEqual("\"00:15:5D:04:1F:03\"", CliCommandBuilder.QuoteIfNeeded("00:15:5D:04:1F:03"));
            Assert.AreEqual("\"*5D\"", CliCommandBuilder.QuoteIfNeeded("*5D"));
            Assert.AreEqual("\"syn,!ack\"", CliCommandBuilder.QuoteIfNeeded("syn,!ack"));
        }

        /// <summary>
        /// The one that started it: a script source. Every character measured as breaking an unquoted
        /// script-typed value on RouterOS 7.24, leading and mid-value.
        /// </summary>
        [TestMethod]
        public void QuoteIfNeeded_ScriptSource_IsQuoted()
        {
            Assert.AreEqual("\":nothing\"", CliCommandBuilder.QuoteIfNeeded(":nothing"));
            foreach (char c in ":[](){}'?!~<>|&,*/+=")
            {
                string lead = c + "abc", mid = "a" + c + "bc";
                Assert.AreEqual("\"" + lead + "\"", CliCommandBuilder.QuoteIfNeeded(lead), "leading '" + c + "'");
                Assert.AreEqual("\"" + mid + "\"", CliCommandBuilder.QuoteIfNeeded(mid), "mid '" + c + "'");
            }
        }

        [TestMethod]
        public void QuoteIfNeeded_Dollar_IsQuotedAndEscaped()
        {
            // The P2.38 defect: this used to go out as a bare  x$y  (syntax error) or, with a space
            // anywhere in the value, as  "x$y"  which RouterOS silently stored as  x .
            Assert.AreEqual("\"x\\$y\"", CliCommandBuilder.QuoteIfNeeded("x$y"));
            Assert.AreEqual("\"x\\$y z\"", CliCommandBuilder.QuoteIfNeeded("x$y z"));
        }

        [TestMethod]
        public void QuoteIfNeeded_Backslash_IsQuotedAndEscaped()
        {
            Assert.AreEqual("\"x\\\\y\"", CliCommandBuilder.QuoteIfNeeded(@"x\y"));
            // Previously emitted verbatim, so the router read \t and \n as escapes and stored
            // C:<TAB>emp<LF>ew — a path silently turned into control characters.
            Assert.AreEqual("\"C:\\\\temp\\\\new w\"", CliCommandBuilder.QuoteIfNeeded(@"C:\temp\new w"));
        }

        [TestMethod]
        public void QuoteIfNeeded_DoubleQuote_IsStillEscaped()
        {
            Assert.AreEqual("\"a\\\"b\"", CliCommandBuilder.QuoteIfNeeded("a\"b"));
        }

        [TestMethod]
        public void QuoteIfNeeded_BackslashIsEscapedBeforeTheOthers()
        {
            // Order matters: escaping '$' first would produce \$ and the backslash pass would then
            // double it into \\$ — i.e. a literal backslash followed by a substitution.
            Assert.AreEqual("\"\\\\\\$\"", CliCommandBuilder.QuoteIfNeeded(@"\$"));
            Assert.AreEqual("\"\\\\\\\"\"", CliCommandBuilder.QuoteIfNeeded("\\\""));
        }

        [TestMethod]
        public void QuoteIfNeeded_RealScriptSource_KeepsItsVariables()
        {
            // The motivating case: a RouterOS script that touches a variable — i.e. essentially all
            // of them. Before the fix the router stored  :put ( . )  and nothing trapped.
            const string source = ":local a 1;\n:put ($a . $b)\n";

            Assert.AreEqual("\":local a 1;\n:put (\\$a . \\$b)\n\"",
                CliCommandBuilder.QuoteIfNeeded(source));
        }

        [TestMethod]
        public void QuoteIfNeeded_NewlineAndTab_AreLeftAsRealCharacters()
        {
            // RouterOS accepts a real line break inside an open quoted value — that is how a
            // multi-line script source round-trips today (P2.17). Do not turn it into \n, which
            // would only work by accident and would break a value carrying a literal backslash-n.
            Assert.AreEqual("\"a\nb\"", CliCommandBuilder.QuoteIfNeeded("a\nb"));
            Assert.AreEqual("\"a\tb\"", CliCommandBuilder.QuoteIfNeeded("a\tb"));
        }

        [TestMethod]
        public void QuoteIfNeeded_Empty_BecomesTheExplicitEmptyLiteral()
        {
            // Was passed through as "", which put a bare 'name=' on the line. RouterOS rejects that as soon
            // as anything follows it:
            //   /system note set note= show-at-login=yes  ->  expected end of command (line 1 column 37)
            // (measured on 7.23.2). So clearing a field over a CLI transport always failed mid-line, and
            // only accidentally worked when the empty value happened to be the last argument.
            Assert.AreEqual("\"\"", CliCommandBuilder.QuoteIfNeeded(""));
            Assert.IsNull(CliCommandBuilder.QuoteIfNeeded(null));
        }

        // ── QuoteForWhere: operands of a where-condition ───────────────────────

        [TestMethod]
        public void QuoteForWhere_SafeValue_IsNotQuoted()
        {
            Assert.AreEqual("ether1", CliCommandBuilder.QuoteForWhere("ether1"));
            Assert.AreEqual("some_name-2.0", CliCommandBuilder.QuoteForWhere("some_name-2.0"));
        }

        [TestMethod]
        public void QuoteForWhere_Dollar_IsEscaped()
        {
            // '$' is outside the safe set, so the value was already being quoted — and then
            // substituted away inside those quotes, silently matching the wrong rows.
            Assert.AreEqual("\"a\\$b\"", CliCommandBuilder.QuoteForWhere("a$b"));
        }

        [TestMethod]
        public void QuoteForWhere_BackslashAndQuote_StillEscaped()
        {
            Assert.AreEqual("\"a\\\\b\"", CliCommandBuilder.QuoteForWhere(@"a\b"));
            Assert.AreEqual("\"a\\\"b\"", CliCommandBuilder.QuoteForWhere("a\"b"));
        }

        [TestMethod]
        public void QuoteForWhere_AddressKeepsBeingQuoted()
        {
            // The original reason QuoteForWhere exists (P2.30): '/' is an operator in expression
            // context, so where address=192.168.1.1/24 matches nothing.
            Assert.AreEqual("\"192.168.1.1/24\"", CliCommandBuilder.QuoteForWhere("192.168.1.1/24"));
        }

        [TestMethod]
        public void QuoteForWhere_Empty_BecomesTheExplicitEmptyLiteral()
        {
            Assert.AreEqual("\"\"", CliCommandBuilder.QuoteForWhere(""));
            Assert.IsNull(CliCommandBuilder.QuoteForWhere(null));
        }

        // ── BuildWhereClause: '?name' (is set) vs '?name=' (equals empty) ───────

        [TestMethod]
        public void WhereClause_NullFilterValue_IsTheBareFieldName()
        {
            // The binary API's '?comment' — "the property is set". The CLI spells that as the bare name.
            Assert.AreEqual("comment", BuildWhere("comment", null));
        }

        [TestMethod]
        public void WhereClause_EmptyFilterValue_ComparesAgainstTheEmptyString()
        {
            // The binary API's '?comment=' — "the property equals the empty string". Both spellings used to
            // collapse to the bare name, which is the OPPOSITE query: measured on 7.23.2 against two
            // /system/script rows (one commented, one not), 'where comment' returns the COMMENTED row, while
            // '?comment=' over the API and 'where comment=""' over the CLI both return nothing.
            Assert.AreEqual("comment=\"\"", BuildWhere("comment", ""));
        }

        [TestMethod]
        public void WhereClause_NegatedEmptyFilterValue_IsStillQuoted()
        {
            Assert.AreEqual("comment!=\"\"", BuildWhere("comment", "!"));
        }

        private static string BuildWhere(string name, string value)
            => CliCommandBuilder.BuildWhereClause(new ITikCommandParameter[]
            {
                new tik4net.Connection.TikCommandParameter(name, value, TikCommandParameterFormat.Filter)
            });
    }
}
