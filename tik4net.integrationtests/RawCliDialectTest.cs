// RawCliDialectTest.cs — the low-level API in the terminal transports' own language.
//
// CallCommandSync takes a command in the connection's format and sends it unchanged. On Api/ApiSsl that is
// API sentence words (CrudTest's *_With_LowLevel_API tests cover those); on the five CLI transports it is
// RouterOS CLI text, and this is what covers those.
//
// The point of the level is reach: it is the only way to run something the O/R mapper cannot express — a
// script line, an /export, a menu tik4net has no entity for. So the assertions below deliberately use
// shapes that no ITikCommand could produce.

using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;

namespace tik4net.integrationtests
{
    [TestClass]
    public class RawCliDialectTest : TestBase
    {
        [TestMethod]
        public void AsValueOutputComesBackAsRecordSentences()
        {
            EnsureRawDialectIsCliText("CallCommandSync with CLI text");

            // RouterOS prints nothing for a bare 'print as-value' typed at a terminal — as-value output is
            // materialised only in script context — so the caller wraps it. That the library does NOT wrap
            // it is the contract: what is typed is what is sent.
            var sentences = RawConnection.CallCommandSync(":put [/interface print as-value]").ToList();

            var rows = sentences.OfType<ITikReSentence>().ToList();
            Assert.IsTrue(rows.Count > 0, "the lab router has interfaces");
            Assert.IsTrue(rows.All(r => !string.IsNullOrEmpty(r.GetResponseFieldOrDefault("name", null))),
                "every interface record carries a name");
            Assert.AreEqual(1, sentences.OfType<ITikDoneSentence>().Count(),
                "a record read ends with !done, the same shape the binary API returns");
        }

        [TestMethod]
        public void RowsAreJoinedIntoOneCommandLine()
        {
            EnsureRawDialectIsCliText("CallCommandSync with CLI text");

            int whole = RawConnection.CallCommandSync(":put [/interface print as-value]")
                                  .OfType<ITikReSentence>().Count();
            int split = RawConnection.CallCommandSync(":put [/interface print", "as-value]")
                                  .OfType<ITikReSentence>().Count();

            Assert.AreEqual(whole, split,
                "several rows are one command line joined by a space — the caller may split it or not");
        }

        [TestMethod]
        public void OutputWithNoRecordStructureComesBackAsText()
        {
            EnsureRawDialectIsCliText("CallCommandSync with CLI text");

            string expected = Connection.CreateCommand("/system/identity/print").ExecuteScalar();

            var sentences = RawConnection.CallCommandSync(":put [/system identity get name]").ToList();

            Assert.AreEqual(0, sentences.OfType<ITikReSentence>().Count(),
                "a scalar :put has no record structure and must not be invented into rows");
            Assert.AreEqual(expected, sentences.OfType<ITikDoneSentence>().Single().GetResponseWord());
        }

        [TestMethod]
        public void ScriptingTheMapperCannotExpressIsReachable()
        {
            EnsureRawDialectIsCliText("CallCommandSync with CLI text");

            // A :foreach with a computed value — there is no ITikCommand or entity shape for this, which is
            // the whole reason the level exists.
            var sentences = RawConnection.CallCommandSync(
                ":put [:len [/interface find]]").ToList();

            string count = sentences.OfType<ITikDoneSentence>().Single().GetResponseWord();
            Assert.IsTrue(int.TryParse(count, out int n) && n > 0,
                $"expected a count of interfaces, got '{count}'");
        }

        [TestMethod]
        public void ARouterErrorIsRaisedAsATrap()
        {
            EnsureRawDialectIsCliText("CallCommandSync with CLI text");

            // Raw mode cannot know which verb this was, so the error check is the text-only one. What must
            // not happen is the error text being returned as a successful ret word. The concrete type is
            // whatever CliErrorParser classifies "bad command name prnt" as; what matters is that it is a
            // trap.
            var ex = Assert.ThrowsException<TikNoSuchCommandException>(
                () => RawConnection.CallCommandSync("/interface prnt").ToList());
            Assert.IsInstanceOfType(ex, typeof(TikCommandTrapException));
        }

        /// <summary>
        /// An API-looking path is <b>accepted</b> by the terminal — and still is not translated. Measured
        /// on 7.x: RouterOS's CLI takes <c>/interface/print</c> as happily as <c>/interface print</c>,
        /// because a slash is a valid path separator there too.
        /// </summary>
        /// <remarks>
        /// This is a migration trap worth pinning rather than a bug. Someone moving code down from
        /// <see cref="ITikCommand"/> will try their API path here, see rows come back, and conclude the
        /// library translated it — it did not. The difference shows in the SHAPE of the answer: without an
        /// <c>as-value</c> wrapper the router prints its human table, so what comes back is text rather
        /// than records. What the level actually promises is that the line is sent as typed; that the
        /// router happens to understand this particular line in both dialects is the router's doing.
        /// </remarks>
        [TestMethod]
        public void AnApiPathIsAcceptedByTheTerminalButStillNotTranslated()
        {
            EnsureRawDialectIsCliText("CallCommandSync with CLI text");

            var sentences = RawConnection.CallCommandSync("/interface/print").ToList();

            Assert.AreEqual(0, sentences.OfType<ITikReSentence>().Count(),
                "the router printed its human table, not as-value output — nothing here parses into records, "
                + "and inventing rows from a formatted table is exactly what this level must not do");

            string text = sentences.OfType<ITikDoneSentence>().Single().GetResponseWord();
            StringAssert.Contains(text, "ether1",
                "the answer is the terminal's own output, handed back whole");
        }
    }
}
