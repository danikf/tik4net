// WinboxPairedTupleTests.cs — an 'X/Y rest' tuple is two API fields, and a flag can share its key with a number.
//
// Measured on RouterOS 7.24.2. The IPv4 Connections window declares 'Orig./Repl. Bytes' as a tuple of two unnamed
// bigbytes (q20/q24) and the API prints orig-bytes and repl-bytes; 'Orig./Repl. Packets' puts its first part on
// q1f, the key the same window's 'Hw. Offload' flag rides as b1f — one record carries both, a u64 and a bool on
// 0x1F. A bridge port's 'Tx/Rx BPDU's' is tx-bpdu and rx-bpdu the same way. Router-free: parser, catalog,
// resolver and codec.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxPairedTupleTests
    {
        private static Dictionary<string, string> Decode(string catalogText, string apiPath, int[] handler, params byte[][] fields)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(catalogText), "the trimmed catalog must parse");
            var resolver = new WinboxFieldResolver(apiPath, handler, catalog, new Dictionary<string, int>());
            var rec = M2Message.ParseAllFields(M2Message.BuildM2(new[] { M2Message.SysFrom() }.Concat(fields).ToArray()));
            return new WinboxRecordCodec(null, catalog).DecodeRecord(rec, resolver.BuildKeyToApiName(),
                resolver.BuildKeyToField(), resolver.DerivedBoolFields);
        }

        private const string Connections =
            "[{name:'IP',c:[{name:'Connection',title:'Connections',type:'map',path:[ 20,32 ],ro:1,c:[" +
            "{name:'Orig./Repl. Bytes',type:'tuple',c:[{type:'bigbytes',id:'q20'},{type:'bigbytes',id:'q24'}]}," +
            "{name:'Orig./Repl. Packets',type:'tuple',c:[{type:'bigdecimal',id:'q1f'},{type:'bigdecimal',id:'q23'}]}," +
            "{name:'Hw. Offload',type:'flag',id:'b1f',hint:'H'},{name:'helper used',type:'flag',id:'b2b',hint:'h'}]}]}]";

        [TestMethod]
        public void AFlagAndANumberOnOneKeyAreBothKept()
        {
            var rec = M2Message.ParseAllFields(M2Message.BuildM2(M2Message.SysFrom(),
                M2Message.BoolSys(0x1F, false), M2Message.U64Sys(0x1F, 76)));

            Assert.IsTrue(rec.Values.Any(v => v.Item1 == "bool"), "the flag");
            Assert.IsTrue(rec.Values.Any(v => v.Item2?.ToString() == "76"), "and the packet count beside it");
        }

        [TestMethod]
        public void AnOrigReplTupleIsTheApisTwoFields()
        {
            var row = Decode(Connections, "/ip/firewall/connection", new[] { 20, 32 },
                M2Message.U64Sys(0x20, 2128), M2Message.U64Sys(0x24, 0),
                M2Message.BoolSys(0x1F, false), M2Message.U64Sys(0x1F, 76), M2Message.U64Sys(0x23, 3),
                M2Message.BoolSys(0x2B, false), M2Message.U32Sys(0x15, 26642));

            Assert.AreEqual("2128", row["orig-bytes"]);
            Assert.AreEqual("0", row["repl-bytes"]);
            Assert.AreEqual("76", row["orig-packets"]);
            Assert.AreEqual("3", row["repl-packets"]);
            Assert.AreEqual("false", row["hw-offload"]);
            Assert.AreEqual("false", row["uses-helper"]);
            Assert.AreEqual("26642", row["gre-key"]);
            Assert.IsFalse(row.Keys.Any(k => k.Contains("/")), "no API name carries a slash");
        }

        [TestMethod]
        public void ABridgePortsStatusFieldsAnswerToTheApiNames()
        {
            const string ports =
                "[{name:'Bridge',c:[{name:'Bridge Port',title:'Ports',type:'map',path:[ 16,2 ],c:[" +
                "{name:'Hardware Offload',type:'bool',id:'bf'},{name:'External FDB',type:'bool',id:'bce',ro:1}," +
                "{name:'Hw. Offload',type:'bool',id:'bd7',ro:1}," +
                "{name:'Tx/Rx BPDU\\'s',type:'tuple',ro:1,sep:'/',c:[{type:'number',id:'ue0',ro:1},{type:'number',id:'ue1',ro:1}]}]}]}]";

            var row = Decode(ports, "/interface/bridge/port", new[] { 16, 2 },
                M2Message.BoolSys(0xF, true), M2Message.BoolSys(0xCE, false), M2Message.BoolSys(0xD7, true),
                M2Message.U32Sys(0xE0, 5), M2Message.U32Sys(0xE1, 7));

            Assert.AreEqual("true", row["hardware-offload"]);
            Assert.AreEqual("false", row["external-fdb-status"]);
            Assert.IsFalse(row.ContainsKey("hw"), "bd7 read false where the API printed hw=true — not the same field");
            Assert.AreEqual("5", row["tx-bpdu"]);
            Assert.AreEqual("7", row["rx-bpdu"]);
        }
    }
}
