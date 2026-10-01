using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    /// <summary>
    /// How <see cref="WinboxJgCatalog.Assemble"/> treats a plugin that arrives but does not parse. The plugin cache
    /// is content-addressed and shared by every process on the machine, so a truncated plugin stored there would be
    /// every later connection's copy of it — and a catalog missing it (roteros.jg holds /interface) must never be
    /// shared as the whole one.
    /// </summary>
    [TestClass]
    public class WinboxJgCatalogAssemblyTests
    {
        private const string WolWindow =
            "[{name:'WoL',title:'WoL',group:'Tools',c:[{title:'Wake on LAN',type:'doit',path:[ 82 ],cmd:1,c:[" +
            "{name:'MAC Address',type:'macaddr',id:'r1'}]}]}]";

        // What a read that ended early leaves: the start of a plugin, cut in the middle of an object.
        private const string Truncated = "[{name:'WoL',title:'WoL',group:'Tools',c:[{title:'Wake on LAN',type:'do";

        private static WinboxJgCatalog.PluginEntry Plugin(string name)
            => new WinboxJgCatalog.PluginEntry { Name = name + ".jg", Unique = name + "-0123456789ab.jg" };

        private sealed class FakeCache
        {
            public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
            public readonly Dictionary<string, string> Served = new Dictionary<string, string>();
            public readonly List<string> Stored = new List<string>();
            public readonly List<string> Evicted = new List<string>();

            public WinboxJgCatalog Assemble(out bool complete, params WinboxJgCatalog.PluginEntry[] plugins)
                => WinboxJgCatalog.Assemble(plugins,
                    p => Files.TryGetValue(p.Unique, out var text) ? text : null,
                    p => Served.TryGetValue(p.Unique, out var text) ? text : null,
                    (p, text) => { Stored.Add(p.Unique); Files[p.Unique] = text; },
                    p => { Evicted.Add(p.Unique); Files.Remove(p.Unique); },
                    out complete);
        }

        [TestMethod]
        public void AFetchedPluginThatParsesIsCachedAndTheCatalogIsComplete()
        {
            var cache = new FakeCache();
            var wol = Plugin("wol");
            cache.Served[wol.Unique] = WolWindow;

            cache.Assemble(out bool complete, wol);

            Assert.IsTrue(complete);
            CollectionAssert.AreEqual(new[] { wol.Unique }, cache.Stored);
        }

        [TestMethod]
        public void ATruncatedDownloadIsNeitherCachedNorPassedOffAsComplete()
        {
            var cache = new FakeCache();
            var roteros = Plugin("roteros");
            var wol = Plugin("wol");
            cache.Served[roteros.Unique] = Truncated;
            cache.Served[wol.Unique] = WolWindow;

            cache.Assemble(out bool complete, roteros, wol);

            Assert.IsFalse(complete, "a catalog missing a plugin must not be shared as the whole one");
            CollectionAssert.DoesNotContain(cache.Stored, roteros.Unique,
                "a plugin that did not parse was stored — every later connection would read it back");
            CollectionAssert.Contains(cache.Stored, wol.Unique, "the channel was fine, so the other plugins still load");
        }

        [TestMethod]
        public void ACachedPluginThatDoesNotParseIsEvictedSoTheNextConnectionFetchesIt()
        {
            var cache = new FakeCache();
            var roteros = Plugin("roteros");
            cache.Files[roteros.Unique] = Truncated;
            cache.Served[roteros.Unique] = WolWindow;

            cache.Assemble(out bool first, roteros);
            Assert.IsFalse(first);
            CollectionAssert.AreEqual(new[] { roteros.Unique }, cache.Evicted);

            cache.Assemble(out bool second, roteros);
            Assert.IsTrue(second, "with the bad copy gone, the next connection fetches the plugin and completes");
            CollectionAssert.AreEqual(new[] { roteros.Unique }, cache.Stored);
        }

        [TestMethod]
        public void APluginThatCannotBeHadStopsTheLoad()
        {
            var cache = new FakeCache();
            var roteros = Plugin("roteros");
            var wol = Plugin("wol");
            cache.Served[wol.Unique] = WolWindow;   // roteros is served by nobody

            cache.Assemble(out bool complete, roteros, wol);

            Assert.IsFalse(complete);
            Assert.AreEqual(0, cache.Stored.Count, "the load stops at the first plugin it cannot get (P2.20)");
        }
    }
}
