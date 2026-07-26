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
