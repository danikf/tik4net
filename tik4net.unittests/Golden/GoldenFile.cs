#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.unittests.Docs;

namespace tik4net.unittests.Golden
{
    /// <summary>
    /// Golden files: a test renders what the code produces as text lines, and the tracked copy under
    /// <c>tik4net.unittests/Golden/</c> holds what it produced when the file was last written.
    /// </summary>
    /// <remarks>
    /// The pins of the WinBox + MAC layer review (T1–T5): every builder, parser, handshake and table that the
    /// consolidation rewrites is rendered here before it moves, so a commit that claims "no behaviour change" is
    /// checked byte for byte rather than by reading the diff. A mismatch names the first differing line.
    /// <para>Set <c>TIK4NET_UPDATE_GOLDEN=1</c> to rewrite the tracked files — only when the change is meant to change
    /// the bytes, and then the diff of the golden file is the review of that change.</para>
    /// </remarks>
    internal static class GoldenFile
    {
        internal static void AssertMatches(string name, IEnumerable<string> lines)
        {
            string actual = string.Join("\n", lines) + "\n";
            string? repo = WikiSampleFinder.FindRepositoryRoot();
            if (repo == null)
                Assert.Inconclusive("tik4net.sln was not found above the test assembly, so the golden files are not reachable.");
            string path = Path.Combine(repo!, "tik4net.unittests", "Golden", name);

            if (Environment.GetEnvironmentVariable("TIK4NET_UPDATE_GOLDEN") == "1")
            {
                File.WriteAllText(path, actual);
                Assert.Inconclusive("TIK4NET_UPDATE_GOLDEN=1 - rewrote " + name + ". Re-run without it.");
            }

            Assert.IsTrue(File.Exists(path), "No golden file " + name + "; run once with TIK4NET_UPDATE_GOLDEN=1 to write it.");
            string[] expected = File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            string[] got = actual.TrimEnd('\n').Split('\n');
            for (int i = 0; i < Math.Max(expected.Length, got.Length); i++)
            {
                string e = i < expected.Length ? expected[i] : "(end of file)";
                string g = i < got.Length ? got[i] : "(end of output)";
                if (e != g)
                    Assert.Fail("Golden file " + name + " differs at line " + (i + 1) + ":\n  expected: " + e
                        + "\n  actual:   " + g);
            }
        }

        internal static string Hex(byte[]? bytes)
            => bytes == null ? "null" : bytes.Length == 0 ? "-" : BitConverter.ToString(bytes).Replace("-", "");

        internal static byte[] FromHex(string hex)
            => Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(2 * i, 2), 16)).ToArray();
    }
}
