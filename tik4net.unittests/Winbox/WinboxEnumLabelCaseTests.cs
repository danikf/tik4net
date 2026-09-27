// WinboxEnumLabelCaseTests.cs — G8(c): normalizing a .jg enum label is right until it merges two members.
//
// Every .jg label is normalized (lowercased, whitespace folded to hyphens, abbreviation dots dropped), and
// that is what makes a label match the API's spelling at all: 'as username' is the API's as-username, 'key 0'
// is key-0. But a handful of maps distinguish their members by exactly the characters normalization removes.
// A sweep of the whole 7.24 catalog finds three, and in all three the RAW label is what RouterOS prints and
// accepts, so keeping it is not a compromise — normalizing was simply wrong for them.

using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxEnumLabelCaseTests
    {
        private static WinboxJgCatalog Parse(string body)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(body), "the trimmed window must parse");
            return catalog;
        }

        private static IReadOnlyDictionary<int, string> MapOf(WinboxJgCatalog catalog, int[] handler, string field)
            => catalog.GetHandlerFields(handler)[field].EnumMap;

        // The wireless security profile's MAC Format: the same seven formats twice, upper then lower. The
        // router agrees they are fourteen distinct values — `set radius-mac-format=` tab-completes to all of
        // them — and the case selects how the MAC is sent to the RADIUS server.
        private const string MacFormatWindow =
            "[{name:'Wireless',c:[{name:'Security Profile',title:'Security Profiles',type:'map',path:[ 88,14 ],c:[" +
            "{name:'MAC Format',type:'enm',id:'u1c',values:{type:'static',map:[" +
            "'XX:XX:XX:XX:XX:XX','XXXX:XXXX:XXXX','XXXXXX:XXXXXX','XX-XX-XX-XX-XX-XX','XXXXXX-XXXXXX'," +
            "'XXXXXXXXXXXX','XX XX XX XX XX XX','xx:xx:xx:xx:xx:xx','xxxx:xxxx:xxxx','xxxxxx:xxxxxx'," +
            "'xx-xx-xx-xx-xx-xx','xxxxxx-xxxxxx','xxxxxxxxxxxx','xx xx xx xx xx xx']}}," +
            "{name:'MAC Mode',type:'enm',id:'u1d',values:{type:'static',map:['as username','as username and password']}}" +
            "]}]}]";

        /// <summary>
        /// The whole point: fourteen members must stay fourteen. Normalized, they collapse to six — both by
        /// case (XX: / xx:) and by separator ('XX XX…' and 'XX-XX…' both fold to hyphens).
        /// </summary>
        [TestMethod]
        public void AMapWhoseLabelsWouldMergeKeepsThemRaw()
        {
            var map = MapOf(Parse(MacFormatWindow), new[] { 88, 14 }, "mac-format");

            Assert.AreEqual(14, map.Count);
            Assert.AreEqual(14, map.Values.Distinct().Count(),
                "normalized, these fourteen collapse to six — and the router treats all fourteen as distinct");
            Assert.AreEqual("XX:XX:XX:XX:XX:XX", map[0], "member 0 is the uppercase one, as RouterOS prints it");
            Assert.AreEqual("xx:xx:xx:xx:xx:xx", map[7], "and member 7 is its lowercase twin");
            Assert.AreEqual("XX XX XX XX XX XX", map[6], "the spaces survive too — the API quotes and keeps them");
        }

        /// <summary>
        /// And the rule is local to the map that needs it: a neighbouring field in the same window is still
        /// normalized, which is what makes it match the API at all.
        /// </summary>
        [TestMethod]
        public void ANeighbouringMapIsStillNormalized()
        {
            var map = MapOf(Parse(MacFormatWindow), new[] { 88, 14 }, "mac-mode");

            Assert.AreEqual("as-username", map[0], "'as username' is the API's as-username");
            Assert.AreEqual("as-username-and-password", map[1]);
        }

        /// <summary>
        /// A rate is a quantity, and RouterOS keeps its case and its point: the API prints <c>rate=1Gbps</c>, and
        /// '2.5Gbps' stays distinct from '25Gbps'.
        /// </summary>
        [TestMethod]
        public void ARateMemberKeepsItsCaseAndItsPoint()
        {
            var window =
                "[{name:'Interfaces',c:[{name:'Interface',title:'Interface',type:'map',path:[ 20,0 ],c:[" +
                "{name:'Rate',type:'enm',id:'u1',values:{type:'static',map:[" +
                "'unknown','10Mbps','100Mbps','1Gbps','2.5Gbps','5Gbps','10Gbps','25Gbps']}}]}]}]";
            var map = MapOf(Parse(window), new[] { 20, 0 }, "rate");

            Assert.AreEqual("2.5Gbps", map[4]);
            Assert.AreEqual("25Gbps", map[7]);
            Assert.AreEqual("1Gbps", map[3], "and the API prints exactly this");
        }

        /// <summary>
        /// An ethernet link mode is led by a quantity, and RouterOS joins its words with dashes and keeps the rest:
        /// completion of <c>/interface ethernet set advertise=</c> lists <c>100M-baseT-full</c> and
        /// <c>2.5G-baseX</c> (7.24.4).
        /// </summary>
        [TestMethod]
        public void ALinkModeKeepsItsCaseAndJoinsItsWords()
        {
            var window =
                "[{name:'Interfaces',c:[{name:'Ethernet',title:'Ethernet',type:'map',path:[ 20,1 ],c:[" +
                "{name:'Speed',type:'enm',id:'u41b',values:{type:'static',map:{0:'10M baseT half',3:'100M baseT full'," +
                "15:'2.5G baseX',25:'40G baseSR4 LR4'}}}]}]}]";
            var map = MapOf(Parse(window), new[] { 20, 1 }, "speed");

            Assert.AreEqual("10M-baseT-half", map[0]);
            Assert.AreEqual("100M-baseT-full", map[3]);
            Assert.AreEqual("2.5G-baseX", map[15]);
            Assert.AreEqual("40G-baseSR4-LR4", map[25]);
        }

        /// <summary>
        /// A point between two digits is a version, not an abbreviation: the router completes
        /// <c>/interface bonding add mode=</c> with <c>802.3ad</c> and hotspot <c>nas-port-type=</c> with
        /// <c>wireless-802.11</c> (7.24.4). An abbreviation point in the same map still goes.
        /// </summary>
        [TestMethod]
        public void AVersionPointInAMemberStays()
        {
            var window =
                "[{name:'Interfaces',c:[{name:'Bonding',title:'Bonding',type:'map',path:[ 20,9 ],c:[" +
                "{name:'Mode',type:'enm',id:'u1',values:{type:'static',map:['balance rr','active backup','802.3ad','Std. Mode']}}," +
                "{name:'NAS Port Type',type:'number',id:'u2',values:{type:'static',map:{15:'ethernet',19:'wireless-802.11'}}}]}]}]";
            var mode = MapOf(Parse(window), new[] { 20, 9 }, "mode");
            var nas = MapOf(Parse(window), new[] { 20, 9 }, "nas-port-type");

            Assert.AreEqual("802.3ad", mode[2]);
            Assert.AreEqual("std-mode", mode[3], "an abbreviation point is still dropped");
            Assert.AreEqual("wireless-802.11", nas[19]);
        }

        /// <summary>
        /// A label repeated at two keys is not a collision — a <c>defenum</c> names an id that the wrapped
        /// list then names again. Such a map must still normalize.
        /// </summary>
        [TestMethod]
        public void TheSameLabelAtTwoKeysIsNotACollision()
        {
            var window =
                "[{name:'IP',c:[{name:'Thing',title:'Things',type:'map',path:[ 60,1 ],c:[" +
                "{name:'Mode',type:'enm',id:'u1',values:{type:'defenum',defid:0,defname:'No Mode'," +
                "values:{type:'static',map:{0:'No Mode',1:'Some Mode'}}}}]}]}]";
            var map = MapOf(Parse(window), new[] { 60, 1 }, "mode");

            Assert.AreEqual("no-mode", map[0], "one label on one key twice is not two members merging");
            Assert.AreEqual("some-mode", map[1]);
        }
    }
}
