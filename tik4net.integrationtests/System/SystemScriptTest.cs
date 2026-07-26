using System;
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
        /// A script source must survive the round trip with its own punctuation intact (P2.17).
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
        /// treats as special inside its own quoted strings (P2.38).
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
        /// The backslash half of P2.38 — same helper, same mechanism, opposite failure mode.
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
        /// Writes <paramref name="source"/> to a throwaway script and asserts the router's stored
        /// copy is byte-for-byte what was sent. Shared by the two P2.38 tests above.
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
