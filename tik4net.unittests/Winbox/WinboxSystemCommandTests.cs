// WinboxSystemCommandTests.cs — /system/reboot and /system/shutdown over WinBox native.
//
// They are commands, not windows with records: System → Reboot opens a window whose one doit is on the system
// handler [24] with cmd 5, Shutdown the same with cmd 6 — and [24] carries Reset Configuration (7) and a package
// downgrade (9) as well, so the action is picked by the path's last segment. The windows below are verbatim from the
// 6.49.13 catalog; 7.24.4 declares them identically.

using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxSystemCommandTests
    {
        private const string SystemWindows =
            "[{name:'Reboot',title:'Reboot',group:'System',c:[{title:'Reboot',type:'doit',path:[ 24 ],cmd:5," +
            "confirm:'Do you want to reboot the router?'}]}," +
            "{name:'Shutdown',title:'Shutdown',group:'System',c:[{title:'Shutdown',type:'doit',path:[ 24 ],cmd:6," +
            "confirm:'Do you want to shut down the router?'}]}]";

        private static (WinboxJgCatalog catalog, WinboxHandlerMap map) Load()
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(SystemWindows), "the System windows must parse");
            var map = new WinboxHandlerMap();
            map.SetDerivedPaths(catalog.GetDerivedPaths());
            return (catalog, map);
        }

        [TestMethod]
        public void RebootAndShutdown_ResolveToTheSystemHandler()
        {
            var (_, map) = Load();

            CollectionAssert.AreEqual(new[] { 24 }, map.Resolve("/system/reboot"));
            CollectionAssert.AreEqual(new[] { 24 }, map.Resolve("/system/shutdown"));
        }

        [TestMethod]
        public void TheActionIsPickedByThePathsLastSegment()
        {
            var (catalog, _) = Load();

            Assert.IsTrue(catalog.IsActionOnlyHandler(new[] { 24 }));
            Assert.AreEqual(-1, catalog.GetSoleActionCmd(new[] { 24 }, out _), "two actions share the handler");
            Assert.AreEqual(5, catalog.GetActionCmd(new[] { 24 }, "reboot", out string reboot));
            Assert.AreEqual("reboot", reboot);
            Assert.AreEqual(6, catalog.GetActionCmd(new[] { 24 }, "shutdown", out _));
            Assert.AreEqual(-1, catalog.GetActionCmd(new[] { 24 }, "restart", out _), "no guessing");
        }
    }
}
