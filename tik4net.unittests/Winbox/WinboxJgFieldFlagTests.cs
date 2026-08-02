using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    /// <summary>
    /// Field flags read out of a <c>.jg</c> window, in particular that a flag on a WRAPPED field is not lost
    /// when the <c>opt</c>/<c>not</c> layers are flattened away (P2.33).
    /// </summary>
    /// <remarks>
    /// The catalog has two places that build a field: the plain path, and <c>AddOptionField</c>, which drills
    /// through <c>opt</c>/<c>not</c> wrappers to the value leaf. Any attribute the second one forgets to read
    /// is silently absent for exactly the fields WinBox chose to make optional — and every firewall address
    /// field is an <c>opt→not→network range:1</c>, so a forgotten <c>range</c> turned the range-END sibling
    /// into a "netmask" and made <c>src-address</c> decode as <c>192.0.2.74/6</c>.
    /// </remarks>
    [TestClass]
    public class WinboxJgFieldFlagTests
    {
        // Trimmed from the real roteros.jg Firewall Rule window (handler [20,3]).
        private const string FirewallRuleWindow =
            "[{name:'Firewall Rule',type:'map',path:[20,3],c:[" +
            "{name:'Chain',type:'text',id:'s27'}," +
            "{name:'Src. Address',type:'opt',id:'b1a2',c:[" +
              "{type:'not',id:'bc8',c:[{type:'network',id:'u32',maskid:'u33',range:1}]}]}," +
            "{name:'Dst. Address',type:'opt',id:'b1a3',c:[" +
              "{type:'not',id:'bca',c:[{type:'network',id:'u34',maskid:'u35',range:1}]}]}" +
            "]}]";

        private static WinboxJgField FieldOf(string apiName)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(FirewallRuleWindow), "the trimmed window must parse");
            var fields = catalog.GetHandlerFields(new[] { 20, 3 });
            Assert.IsNotNull(fields, "handler [20,3] must be in the catalog");
            Assert.IsTrue(fields.TryGetValue(apiName, out var f), $"'{apiName}' must be a known field");
            return f;
        }

        [TestMethod]
        public void AWrappedNetworkFieldKeepsItsRangeFlag()
        {
            WinboxJgField src = FieldOf("src-address");

            Assert.AreEqual("network", src.UiType);
            Assert.AreEqual(0x32, src.Key, "the value leaf's key, not the opt wrapper's");
            Assert.AreEqual(0x33, src.MaskKey);
            Assert.IsTrue(src.IsRange,
                "range:1 sits on the leaf INSIDE opt→not; losing it makes the end address decode as a netmask");
        }

        [TestMethod]
        public void TheOptAndNotFlagKeysAreStillCarried()
        {
            WinboxJgField src = FieldOf("src-address");

            Assert.AreEqual(0x1A2, src.OptKey, "the opt bool the router needs set for the value to count");
            Assert.AreEqual(0xC8, src.NotKey);
        }

        [TestMethod]
        public void EachWrappedFieldResolvesToItsOwnLeaf()
        {
            WinboxJgField dst = FieldOf("dst-address");

            Assert.AreEqual(0x34, dst.Key);
            Assert.AreEqual(0x35, dst.MaskKey);
            Assert.IsTrue(dst.IsRange);
        }
    }
}
