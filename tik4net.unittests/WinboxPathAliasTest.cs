// WinboxPathAliasTest.cs — router-free tests for the *text* half of the WinBox native name→number bridge.
//
// The M2 wire only speaks numbers (handler [20,5], key 0xFF0001) and those numbers move between RouterOS
// versions. What a user can actually see and write down is the WinBox GUI menu label, so the session
// extension point is PathAlias(apiPath → menu-label path): the text is pinned, the handler behind it is
// still read live from the router's .jg catalog. These tests hand-build the derived map that the catalog
// would normally supply, so no router is needed.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests
{
    [TestClass]
    public class WinboxPathAliasTest
    {
        // Stands in for WinboxJgCatalog.GetDerivedPaths(): menu-label path → handler, as harvested from the
        // router's version-matched .jg menu tree.
        private static WinboxHandlerMap MapWithDerived(params (string menuPath, int[] handler)[] entries)
        {
            var derived = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var (menuPath, handler) in entries)
                derived[menuPath] = handler;

            var map = new WinboxHandlerMap();
            map.SetDerivedPaths(derived);
            return map;
        }

        [TestMethod]
        public void PathAlias_MapsApiPathToMenuLabelPath()
        {
            // WinBox shows:  PPP ▸ Secrets ▸ (window) PPP Secret   →  /ppp/secrets/ppp-secret
            var map = MapWithDerived(("/ppp/secrets/ppp-secret", new[] { 20, 12 }));
            Assert.IsNull(map.Resolve("/ppp/secret"), "no bridge yet — the API leaf is not a menu label");

            map.AddAlias("/ppp/secret", "/ppp/secrets/ppp-secret");

            CollectionAssert.AreEqual(new[] { 20, 12 }, map.Resolve("/ppp/secret"));
        }

        [TestMethod]
        public void PathAlias_NormalizesBothSides()
        {
            var map = MapWithDerived(("/ppp/secrets/ppp-secret", new[] { 20, 12 }));
            // Leading/trailing slash noise and case are folded on registration and on lookup.
            map.AddAlias("ppp/secret/", "/PPP/Secrets/PPP-Secret");

            CollectionAssert.AreEqual(new[] { 20, 12 }, map.Resolve("/ppp/secret"));
            CollectionAssert.AreEqual(new[] { 20, 12 }, map.Resolve("/PPP/Secret"));
        }

        [TestMethod]
        public void PathAlias_TracksHandlerLiveFromCatalog()
        {
            // The point of aliasing by text: a RouterOS upgrade that renumbers the handler needs no code change,
            // because only the label path is pinned — re-supplying the derived map is enough.
            var map = MapWithDerived(("/ppp/secrets/ppp-secret", new[] { 20, 12 }));
            map.AddAlias("/ppp/secret", "/ppp/secrets/ppp-secret");
            CollectionAssert.AreEqual(new[] { 20, 12 }, map.Resolve("/ppp/secret"));

            map.SetDerivedPaths(new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["/ppp/secrets/ppp-secret"] = new[] { 20, 44 },
            });
            CollectionAssert.AreEqual(new[] { 20, 44 }, map.Resolve("/ppp/secret"));
        }

        [TestMethod]
        public void PathAlias_ToUnknownMenuPath_DoesNotResolve()
        {
            var map = MapWithDerived(("/ppp/secrets/ppp-secret", new[] { 20, 12 }));
            map.AddAlias("/ppp/secret", "/ppp/typo-not-a-window");

            Assert.IsNull(map.Resolve("/ppp/secret"));
        }

        [TestMethod]
        public void PathOverride_WinsOverPathAlias()
        {
            // Resolution order: numeric session override → direct .jg hit → session alias → shipped alias.
            var map = MapWithDerived(("/ppp/secrets/ppp-secret", new[] { 20, 12 }));
            map.AddAlias("/ppp/secret", "/ppp/secrets/ppp-secret");
            map.AddOverride("/ppp/secret", new[] { 27, 101 });

            CollectionAssert.AreEqual(new[] { 27, 101 }, map.Resolve("/ppp/secret"));
        }

        [TestMethod]
        public void PathAlias_WinsOverShippedAlias()
        {
            // /interface/list ships as → /interfaces/interface-list; a session alias must be able to redirect it
            // (e.g. a RouterOS version that renamed the window) without a library change.
            var map = MapWithDerived(
                ("/interfaces/interface-list", new[] { 20, 5 }),
                ("/interfaces/renamed-list", new[] { 20, 9 }));

            CollectionAssert.AreEqual(new[] { 20, 5 }, map.Resolve("/interface/list"), "shipped alias baseline");

            map.AddAlias("/interface/list", "/interfaces/renamed-list");
            CollectionAssert.AreEqual(new[] { 20, 9 }, map.Resolve("/interface/list"));
        }

        [TestMethod]
        public void PathAlias_ComposesWithGuiNameAddressing()
        {
            // UseGuiNames folds the WinBox separators on the INPUT side, then the alias lookup runs as usual —
            // so a path typed the way the GUI menu spells it still lands on the aliased window.
            var map = MapWithDerived(("/ppp/secrets/ppp-secret", new[] { 20, 12 }));
            map.AddAlias("/ppp/secret", "/ppp/secrets/ppp-secret");
            map.UseGuiNames = true;

            CollectionAssert.AreEqual(new[] { 20, 12 }, map.Resolve("/PPP/Secret"));
            CollectionAssert.AreEqual(new[] { 20, 12 }, map.Resolve("/ppp/secrets/PPP Secret"));
        }
    }
}
