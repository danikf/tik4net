#nullable enable
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Connection;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// How a command path is sliced. <see cref="TikPath"/> is the one home for it; each row pins what every former copy
    /// of the slicing gives for the same input, so a difference between them is visible as a row, not as a silent change.
    /// </summary>
    [TestClass]
    public class TikPathTests
    {
        [DataTestMethod]
        [DataRow("", "")]
        [DataRow("/", "")]
        [DataRow("ip/route", "/ip/route")]
        [DataRow("/ip/route/", "/ip/route")]
        [DataRow(" /ip/route ", "/ip/route")]
        [DataRow("//ip//route//", "//ip//route")]
        public void Normalize(string path, string expected)
        {
            Assert.AreEqual(expected, TikPath.Normalize(path));
        }

        [DataTestMethod]
        [DataRow("", "")]
        [DataRow("/", "")]
        [DataRow("ip/route", "/ip")]
        [DataRow("/ip/route/", "/ip")]
        [DataRow(" /ip/route ", "/ip")]
        [DataRow("//ip//route//", "//ip/")]
        [DataRow("/ip", "/ip")]
        public void Parent(string path, string expected)
        {
            Assert.AreEqual(expected, TikPath.Parent(path));
        }

        [DataTestMethod]
        [DataRow("", "print")]
        [DataRow("/", "print")]                 // was "": the root has no verb, as a blank path has none
        [DataRow("ip/route", "route")]
        [DataRow("/ip/route/", "route")]        // was "": Parent already took "route" for the verb here
        [DataRow(" /ip/route ", "route")]
        [DataRow("//ip//route//", "route")]     // was ""
        [DataRow("/IP/Route/Print", "print")]
        public void Verb(string path, string expected)
        {
            Assert.AreEqual(expected, TikPath.Verb(path));
        }

        [DataTestMethod]
        [DataRow("", "")]
        [DataRow("/", "")]
        [DataRow("ip/route", "ip|route")]
        [DataRow("/ip/route/", "ip|route")]
        [DataRow(" /ip/route ", "ip|route")]   // was " |ip|route ": the menu-schema copy did not trim whitespace
        [DataRow("//ip//route//", "ip||route")]
        public void Segments(string path, string expected)
        {
            Assert.AreEqual(expected, string.Join("|", TikPath.Segments(path)));
        }

        // The menu-schema path: a blank path is the root menu, and every slash at either end goes.
        [DataTestMethod]
        [DataRow("", "/")]
        [DataRow("/", "/")]
        [DataRow("ip/route", "/ip/route")]
        [DataRow("/ip/route/", "/ip/route")]
        [DataRow(" /ip/route ", "/ip/route")]
        [DataRow("//ip//route//", "/ip//route")]
        public void MenuSchemaPath(string path, string expected)
        {
            Assert.AreEqual(expected, TikMenuSchemaExtensions.SchemaPath(path));
        }

        // The CLI menu of a command: its parent, in CLI spelling.
        [DataTestMethod]
        [DataRow("", "")]
        [DataRow("/", "")]
        [DataRow("ip/route/print", "/ip route")]
        [DataRow("/ip/route/print/", "/ip route")]
        [DataRow(" /ip/route ", "/ip")]        // was "/  ip": the copy kept the whitespace TikPath.Parent trims
        [DataRow("//ip//route//", "/ip ")]
        [DataRow("/ip", "/ip")]
        public void MenuPathToCli(string path, string expected)
        {
            Assert.AreEqual(expected, CliCommandBuilder.MenuPathToCli(path));
        }
    }
}
