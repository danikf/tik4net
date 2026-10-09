// WinboxFallbackFieldTests.cs — router-free tests for a shipped FALLBACK field: a key one RouterOS version's window
// leaves unnamed and another version names under a different key.
//
// /ip/route's vrf-interface is 0x3C on 6.49.13, where the window declares nothing there, and 'VRF Interface' u111 on
// 7.x. The fallback has to name 0x3C on the first and stay out of the second's way — an ordinary synthetic outranks
// the catalog by name and would send a 7.x vrf-interface to 0x3C. The window fragments are cut down to the fields
// the rule reads.

using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxFallbackFieldTests
    {
        private static readonly int[] Route6 = { 44, 1 };
        private static readonly int[] Route7 = { 44, 21 };

        private static WinboxFieldResolver Resolver(string window, int[] handler)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto("[" + window + "]"), "the fragment should parse");
            return new WinboxFieldResolver("/ip/route", handler, catalog, new Dictionary<string, int>());
        }

        [TestMethod]
        public void RouterOs6_TheWindowLeavesTheKeyUnnamed_TheFallbackNamesIt()
        {
            var resolver = Resolver("{name:'Routes',c:[{name:'Route',title:'Routes',type:'map',path:[ 44,1 ],"
                + "refreshfilter:131072,c:[{name:'Scope',type:'number',id:'uf',def:30,max:255}]}]}", Route6);

            Assert.AreEqual("vrf-interface", resolver.BuildKeyToApiName()[0x3C]);
            var field = resolver.BuildKeyToField()[0x3C];
            CollectionAssert.AreEqual(new[] { 20, 0 }, field.RefHandler, "an interface id, printed as the interface's name");
            Assert.IsTrue(field.ReadOnly, "a native write of it has not been measured");
        }

        [TestMethod]
        public void RouterOs7_TheWindowNamesTheField_ItsOwnKeyWins()
        {
            var resolver = Resolver("{name:'Routes',c:[{name:'Route',title:'Routes',type:'map',path:[ 44,21 ],c:["
                + "{name:'VRF Interface',type:'opt',c:[{type:'enm',id:'u111',values:{type:'dynamic',path:[ 20,0 ]}}]}]}]}",
                Route7);

            Assert.AreEqual(0x111, resolver.ResolveKey("vrf-interface"), "a write goes to the key 7.x declares");
            Assert.AreEqual("vrf-interface", resolver.BuildKeyToApiName()[0x111]);
            Assert.IsFalse(resolver.BuildKeyToApiName().ContainsKey(0x3C),
                "the name belongs to the catalog's field; 0x3C is not given it a second time");
        }
    }
}
