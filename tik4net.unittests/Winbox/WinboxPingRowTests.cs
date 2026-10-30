// WinboxPingRowTests.cs — /ping and /tool/ping over WinBox native, against the API's rows (7.25).
//
// One ping, both transports. The API: seq host size ttl time sent received packet-loss min-rtt avg-rtt max-rtt.
// The native reply record: {0x10=True, 0x4=0, 0x5=64, 0x7=135, 0xC=0, 0xD=0, 0x64=135, 0xF=0, 0x65=135, 0x66=135,
// 0x69=1, 0x6A=1, 0x67=0, 0xFE0001=0, 0x1=<host>, 0x3=56, 0x68="1 of 1 packets received"}.
// Router-free: the catalog's Ping window (cut down), the resolver and the codec.

using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxPingRowTests
    {
        private const string PingWindow =
            "[{name:'Ping',group:'Tools',c:[{title:'Ping',type:'query',path:[22],autorefresh:1000,cancelcmd:16646161," +
            "request:[{name:'Ping To',type:'addr',id:'m16',allow:'46v%Dm',req:1},{name:'Packet Count',type:'number',id:'u11',opt:1}," +
            "{name:'Packet Size',type:'number',id:'u3',def:56,max:65535,min:28},{name:'Dont Fragment',type:'bool',id:'b4'}]," +
            "startcmd:16646159,status:[{type:'statusbar',c:[{type:'string',id:'s68',cols:20}," +
            "{type:'integer',id:'u67',def:4294967295,opt:1,postfix:'% packet loss'}]}]," +
            "c:[{name:'Seq #',type:'number',id:'uf',width:45},{name:'Reply Size',type:'number',id:'u3',opt:1,width:60}," +
            "{name:'TTL',type:'number',id:'u5',opt:1,width:35}]}]}]";

        private static WinboxFieldResolver Resolver(string path)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(PingWindow), "the trimmed Ping window must parse");
            return new WinboxFieldResolver(path, new[] { 22 }, catalog, new Dictionary<string, int>());
        }

        private static Dictionary<string, string> DecodeReply(string path)
        {
            var resolver = Resolver(path);
            var rec = new M2Record
            {
                [0x4] = new M2Value(M2Type.U32, 0u),
                [0x5] = new M2Value(M2Type.U32, 64u),
                [0xF] = new M2Value(M2Type.U32, 1u),
                [0x69] = new M2Value(M2Type.U32, 2u),
                [0x6A] = new M2Value(M2Type.U32, 2u),
                [0x67] = new M2Value(M2Type.U32, 0u),
                [WinboxM2Protocol.RecordKey.Id] = new M2Value(M2Type.U32, 1u),
                [0x1] = new M2Value(M2Type.U32, 0x0104A8C0u),
                [0x3] = new M2Value(M2Type.U32, 56u),
            };
            return new WinboxRecordCodec(null, null).DecodeRecord(rec, resolver.BuildKeyToApiName(), resolver.BuildKeyToField());
        }

        [DataTestMethod]
        [DataRow("/ping")]
        [DataRow("/tool/ping")]
        public void TheTargetIsWrittenAsAddress(string path)
            => Assert.AreNotEqual(0, Resolver(path).EncodeField("address", "192.168.4.1").Count,
                "'address' rides the window's 'Ping To'");

        [DataTestMethod]
        [DataRow("/ping")]
        [DataRow("/tool/ping")]
        public void AReplyCarriesTheRunningCounts(string path)
        {
            var row = DecodeReply(path);
            Assert.AreEqual("2", row["sent"]);
            Assert.AreEqual("2", row["received"]);
            Assert.AreEqual("0", row["packet-loss"]);
            Assert.AreEqual("192.168.4.1", row["host"]);
            Assert.AreEqual("1", row["seq"]);
        }

        [DataTestMethod]
        [DataRow("/ping")]
        [DataRow("/tool/ping")]
        public void AReplyHasNoIdAndNoRequestField(string path)
        {
            var row = DecodeReply(path);
            Assert.IsFalse(row.ContainsKey(".id"), "the reply's row index is not a field the API prints");
            Assert.IsFalse(row.ContainsKey("dont-fragment"), "0x4 on a reply is not the request's Dont Fragment");
        }
    }
}
