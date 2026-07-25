using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    /// <summary>
    /// Covers the mproxy <c>list</c> catalog parse that resolves which <c>.jg</c> plugins a router serves.
    /// The set is version- and package-dependent, so it must come from the router; and because the names
    /// it reports become local cache filenames, they must be validated rather than trusted.
    /// </summary>
    [TestClass]
    public class WinboxJgPluginListTests
    {
        // Shape of the real catalog served by RouterOS 7.23.2 (trimmed).
        private const string RealList =
            "{ crc: 3100105613, size: 73929, name: \"wave2.jg\", unique: \"wave2-61719aa0f1dc.jg\", version: \"7.23.2\" }" +
            "{ crc: 12, size: 27574, name: \"dhcp.jg\", unique: \"dhcp-18bd695dd56c.jg\", version: \"7.23.2\" }" +
            "{ crc: 13, size: 25219, name: \"icons.png\", unique: \"\", version: \"7.23.2\" }" +
            "{ crc: 14, size: 933431, name: \"roteros.jg\", unique: \"roteros-33a7039ff432.jg\", version: \"7.23.2\" }";

        [TestMethod]
        public void ParsePluginList_ResolvesJgPluginsWithTheirOnDiskNames()
        {
            var plugins = WinboxJgCatalog.ParsePluginList(RealList);

            CollectionAssert.AreEquivalent(
                new[] { "wave2.jg", "dhcp.jg", "roteros.jg" },
                plugins.Select(p => p.Name).ToArray(),
                "only .jg entries carrying a 'unique' are fetchable plugins");
            Assert.AreEqual("roteros-33a7039ff432.jg",
                plugins.Single(p => p.Name == "roteros.jg").Unique,
                "the version-stamped 'unique' is the name the plugin is actually downloaded by");
        }

        [TestMethod]
        public void ParsePluginList_SkipsIconsWithoutUnique()
        {
            Assert.IsFalse(WinboxJgCatalog.ParsePluginList(RealList).Any(p => p.Name == "icons.png"));
        }

        // The router supplies these names and they are used as cache filenames, so anything that could
        // escape the cache directory must be dropped rather than sanitized into something plausible.
        [TestMethod]
        public void ParsePluginList_RejectsNamesThatCouldEscapeTheCacheDirectory()
        {
            string hostile =
                "{ name: \"../../evil.jg\", unique: \"../../evil.jg\" }" +
                "{ name: \"ok.jg\", unique: \"../../../etc/passwd.jg\" }" +
                "{ name: \"ok2.jg\", unique: \"sub/dir.jg\" }" +
                "{ name: \"ok3.jg\", unique: \"C:\\\\windows\\\\evil.jg\" }" +
                "{ name: \"ok4.jg\", unique: \"..jg\" }" +
                "{ name: \"good.jg\", unique: \"good-01ab.jg\" }";

            var plugins = WinboxJgCatalog.ParsePluginList(hostile);

            CollectionAssert.AreEqual(new[] { "good-01ab.jg" }, plugins.Select(p => p.Unique).ToArray(),
                "only the plain filename should survive");
        }

        [TestMethod]
        public void ParsePluginList_ToleratesGarbage()
        {
            Assert.AreEqual(0, WinboxJgCatalog.ParsePluginList(null).Count);
            Assert.AreEqual(0, WinboxJgCatalog.ParsePluginList("").Count);
            Assert.AreEqual(0, WinboxJgCatalog.ParsePluginList("not a catalog at all").Count);
        }

        // A resolved set is remembered per router so that a momentarily unavailable `list` reuses it rather
        // than leaving the connection on the seed table, where missing counters look like zeros (P2.23).
        [TestMethod]
        public void RememberedList_RoundTripsThePluginSet()
        {
            var original = WinboxJgCatalog.ParsePluginList(RealList);

            var restored = WinboxJgCatalog.ParseRememberedList(WinboxJgCatalog.FormatPluginList(original));

            CollectionAssert.AreEqual(original.Select(p => p.Name + "|" + p.Unique).ToArray(),
                                      restored.Select(p => p.Name + "|" + p.Unique).ToArray());
        }

        // The remembered set lives on disk between runs, and its names go straight back out as mproxy
        // filenames and cache filenames — so it is re-validated on read, not trusted because we wrote it.
        [TestMethod]
        public void RememberedList_RevalidatesNamesOnRead()
        {
            var restored = WinboxJgCatalog.ParseRememberedList(
                "../../evil.jg\t../../evil.jg\n" +
                "ok.jg\tsub/dir.jg\n" +
                "good.jg\tgood-01ab.jg\n");

            CollectionAssert.AreEqual(new[] { "good-01ab.jg" }, restored.Select(p => p.Unique).ToArray());
        }

        [TestMethod]
        public void RememberedList_ToleratesGarbage()
        {
            Assert.AreEqual(0, WinboxJgCatalog.ParseRememberedList(null).Count);
            Assert.AreEqual(0, WinboxJgCatalog.ParseRememberedList("").Count);
            Assert.AreEqual(0, WinboxJgCatalog.ParseRememberedList("no tab here\n\n   \n").Count);
        }
    }
}
