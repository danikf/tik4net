// IntegrationTestConfigReadTests.cs — the integration suite reads its settings through LabConfig, and only there.
//
// A run picks its router with TIK4NET_ROUTER (run-integration-tests.ps1 -Router). That works only while every test
// asks LabConfig.Get: one direct ConfigurationManager.AppSettings["host"] reads the DEFAULT router whatever the run
// selected, so half a CHR2 run would quietly talk to CHR. 272 such reads in 51 files were what made switching routers
// an App.config edit; this keeps them from coming back. It runs here, in CI, because the integration project does not.

#nullable enable
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tik4net.unittests.Docs
{
    [TestClass]
    public class IntegrationTestConfigReadTests
    {
        private static readonly Regex DirectRead = new Regex(@"\bAppSettings\s*[\[.]", RegexOptions.Compiled);

        [TestMethod]
        public void OnlyLabConfigReadsAppSettings()
        {
            string? repo = WikiSampleFinder.FindRepositoryRoot();
            if (repo == null)
                Assert.Inconclusive("the repository root (tik4net.sln) was not found above the test assembly");

            string suite = Path.Combine(repo, "tik4net.integrationtests");
            var offenders = Directory.EnumerateFiles(suite, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                            && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                            && Path.GetFileName(f) != "LabConfig.cs")
                .SelectMany(f => File.ReadAllLines(f)
                    .Select((line, i) => (file: f, line, number: i + 1)))
                .Where(x => DirectRead.IsMatch(x.line) && !x.line.TrimStart().StartsWith("//"))
                .Select(x => $"{x.file.Substring(suite.Length).TrimStart(Path.DirectorySeparatorChar)}:{x.number}: {x.line.Trim()}")
                .ToList();

            Assert.AreEqual(0, offenders.Count,
                "Read App.config through LabConfig.Get(key), so a run's router profile (TIK4NET_ROUTER) applies:\n"
                + string.Join("\n", offenders));
        }
    }
}
