using System;
using System.Configuration;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.System;

namespace tik4net.integrationtests
{
    [TestClass]
    public class FileTest : TestBase
    {
        // 1) List — LoadAll must not throw and must return a (possibly empty) list.
        [TestMethod]
        public void ListFilesWillNotFail()
        {
            EnsureCommandAvailable("/file");
            var list = Connection.LoadAll<File>();
            Assert.IsNotNull(list);
        }

        /// <summary>
        /// The row count must match what the binary API reports for the same menu (P2.17).
        /// <para>
        /// IsNotNull was not enough to catch the defect this test exists for: /file carries
        /// <c>contents</c>, and a file body is full of ';', '=' and newlines — the separators of the
        /// unescaped as-value format the CLI transports read. One HTML or JS file therefore swallowed the
        /// rest of the listing, and the measurement was 27 rows over the API against 1 over both winboxcli
        /// and telnet. It did not fail: it returned a plausible, much shorter list. Comparing against a
        /// transport that frames its values is the only assertion that can see that.
        /// </para>
        /// <para>
        /// The reference connection is deliberately a fixed binary-API one rather than
        /// <see cref="TestBase.OpenSecondaryConnection"/>, so that on an API run the two sides do not
        /// become the same code path asserting against itself.
        /// </para>
        /// </summary>
        [TestMethod]
        public void ListFiles_RowCountMatchesTheBinaryApi()
        {
            EnsureCommandAvailable("/file");

            var viaTransport = Connection.LoadAll<File>();

            string host = ConfigurationManager.AppSettings["host"];
            string user = ConfigurationManager.AppSettings["user"];
            string pass = ConfigurationManager.AppSettings["pass"] ?? "";

            using (var apiConnection = ConnectionFactory.CreateConnection(TikConnectionType.Api))
            {
                apiConnection.Open(host, user, pass);
                var viaApi = apiConnection.LoadAll<File>();

                Assert.AreEqual(viaApi.Count(), viaTransport.Count(),
                    "The transport under test returned a different number of /file rows than the binary "
                    + "API. A short list here means values are being lost in parsing, not that files "
                    + "disappeared — see P2.17.");

                // Names, not just the count: a mis-split can preserve the row count while corrupting the
                // field it split on.
                CollectionAssert.AreEquivalent(
                    viaApi.Select(f => f.Name).ToList(),
                    viaTransport.Select(f => f.Name).ToList(),
                    "The transport under test returned different /file names than the binary API.");
            }
        }

        /// <summary>
        /// A file body must survive the round trip with its separator characters intact — the specific
        /// thing as-value cannot express. Skipped when the router holds no readable text file (a bare
        /// router has none; ours has the user-manager um5files tree).
        /// </summary>
        [TestMethod]
        public void LoadFileContents_KeepsSeparatorCharacters()
        {
            EnsureCommandAvailable("/file");

            var textFile = Connection.LoadAll<File>()
                .FirstOrDefault(f => !string.IsNullOrEmpty(f.Contents)
                                     && (f.Contents.Contains(";") || f.Contents.Contains("=")));

            if (textFile == null)
                Assert.Inconclusive("No file on this router exposes a text body containing ';' or '=' — "
                    + "nothing to assert the escaping against.");

            Console.WriteLine($"contents of '{textFile.Name}' ({textFile.Contents.Length} chars)");

            string host = ConfigurationManager.AppSettings["host"];
            string user = ConfigurationManager.AppSettings["user"];
            string pass = ConfigurationManager.AppSettings["pass"] ?? "";

            using (var apiConnection = ConnectionFactory.CreateConnection(TikConnectionType.Api))
            {
                apiConnection.Open(host, user, pass);
                var viaApi = apiConnection.LoadAll<File>().Single(f => f.Name == textFile.Name);

                Assert.AreEqual(viaApi.Contents, textFile.Contents,
                    "The file body read over the transport under test differs from the binary API's copy.");
            }
        }
    }
}
