// WikiSampleFinderTests.cs — the finder sees every C# block a reader sees, wherever markdown puts its fence.
//
// A fence under a list item is indented, and one inside a blockquote starts with "> ". The finder matched column 0
// only, so it skipped both kinds silently: ten blocks on the WinBox native page were never compiled, and one of them
// used a TikField<bool?> as a bool.

using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tik4net.unittests.Docs
{
    [TestClass]
    public class WikiSampleFinderTests
    {
        private static string[] Codes(string markdown)
        {
            string path = Path.Combine(Path.GetTempPath(), "t4n-finder-" + Guid.NewGuid().ToString("N") + ".md");
            File.WriteAllText(path, markdown);
            try
            {
                return WikiSampleFinder.ReadSamples(path).Select(s => s.Code).ToArray();
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void AColumnZeroFence_IsFound()
        {
            CollectionAssert.AreEqual(new[] { "var a = 1;" }, Codes("# t\n\n```cs\nvar a = 1;\n```\n"));
        }

        [TestMethod]
        public void AFenceUnderAListItem_IsFound_WithoutItsIndent()
        {
            string md = "* item\n\n  ```cs\n  var a = 1;\n    var b = 2;\n  ```\n";
            CollectionAssert.AreEqual(new[] { "var a = 1;" + Environment.NewLine + "  var b = 2;" }, Codes(md));
        }

        [TestMethod]
        public void AFenceInABlockquote_IsFound_WithoutItsMarker()
        {
            string md = "> note\n>\n> ```cs\n> var a = 1;\n> ```\n";
            CollectionAssert.AreEqual(new[] { "var a = 1;" }, Codes(md));
        }

        [TestMethod]
        public void AnIndentedFenceOfAnotherLanguage_IsNotACSharpSample()
        {
            Assert.AreEqual(0, Codes("* item\n\n  ```text\n  var a = 1;\n  ```\n").Length);
        }
    }
}
