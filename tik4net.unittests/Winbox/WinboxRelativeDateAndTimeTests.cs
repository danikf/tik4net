// WinboxRelativeDateAndTimeTests.cs — G12: a `dateandtime` whose declaration says `relative:1` is a moment
// on the router's UPTIME clock, not seconds since 1970.
//
// The numbers here are the live 7.24 measurement that opened the item: /interface's last-link-up-time and
// last-link-down-time came back as 1970-02-28 19:01:49 / 19:05:09 over winboxnative where the API said
// 2026-08-21 21:48:56 / 21:48:58. The raw wire values were 5 079 709 and 5 079 909; the router's clock
// (23:55:46) minus its uptime (16h13m27s) puts boot at 07:42:19, and boot plus those two distances in
// hundredths is the API's pair to the second. The 200-tick gap being the API's 2-second gap is what rules
// out a coincidence.
//
// Nothing here needs a router: the .jg fragment is the real declaration, and the boot moment is seeded.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxRelativeDateAndTimeTests
    {
        // roteros.jg, the Interface window's Status tab, cut down to the two fields and their name column.
        // `relative:1` and `scale:100` are the declaration's own — they are what says these two are not epoch
        // timestamps, and the whole defect was reading them as if they were.
        private const string InterfaceWindow =
            "[{name:'Interface',type:'map',path:[ 20,0 ],c:[" +
            "{name:'Name',type:'text',id:'s1'}," +
            "{name:'Last Link Down Time',type:'dateandtime',id:'u10037',opt:1,relative:1,ro:1,scale:100}," +
            "{name:'Last Link Up Time',type:'dateandtime',id:'u10038',opt:1,relative:1,ro:1,scale:100}]}]";

        // /certificate's 'Invalid After' — the ABSOLUTE shape, declaring no `relative`, which must keep
        // reading as unix-epoch seconds.
        private const string CertificateWindow =
            "[{name:'Certificate',type:'map',path:[ 19,1 ],c:[" +
            "{name:'Invalid After',type:'dateandtime',id:'u11',opt:1,ro:1}]}]";

        // The boot moment behind the measurement above: 2026-08-21 07:42:19 in the router's own timezone,
        // expressed the way every dateandtime value on the wire is — epoch seconds with the timezone already
        // in them (an absolute value decodes with no shift, which is what pins the frame).
        private const long BootEpoch = 1787298139L;

        private const long LastLinkUpWire = 5079709L;
        private const long LastLinkDownWire = 5079909L;

        private static WinboxJgCatalog Parse(string body)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(body), "the trimmed window must parse");
            return catalog;
        }

        private static Dictionary<string, string> Decode(
            WinboxJgCatalog catalog, int[] handler, Dictionary<int, Tuple<string, object>> rec,
            long? bootEpoch = null)
        {
            var resolver = new WinboxFieldResolver(null, handler, catalog, new Dictionary<string, int>());
            var codec = new WinboxRecordCodec(null, catalog);
            if (bootEpoch != null) codec.SeedRouterClock(bootEpoch.Value);
            return codec.DecodeRecord(rec, resolver.BuildKeyToApiName(), resolver.BuildKeyToField());
        }

        private static Dictionary<int, Tuple<string, object>> Rec(params (int key, string type, object val)[] fields)
        {
            var rec = new Dictionary<int, Tuple<string, object>>();
            foreach (var f in fields) rec[f.key] = Tuple.Create(f.type, f.val);
            return rec;
        }

        [TestMethod]
        public void ARelativeDateAndTimeIsCountedFromBootAndNotFromTheEpoch()
        {
            var decoded = Decode(Parse(InterfaceWindow), new[] { 20, 0 },
                Rec((0x10038, "u32", (uint)LastLinkUpWire), (0x10037, "u32", (uint)LastLinkDownWire)),
                bootEpoch: BootEpoch);

            // The two timestamps the API prints for exactly this record.
            Assert.AreEqual("2026-08-21 21:48:56", decoded["last-link-up-time"]);
            Assert.AreEqual("2026-08-21 21:48:58", decoded["last-link-down-time"]);
        }

        [TestMethod]
        public void TheScaleIsHundredthsSoTwoHundredTicksAreTwoSeconds()
        {
            // What rules out a fitted constant: the DISTANCE between the two values has to survive the same
            // arithmetic, and 200 wire units are the API's two seconds only if the scale is honoured.
            var up = DateTime.ParseExact(
                Decode(Parse(InterfaceWindow), new[] { 20, 0 },
                       Rec((0x10038, "u32", (uint)LastLinkUpWire)), BootEpoch)["last-link-up-time"],
                "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            var down = DateTime.ParseExact(
                Decode(Parse(InterfaceWindow), new[] { 20, 0 },
                       Rec((0x10037, "u32", (uint)LastLinkDownWire)), BootEpoch)["last-link-down-time"],
                "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

            Assert.AreEqual(TimeSpan.FromSeconds(2), down - up);
        }

        [TestMethod]
        public void WithoutTheRoutersClockTheRawValueIsKeptRatherThanA1970Date()
        {
            // The honest answer with no ops channel to ask, and the shape of the defect itself: rendered as
            // epoch seconds these two land in February 1970 — a well-formed timestamp, which is why neither
            // the path-map audit nor an eyeball on the field name catches it. An `age` already answers this
            // way for the same reason.
            var decoded = Decode(Parse(InterfaceWindow), new[] { 20, 0 },
                Rec((0x10038, "u32", (uint)LastLinkUpWire)));

            Assert.AreEqual(LastLinkUpWire.ToString(), decoded["last-link-up-time"],
                "no boot moment available — the raw value, never a date computed from a guessed origin");
        }

        [TestMethod]
        public void AnAbsoluteDateAndTimeStillReadsAsEpochSeconds()
        {
            // A pin, not evidence: this passes before the fix as well. It is here because the fix reaches into
            // the case that decodes every certificate expiry, and `relative` must be the only thing that
            // changes. 1784975092 is the API's 2026-07-25 10:24:52 for the lab's own certificate.
            Assert.AreEqual("2026-07-25 10:24:52",
                Decode(Parse(CertificateWindow), new[] { 19, 1 },
                       Rec((0x11, "u32", 1784975092u)), BootEpoch)["invalid-after"],
                "a declaration without `relative` is unaffected, seeded clock or not");
        }
    }
}
