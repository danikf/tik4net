using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.System;

namespace tik4net.integrationtests
{
    [TestClass]
    public class SystemScriptTest : TestBase
    {
        // 1) List — LoadAll must not throw and must return a (possibly empty) list.
        [TestMethod]
        public void ListSystemScriptsWillNotFail()
        {
            EnsureCommandAvailable("/system/script");
            var list = Connection.LoadAll<SystemScript>();
            Assert.IsNotNull(list);
        }

        /// <summary>
        /// A whole-table read must survive a script whose source spans lines — the usual shape of a script. RouterOS
        /// writes the line breaks raw into the CLI's per-row DSV, in the row's last field, and the parser took each
        /// continuation line for the next row's field names: one script, two records, the read refused. A lab router
        /// holds no scripts, so the suite saw it only when another leg's script was there at the same moment.
        /// </summary>
        [TestMethod]
        public void AWholeTableReadSurvivesAMultiLineSource()
        {
            EnsureCommandAvailable("/system/script");
            string name = TestNames.Unique("t4n-multiline");
            var entity = new SystemScript
            {
                Name = name,
                Source = ":local a 1\n:local b \"x=y\"\n:put $a\n",
            };
            SaveTracked(entity);
            try
            {
                var rows = Connection.CreateCommand("/system/script/print").ExecuteList();
                Assert.AreEqual(1, rows.Count(r => r.GetResponseField("name") == name),
                    "the script was not read back as exactly one row");
                Assert.AreEqual(1, Connection.LoadAll<SystemScript>().Count(s => s.Name.Value == name));
            }
            finally
            {
                Connection.Delete(entity);
            }
        }

        /// <summary>
        /// A script source must survive the round trip with its own punctuation intact.
        /// <para>
        /// <c>AddSystemScriptWillNotFail</c> below asserts only the name, and its source
        /// (<c>:log info test</c>) happens to contain neither ';' nor '=' — so it never saw this. RouterOS
        /// script source normally contains both, and the CLI's as-value output escapes nothing: measured
        /// on 7.23.2, the source asserted here came back over as-value truncated at its first semicolon,
        /// with the remainder parsed as further fields.
        /// </para>
        /// <para>
        /// The source deliberately contains NO <c>$</c>: a separate, write-side defect makes the CLI
        /// interpolate <c>$name</c> inside the quoted value, so <c>:put ($a . $b)</c> is stored as
        /// <c>:put ( . )</c>. That is a different bug on a different path (quoting in
        /// <c>CliCommandBuilder</c>, not parsing) and has its own plan item — mixing it in here would make
        /// this test fail for a reason it is not about.
        /// </para>
        /// </summary>
        [TestMethod]
        public void ScriptSource_SurvivesItsOwnSeparators()
        {
            EnsureCommandAvailable("/system/script");
            const string source = ":local a 1;\n:local b \"x=y;z\"\n:log info \"done; ok=1\"\n";

            var entity = new SystemScript
            {
                Name = "t4n" + Guid.NewGuid().ToString("N").Substring(0, 12),
                Source = source,
            };
            SaveTracked(entity);
            try
            {
                var loaded = Connection.LoadById<SystemScript>(entity.Id);

                Assert.IsNotNull(loaded);
                Assert.AreEqual(source, loaded.Source,
                    "The script source did not survive the round trip — a value containing ';' or '=' is "
                    + "being cut or re-parsed as further fields (P2.17).");
            }
            finally
            {
                Connection.Delete(entity);
            }
        }

        /// <summary>
        /// A written value must reach the router as it was given, including the characters RouterOS
        /// treats as special inside its own quoted strings.
        /// <para>
        /// This is the WRITE-side mirror of <see cref="ScriptSource_SurvivesItsOwnSeparators"/>. The
        /// CLI transports quote a value in double quotes, and in a RouterOS double-quoted string
        /// <c>$</c> starts variable substitution and <c>\</c> starts an escape — so before the fix
        /// <c>:put ($a . $b)</c> was stored by the router as <c>:put ( . )</c> and
        /// <c>C:\temp\new</c> as <c>C:&lt;TAB&gt;emp&lt;LF&gt;ew</c>. It failed silently and
        /// permanently: the add succeeded, the <c>.id</c> came back, nothing trapped, and the
        /// corruption is in the router's own copy — so re-reading it over ANY transport agrees on the
        /// wrong value. That is why this asserts the round trip rather than "did not throw".
        /// </para>
        /// <para>
        /// Undefined variables are used on purpose: an undefined one substitutes to empty, so the
        /// corruption is total and unambiguous. Backslashes are deliberately kept OUT of this test
        /// and covered separately below — an unescaped one is a hard syntax error, and a test that
        /// throws before it can compare would mask a regression of the silent half.
        /// </para>
        /// </summary>
        [TestMethod]
        public void ScriptSource_SurvivesVariableReferences()
        {
            EnsureCommandAvailable("/system/script");
            const string source = ":local a 1;\n:local b 2\n:put ($a . $b)\n";

            AssertScriptSourceRoundTrips(source,
                "The script source stored by the router differs from what was sent — '$' is being "
                + "read as variable substitution by the RouterOS CLI parser instead of quoted through, "
                + "so the variables were replaced by empty strings before the router ever stored the "
                + "text (P2.38). Nothing traps and the damage is in the router's own copy, so every "
                + "transport reads it back identically wrong.");
        }

        /// <summary>
        /// The backslash half of the quoting round trip — same helper, same mechanism, opposite failure mode.
        /// <para>
        /// Inside a RouterOS quoted value <c>\</c> starts an escape sequence: an escape the router
        /// knows is applied silently (<c>C:\temp\new</c> is stored as <c>C:&lt;TAB&gt;emp&lt;LF&gt;ew</c>)
        /// and one it does not know (<c>\d</c>) is a hard syntax error. Both are covered here — the
        /// value carries a known escape and an unknown one — so this fails whichever way the router
        /// leans.
        /// </para>
        /// </summary>
        [TestMethod]
        public void ScriptSource_SurvivesBackslashes()
        {
            EnsureCommandAvailable("/system/script");
            const string source = ":local p \"C:\\temp\\dir\"\n:put $p\n";

            AssertScriptSourceRoundTrips(source,
                "The script source stored by the router differs from what was sent — '\\' is being "
                + "read as an escape sequence by the RouterOS CLI parser instead of quoted through "
                + "(P2.38).");
        }

        /// <summary>
        /// Control characters and characters whose UTF-8 form holds a C1 byte reach the router as written (security
        /// review X3). Over a CLI transport the value is TYPED: a raw Ctrl-C dropped the line, and the 0x9B in UTF-8
        /// <c>ě</c> (C4 9B) and <c>Û</c> (C3 9B) was a CSI to the line editor. The stored copy is read back over the
        /// binary API, so this asserts the write; the leg's own read is asserted after it, separately. On that read
        /// U+009B is the UTF-8 C2 9B of the text, which a CLI read keeps although the editor takes the byte for a CSI;
        /// the letter after it is the one an 8-bit CSI would end on.
        /// </summary>
        [TestMethod]
        public void ScriptSource_SurvivesControlCharactersAndNonAscii()
        {
            EnsureCommandAvailable("/system/script");
            const string source = "# a\u0003:put T4NINJ ěÛ \u009bb \u007f \u0001\n";
            var entity = new SystemScript { Name = "t4n" + Guid.NewGuid().ToString("N").Substring(0, 12), Source = source };
            SaveTracked(entity);
            try
            {
                string stored;
                using (var api = LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api))
                    stored = api.LoadById<SystemScript>(entity.Id)?.Source.Value;
                Assert.AreEqual(Escape(source), Escape(stored ?? "<no row>"), "the router stored something else");

                Assert.AreEqual(Escape(source), Escape(Connection.LoadById<SystemScript>(entity.Id)?.Source.Value ?? "<no row>"),
                    "stored correctly, but this transport reads it back differently");
            }
            finally
            {
                Connection.Delete(entity);
            }
        }

        /// <summary>
        /// A filter value cannot break out of its quoted string on a CLI transport (security review X3). Typed raw, the
        /// Ctrl-C dropped the line typed so far and the router parsed <c>:put T4NINJ"]…</c> as a new command, answering
        /// "expected end of command"; <c>ě</c> (UTF-8 C4 9B) and U+009B carry the byte 0x9B, a CSI to the line editor,
        /// which ate the characters after it (7.25.1 and 6.49.21). On every transport this is an ordinary filter that
        /// matches nothing.
        /// </summary>
        [TestMethod]
        [SafeInParallelLegs]
        public void AFilterValueHoldingControlBytes_MatchesNothing_AndRunsNothing()
        {
            EnsureCommandAvailable("/system/script");
            foreach (string value in new[] { "x\u0003:put T4NINJ", "xě:put T4NINJ", "x\u009b:put T4NINJ", "x\u001b[2J\u007f" })
            {
                var cmd = Connection.CreateCommand("/system/script/print");
                cmd.AddParameter("name", value, TikCommandParameterFormat.Filter);

                Assert.AreEqual(0, cmd.ExecuteList().Count(), "a script named " + Escape(value));
            }
        }

        private static string Escape(string s)
            => string.Concat(s.Select(c => c < 0x20 || c >= 0x7F ? "\\u" + ((int)c).ToString("x4") : c.ToString()));

        /// <summary>
        /// Writes <paramref name="source"/> to a throwaway script and asserts the router's stored
        /// copy is byte-for-byte what was sent. Shared by the two quoting tests above.
        /// </summary>
        private void AssertScriptSourceRoundTrips(string source, string message)
        {
            var entity = new SystemScript
            {
                Name = "t4n" + Guid.NewGuid().ToString("N").Substring(0, 12),
                Source = source,
            };
            SaveTracked(entity);
            try
            {
                var loaded = Connection.LoadById<SystemScript>(entity.Id);

                Assert.IsNotNull(loaded);
                Assert.AreEqual(source, loaded.Source, message);
            }
            finally
            {
                Connection.Delete(entity);
            }
        }

        // 2) Add — create, reload by id, assert name, then delete (always clean up).
        [TestMethod]
        public void AddSystemScriptWillNotFail()
        {
            EnsureCommandAvailable("/system/script");
            string marker = Guid.NewGuid().ToString();
            var entity = new SystemScript
            {
                Name = marker,
                Source = ":log info test",
            };
            SaveTracked(entity);

            var loaded = Connection.LoadById<SystemScript>(entity.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Name);

            Connection.Delete(loaded);
        }
    }
}
