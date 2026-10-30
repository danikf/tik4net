// WinboxWireKindContradictionTests.cs — a row whose value is not the kind its field is declared (WinBox review F10).
//
// Both directions are one rule: the field is not reported on that row, and the codec's trace says so. Text declared,
// number carried is /interface's 0x1001E on an enslaved row (WinboxEnslavedInterfaceTypeTests). Number declared, text
// carried was measured on 7.25: a firewall rule whose src-address key (u32) held str "999.1.1.1", written by an M2
// client; the API printed src-address="" and every native read of the table threw decoding that one row.
// Router-free: a cut-down window, the resolver and the codec.

#nullable enable

using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Diagnostics;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    [DoNotParallelize]   // TikWireTrace.Capture installs a process-wide sink
    public class WinboxWireKindContradictionTests
    {
        private sealed class NoteSink : ITikWireTraceSink
        {
            internal readonly List<string> Notes = new List<string>();

            public void Emit(string channel, TikWireDir dir, byte[]? data, int offset, int count, string? note)
            {
                if (channel == "wbx.codec" && note != null) Notes.Add(note);
            }
        }

        private const string Window =
            "[{name:'X',c:[{name:'Y',title:'Y',type:'map',path:[ 90,1 ],c:[" +
            "{name:'Name',type:'string',id:'s10006'}," +
            "{name:'Limit',type:'number',id:'u5'}," +
            "{name:'Gateway',type:'ipaddr',id:'u6'}]}]}]";

        private static Dictionary<string, string> Decode(M2Record rec)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(Window), "the window must parse");
            var resolver = new WinboxFieldResolver("/x/y", new[] { 90, 1 }, catalog, new Dictionary<string, int>());
            return new WinboxRecordCodec(null!, null!).DecodeRecord(rec, resolver.BuildKeyToApiName(), resolver.BuildKeyToField());
        }

        [TestMethod]
        public void TextOnANumberField_IsNotReported()
        {
            var sink = new NoteSink();
            Dictionary<string, string> row;
            using (TikWireTrace.Capture(sink))
                row = Decode(new M2Record
                {
                    [WinboxM2Protocol.RecordKey.Name] = new M2Value(M2Type.String, "a"),
                    [0x5] = new M2Value(M2Type.String, "auto"),
                });
            Assert.AreEqual("a", row["name"]);
            Assert.IsFalse(row.ContainsKey("limit"), "a number field read as the text the row carried: " +
                (row.TryGetValue("limit", out var v) ? v : ""));
            // The note is what WinboxWireKindContradictionProbe counts on a live router.
            Assert.IsTrue(sink.Notes.Any(n => n.StartsWith("'limit' (key 0x5) is declared u32")), string.Join("; ", sink.Notes));
        }

        [TestMethod]
        public void TextOnAnAddressField_DoesNotFailTheRow()
        {
            var row = Decode(new M2Record
            {
                [WinboxM2Protocol.RecordKey.Name] = new M2Value(M2Type.String, "a"),
                [0x6] = new M2Value(M2Type.String, "999.1.1.1"),
            });
            Assert.AreEqual("a", row["name"]);
            Assert.IsFalse(row.ContainsKey("gateway"));
        }

        [TestMethod]
        public void ANumberOnANumberField_IsStillReported()
        {
            var row = Decode(new M2Record
            {
                [0x5] = new M2Value(M2Type.U32, 7u),
                [0x6] = new M2Value(M2Type.U32, 0x0104A8C0u),
            });
            Assert.AreEqual("7", row["limit"]);
            Assert.AreEqual("192.168.4.1", row["gateway"]);
        }
    }
}
