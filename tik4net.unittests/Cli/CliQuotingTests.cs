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

        [TestMethod]
        public void QuoteIfNeeded_PlainValue_IsNotQuoted()
        {
            Assert.AreEqual("ether1", CliCommandBuilder.QuoteIfNeeded("ether1"));
            Assert.AreEqual("192.168.1.1/24", CliCommandBuilder.QuoteIfNeeded("192.168.1.1/24"));
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
        public void QuoteIfNeeded_EmptyAndNull_PassThrough()
        {
            Assert.AreEqual("", CliCommandBuilder.QuoteIfNeeded(""));
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
    }
}
