// WinboxSyntheticFieldTests.cs — G10: fields the ROUTER sends that no .jg window names.
//
// The path-map audit recorded /system/health's `state` and `state-after-reboot` as "API-only fields with no
// WinBox equivalent". They are not. A getall on [24,14] answers `0x8=bool:False 0x9=bool:True` against the
// API's `state=disabled state-after-reboot=enabled` — measured on 7.24 — and the pairing was confirmed by
// setting state-after-reboot=disabled over the API and watching 0x9 go True → False while 0x8 stayed put.
// [24,14]'s two windows are 'Settings' (fan control) and the x86-gated 'System Health' (voltages,
// temperatures, `caps` at uf); neither declares key 8 or 9, and the decoder drops what nothing names.
//
// A shipped SYNTHETIC field carries the key, the wire type and the enum map, so the pair reads, resolves and
// writes like any catalogued field. Router-free: everything below is the resolver and the codec.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxSyntheticFieldTests
    {
        private static readonly int[] HealthHandler = { 24, 14 };

        // The catalogued half of the window: `caps` is the only field the .jg names on a non-x86 board, and
        // it must keep working — a synthetic field is an addition, not a replacement.
        private const string HealthWindow =
            "[{name:'System',title:'System',c:[{name:'Health',title:'System Health',type:'item'," +
            "path:[ 24,14 ],c:[{name:'caps',type:'number',id:'uf',nonpublic:1}]}]}]";

        // A resolver for /system/health whose synthetic fields are the shipped ones.
        private static WinboxFieldResolver ResolverWith(WinboxJgField field)
        {
            // The security-profile path, so the shipped alias set for it is in force; the field under test
            // is handed in through the catalog rather than the alias table, which is enough to exercise
            // EncodeField's enum branch.
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(
                "[{name:'X',c:[{name:'Y',title:'Y',type:'map',path:[ 90,1 ],c:[{name:'" + field.ApiName
                + "',type:'enm',id:'u" + field.Key.ToString("x") + "',values:{type:'static',map:{"
                + string.Join(",", field.EnumMap.Select(kv => kv.Key + ":'" + kv.Value + "'"))
                + "}}}]}]}]"));
            return new WinboxFieldResolver("/x/y", new[] { 90, 1 }, catalog, new Dictionary<string, int>());
        }

        private static WinboxFieldResolver Resolver()
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(HealthWindow), "the trimmed window must parse");
            return new WinboxFieldResolver("/system/health", HealthHandler, catalog,
                new Dictionary<string, int>());
        }

        private static Dictionary<string, string> Decode(Dictionary<int, Tuple<string, object>> rec)
        {
            var resolver = Resolver();
            return new WinboxRecordCodec(null, null)
                .DecodeRecord(rec, resolver.BuildKeyToApiName(), resolver.BuildKeyToField());
        }

        private static KeyValuePair<int, object> Encoded(IList<byte[]> encoded)
        {
            Assert.AreEqual(1, encoded.Count, "expected exactly one encoded field");
            var msg = new[] { (byte)'M', (byte)'2' }.Concat(encoded[0]).ToArray();
            var fields = M2Message.ParseAllFields(msg);
            Assert.AreEqual(1, fields.Count);
            foreach (var kv in fields) return new KeyValuePair<int, object>(kv.Key, kv.Value.Item2);
            return default(KeyValuePair<int, object>);
        }

        /// <summary>The two keys read under the API's names, spelled the way RouterOS spells them.</summary>
        [TestMethod]
        public void AKeyNoWindowNamesIsStillDecoded()
        {
            var decoded = Decode(new Dictionary<int, Tuple<string, object>>
            {
                [0x8] = Tuple.Create("bool", (object)false),
                [0x9] = Tuple.Create("bool", (object)true),
                [0xF] = Tuple.Create("u32", (object)0u),
            });

            Assert.AreEqual("disabled", decoded["state"], "the live pair: 0x8=False alongside state=disabled");
            Assert.AreEqual("enabled", decoded["state-after-reboot"], "and 0x9=True alongside enabled");
            Assert.AreEqual("0", decoded["caps"], "the field the .jg DOES name is unaffected");
        }

        /// <summary>
        /// The write must go out as a BOOL. The static-enum encoder emitted a u32 for every mapped value,
        /// and a u32 written to a bool key is a request the router accepts, answers, and ignores — the same
        /// silent shape as G4's interval write.
        /// </summary>
        [TestMethod]
        public void AMappedValueOverABoolKeyIsWrittenAsABool()
        {
            var enabled = Encoded(Resolver().EncodeField("state-after-reboot", "enabled"));
            Assert.AreEqual(0x9, enabled.Key);
            Assert.AreEqual(true, enabled.Value, "'enabled' is the map's member 1, and the key is a bool");

            var disabled = Encoded(Resolver().EncodeField("state-after-reboot", "disabled"));
            Assert.AreEqual(0x9, disabled.Key);
            Assert.AreEqual(false, disabled.Value);
        }

        /// <summary>
        /// `state` is read-only on the router — <c>/system/health set</c> tab-completes to
        /// state-after-reboot and nothing else — so the synthetic field is declared read-only and encodes to
        /// nothing, rather than sending a field the router would refuse.
        /// </summary>
        [TestMethod]
        public void TheReadOnlyHalfEncodesToNothing()
        {
            Assert.AreEqual(0, Resolver().EncodeField("state", "enabled").Count);
        }

        /// <summary>
        /// G8(c), the write side: an EXACT match wins over a case-insensitive one.
        /// </summary>
        /// <remarks>
        /// Matching case-insensitively in one pass returns whichever member comes FIRST, so on a map that
        /// carries a value twice — a wireless MAC format, upper then lower — every lowercase value was
        /// written as its uppercase twin, silently changing what the router sends to RADIUS. The
        /// case-insensitive pass is kept as a FALLBACK, because on every other map the API's spelling and
        /// the .jg label routinely differ in case and always did resolve.
        /// </remarks>
        [TestMethod]
        public void AnExactEnumMatchWinsOverACaseInsensitiveOne()
        {
            var twins = new Dictionary<int, string> { [0] = "XX:XX:XX:XX:XX:XX", [7] = "xx:xx:xx:xx:xx:xx" };
            var field = new WinboxJgField("mac-format", 0x1C, "u32", false, enumMap: twins);
            var resolver = ResolverWith(field);

            Assert.AreEqual(7L, Convert.ToInt64(Encoded(resolver.EncodeField("mac-format", "xx:xx:xx:xx:xx:xx")).Value),
                "the lowercase value is member 7, not its uppercase twin at 0");
            Assert.AreEqual(0L, Convert.ToInt64(Encoded(resolver.EncodeField("mac-format", "XX:XX:XX:XX:XX:XX")).Value));

            // The fallback still resolves a value whose case matches no member exactly.
            var single = new Dictionary<int, string> { [0] = "as-username", [1] = "as-username-and-password" };
            var plain = ResolverWith(new WinboxJgField("mac-mode", 0x1D, "u32", false, enumMap: single));
            Assert.AreEqual(1L, Convert.ToInt64(Encoded(plain.EncodeField("mac-mode", "AS-Username-And-Password")).Value),
                "an inexact case must still resolve — that is what the fallback is for");
        }

        /// <summary>A synthetic field resolves for a write like any other — that is what makes it settable.</summary>
        [TestMethod]
        public void ASyntheticFieldResolvesItsKey()
        {
            Assert.AreEqual(0x9, Resolver().ResolveKey("state-after-reboot"));
        }
    }
}
