// ProjectRootLayoutTests.cs — the root of tik4net/ is the public contract; internal types live in folders under the
// folder's namespace (ARCHITECTURE.md, "Layer 1"). A new internal helper dropped in the root fails here.

#nullable enable
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tik4net.unittests.Docs
{
    [TestClass]
    public class ProjectRootLayoutTests
    {
        private static readonly Regex PublicType = new Regex(
            @"^\s*public\s+(?:(?:static|sealed|abstract|partial|readonly)\s+)*(?:class|struct|enum|interface|delegate)\b",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex Namespace = new Regex(@"^namespace\s+([A-Za-z0-9_.]+)", RegexOptions.Multiline | RegexOptions.Compiled);

        private static string Core()
        {
            string? repo = WikiSampleFinder.FindRepositoryRoot();
            if (repo == null)
                Assert.Inconclusive("the repository root (tik4net.sln) was not found above the test assembly");
            return Path.Combine(repo, "tik4net");
        }

        [TestMethod]
        public void EveryFileInTheRoot_HoldsAPublicType_InTheRootNamespace()
        {
            string core = Core();
            var offenders = Directory.EnumerateFiles(core, "*.cs", SearchOption.TopDirectoryOnly)
                .Where(f =>
                {
                    string text = File.ReadAllText(f);
                    return !PublicType.IsMatch(text) || Namespace.Match(text).Groups[1].Value != "tik4net";
                })
                .Select(Path.GetFileName)
                .ToList();

            Assert.AreEqual(0, offenders.Count,
                "The root of tik4net/ holds the public contract only. Move an internal type to the folder of its kind "
                + "(Connection/, Helpers/, WireTextFormats/, a transport's folder) and give it that folder's namespace:\n"
                + string.Join("\n", offenders));
        }

        [DataTestMethod]
        [DataRow("Helpers", "tik4net.Helpers")]
        [DataRow("WireTextFormats", "tik4net.WireTextFormats")]
        public void AnInternalHelperFolder_UsesItsOwnNamespace(string folder, string expected)
        {
            var offenders = Directory.EnumerateFiles(Path.Combine(Core(), folder), "*.cs", SearchOption.AllDirectories)
                .Where(f => !PublicType.IsMatch(File.ReadAllText(f))
                            && Namespace.Match(File.ReadAllText(f)).Groups[1].Value != expected)
                .Select(Path.GetFileName)
                .ToList();

            Assert.AreEqual(0, offenders.Count, "internal types in " + folder + "/ take namespace " + expected + ":\n"
                + string.Join("\n", offenders));
        }
    }
}
