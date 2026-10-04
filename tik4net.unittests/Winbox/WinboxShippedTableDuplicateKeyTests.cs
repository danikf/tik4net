// WinboxShippedTableDuplicateKeyTests.cs — no shipped WinBox table names one key twice.
//
// The shipped tables are collection initializers written with the indexer (["/path"] = …), and an indexer
// initializer does not throw on a repeated key: the later entry silently replaces the earlier one. That is how
// /routing/ospf/instance lost its `inactive` mapping — a second entry, added for its RouterOS 6 labels, overwrote
// the first, and 7.24.5 native read a disabled instance as `invalid`. Nothing at run time can see it, so the source is
// read: each `["key"] =` line is grouped by the `{` that opens its initializer.

#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.unittests.Docs;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxShippedTableDuplicateKeyTests
    {
        private static readonly Regex KeyLine = new Regex(@"^(\s*)\[""([^""]+)""\]\s*=", RegexOptions.Compiled);

        [TestMethod]
        public void NoInitializerRepeatsAKey()
        {
            string? repo = WikiSampleFinder.FindRepositoryRoot();
            if (repo == null)
                Assert.Inconclusive("the repository root (tik4net.sln) was not found above the test assembly");

            var duplicates = new List<string>();
            foreach (string file in Directory.EnumerateFiles(Path.Combine(repo, "tik4net", "Winbox"), "*.cs"))
            {
                string[] lines = File.ReadAllLines(file);
                var seen = new Dictionary<(int opener, string key), int>();
                for (int i = 0; i < lines.Length; i++)
                {
                    var m = KeyLine.Match(lines[i]);
                    if (!m.Success) continue;
                    int indent = m.Groups[1].Value.Length;
                    int opener = -1;
                    for (int j = i - 1; j >= 0; j--)
                        if (lines[j].Trim() == "{" && lines[j].Length - lines[j].TrimStart().Length < indent) { opener = j; break; }
                    var id = (opener, m.Groups[2].Value);
                    if (seen.TryGetValue(id, out int first))
                        duplicates.Add($"{Path.GetFileName(file)}:{i + 1}: [\"{id.Item2}\"] repeats line {first}");
                    else
                        seen[id] = i + 1;
                }
            }

            Assert.AreEqual(0, duplicates.Count,
                "An indexer initializer keeps only the LAST entry for a key — merge them into one:\n" + string.Join("\n", duplicates));
        }
    }
}
