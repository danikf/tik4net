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
        /// The same defect without any case involved: the abbreviation-dot rule turns '2.5Gbps' into
        /// '25gbps', which is already a member. The API prints <c>rate=1Gbps</c>, i.e. the raw label.
        /// </summary>
        [TestMethod]
        public void ADotStrippedLabelThatCollidesAlsoKeepsItsRawForm()
        {
            var window =
                "[{name:'Interfaces',c:[{name:'Interface',title:'Interface',type:'map',path:[ 20,0 ],c:[" +
                "{name:'Rate',type:'enm',id:'u1',values:{type:'static',map:[" +
                "'unknown','10Mbps','100Mbps','1Gbps','2.5Gbps','5Gbps','10Gbps','25Gbps']}}]}]}]";
            var map = MapOf(Parse(window), new[] { 20, 0 }, "rate");

            Assert.AreEqual("2.5Gbps", map[4], "'2.5Gbps' and '25Gbps' both normalize to 25gbps");
            Assert.AreEqual("25Gbps", map[7]);
            Assert.AreEqual("1Gbps", map[3], "and the API prints exactly this");
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
