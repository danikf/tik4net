// CultureInvariantParseTests.cs — the shipping libraries parse integers with an explicit culture.
//
// CA1305 (on for the shipping projects, see shipping.globalconfig) holds every Format, ToString and Parse to an
// explicit IFormatProvider, but it does not see int.TryParse(s, out n): the provider overload puts the provider
// before the out parameter, and the rule only looks for one appended. That two-argument TryParse reads with the
// host's culture, so it is the gap this closes. It runs here, in CI, on every push.

#nullable enable
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tik4net.unittests.Docs
{
    [TestClass]
    public class CultureInvariantParseTests
    {
        private static readonly string[] ShippingProjects = { "tik4net", "tik4net.objects", "tik4net.ssh", "tik4net.testing" };

        // T.TryParse(<one argument>, out …): the argument may hold calls (value.Substring(2)) but no top-level comma.
        private static readonly Regex TwoArgumentTryParse = new Regex(
            @"\b(?:byte|sbyte|short|ushort|int|uint|long|ulong|float|double|decimal|Byte|SByte|Int16|UInt16|Int32|UInt32|Int64|UInt64|Single|Double|Decimal|DateTime|DateTimeOffset|TimeSpan)"
            + @"\.TryParse\(\s*[^,;()]*(?:\([^()]*\)[^,;()]*)*,\s*out\b",
            RegexOptions.Compiled);

        [TestMethod]
        public void NoNumberIsParsedWithTheHostCulture()
        {
            string? repo = WikiSampleFinder.FindRepositoryRoot();
            if (repo == null)
                Assert.Inconclusive("the repository root (tik4net.sln) was not found above the test assembly");

            var offenders = ShippingProjects
                .Select(p => Path.Combine(repo, p))
                .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                            && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                .SelectMany(f =>
                {
                    string text = File.ReadAllText(f);
                    return TwoArgumentTryParse.Matches(text).Cast<Match>().Select(m =>
                        $"{f.Substring(repo.Length).TrimStart(Path.DirectorySeparatorChar)}:"
                        + $"{text.Take(m.Index).Count(c => c == '\n') + 1}: {m.Value}");
                })
                .ToList();

            Assert.AreEqual(0, offenders.Count,
                "Parse with an explicit culture, e.g. long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n):\n"
                + string.Join("\n", offenders));
        }
    }
}
