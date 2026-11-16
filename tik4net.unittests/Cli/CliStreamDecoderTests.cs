// CliStreamDecoderTests.cs — a terminal's bytes are decoded as one stream: a multi-byte character split across two
// received chunks reads as itself, not as two U+FFFD. WinBox CLI read a file's U+01F2 (C7 B2) as two U+FFFD once in 25
// reads (7.25.1), which is what decoding each chunk on its own does wherever a frame boundary falls in the text.

#nullable enable
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.unittests.Docs;

namespace tik4net.unittests.Cli
{
    [TestClass]
    public class CliStreamDecoderTests
    {
        [TestMethod]
        public void ACharacterSplitAcrossTwoChunks_ReadsWhole()
        {
            byte[] all = Encoding.UTF8.GetBytes("abǲcdčřx");   // ǲ, č, ř: two bytes each
            for (int cut = 1; cut < all.Length; cut++)
            {
                var decoder = new CliStreamDecoder(Encoding.UTF8);
                string text = decoder.Decode(all.Take(cut).ToArray()) + decoder.Decode(all.Skip(cut).ToArray());
                Assert.AreEqual("abǲcdčřx", text, "cut at byte " + cut);
            }
        }

        [TestMethod]
        public void TheOldWay_IsWhatGoesWrong()
        {
            // The defect this replaces, pinned so the test above cannot pass for the wrong reason.
            byte[] all = Encoding.UTF8.GetBytes("ǲ");
            string perChunk = Encoding.UTF8.GetString(all, 0, 1) + Encoding.UTF8.GetString(all, 1, 1);
            Assert.AreEqual("��", perChunk);
        }

        [TestMethod]
        public void NoTerminalClient_DecodesAChunkOnItsOwn()
        {
            string? repo = WikiSampleFinder.FindRepositoryRoot();
            if (repo == null)
                Assert.Inconclusive("the repository root (tik4net.sln) was not found above the test assembly");

            var perChunk = new Regex(@"_encoding\.GetString\(\s*(chunk|payload)\b");
            var offenders = new[] { "tik4net/WinboxCli", "tik4net/MacTelnet", "tik4net/Cli", "tik4net/Telnet", "tik4net.ssh" }
                .Select(d => Path.Combine(repo, d))
                .Where(Directory.Exists)
                .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                .Where(f => perChunk.IsMatch(File.ReadAllText(f)))
                .Select(Path.GetFileName)
                .ToList();

            Assert.AreEqual(0, offenders.Count, "decode a received chunk with the stream's CliStreamDecoder:\n"
                + string.Join("\n", offenders));
        }
    }
}
