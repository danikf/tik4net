// WinboxAuditExposedDecodeTests.cs — the four value differences the transport audit could not see while it
// skipped every field whose NAME looked like a counter (time, rate, age, count, …).
//
// Measured on RouterOS 7.24.2, WinBox native against the binary API:
//   /system/watchdog  ping-timeout  wire 60     API 1m        {number,postfix:'s'}
//   /queue/tree       burst-time    wire 0      API 0s        {number,opt:1,postfix:'s'}
//   /system/clock     time          wire 84043  API 23:20:43  {clocktime}
//   /system/scheduler start-time    wire 84032  API 23:20:32  {enm,map:{4294967295:'startup'},c:[{clocktime}]}
//   /queue/tree       parent        *10002C1    API the queue's name — values:{pair{pair{static,dynamic [20,0]},
//                                               dynamic [20,12]}}, and only the first table was searched.
// Router-free: the catalog, the resolver and the codec.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxAuditExposedDecodeTests
    {
        private static readonly int[] Handler = { 90, 1 };

        // One window carrying each shape, declared exactly as the 7.24.2 catalog declares it.
        private const string Window =
            "[{name:'X',c:[{title:'X',type:'map',path:[ 90,1 ],c:[" +
            "{name:'Ping Timeout',type:'number',id:'uc',max:600,min:10,postfix:'s'}," +
            "{name:'Burst Time',type:'number',id:'u43',opt:1,postfix:'s'}," +
            "{name:'Time',type:'clocktime',id:'u1e'}," +
            "{name:'Start Time',type:'enm',id:'u12e',values:{type:'static',map:{4294967295:'startup'}},c:[{type:'clocktime',nowdef:1}]}," +
            "{name:'Parent',type:'enm',id:'u1',values:{type:'pair',c:[{type:'pair',c:[{type:'static',map:{16777204:'global'}}," +
            "{type:'dynamic',path:[ 20,0 ]}]},{type:'dynamic',path:[ 20,12 ]}]}}" +
            "]}]}]";

        private static WinboxFieldResolver Resolver()
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(Window), "the trimmed window must parse");
            return new WinboxFieldResolver("/x", Handler, catalog, new Dictionary<string, int>());
        }

        private static string Decode(int key, string wireType, object value)
        {
            var resolver = Resolver();
            var decoded = new WinboxRecordCodec(null, null).DecodeRecord(
                new Dictionary<int, Tuple<string, object>> { [key] = Tuple.Create(wireType, value) },
                resolver.BuildKeyToApiName(), resolver.BuildKeyToField());
            return decoded.Values.Single();
        }

        private static long EncodedU32(string apiName, string value)
        {
            var encoded = Resolver().EncodeField(apiName, value);
            var msg = new[] { (byte)'M', (byte)'2' }.Concat(encoded.Last()).ToArray();
            return Convert.ToInt64(M2Message.ParseAllFields(msg).Single().Value.Item2);
        }

        private static Dictionary<string, string> DecodeWith(string window, int[] handler, string apiPath,
            params (int key, string wire, object value)[] fields)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(window), "the trimmed window must parse");
            var resolver = new WinboxFieldResolver(apiPath, handler, catalog, new Dictionary<string, int>());
            var rec = fields.ToDictionary(f => f.key, f => Tuple.Create(f.wire, f.value));
            return new WinboxRecordCodec(null, catalog).DecodeRecord(rec, resolver.BuildKeyToApiName(), resolver.BuildKeyToField());
        }

        [TestMethod]
        public void AReferencedTableWithoutANameBoxIsNamedByItsNameval()
        {
            // /queue/type's window: nameval:'Type Name' at s16, and no field labelled 'Name'. The resolver seeds
            // name at 0x10006 for such a table — a key its rows never carry — so the nameval key has to be
            // offered as well, or every /queue/simple queue reference reads as a raw id.
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(
                "[{name:'X',c:[{title:'Queue Types',type:'map',path:[ 20,10 ],nameval:'Type Name',c:[" +
                "{name:'Type Name',type:'string',id:'s16',min:1}]}]}]"));

            var (nameKey, nameValKey) = WinboxRecordCodec.RefNameKeys(catalog, new[] { 20, 10 });

            Assert.AreEqual(0x16, nameValKey, "the Type Name box");
            Assert.AreNotEqual(0x16, nameKey, "the seeded name key is not where these rows keep their name");
        }

        [TestMethod]
        public void AnEnumMemberIsNotRenamedByTheFieldLabelOverrides()
        {
            // /interface/vxlan rem-csum's members are none/rx/tx/both; 'tx' went through the FIELD override
            // that maps the interface counter label 'Tx' to tx-byte, and read back as tx-byte (7.24.2).
            var decoded = DecodeWith(
                "[{name:'X',c:[{title:'X',type:'map',path:[ 90,3 ],c:[" +
                "{name:'Remote Checksum Offload',type:'enm',id:'u16',def:0,values:{type:'static',map:{0:'none',1:'rx',2:'tx',3:'both'}}}]}]}]",
                new[] { 90, 3 }, "/interface/vxlan", (0x16, "u32", (object)2u));

            Assert.AreEqual("tx", decoded["rem-csum"], "and the field reads under the API's name");
        }

        [TestMethod]
        public void ATuplesUnitReachesItsSeparateParts()
        {
            // /queue/simple 'Burst Time' {tuple,postfix:'s',separate:1}: the halves are numbers with no unit of
            // their own, and the API prints burst-time=7s/9s.
            var decoded = DecodeWith(
                "[{name:'X',c:[{title:'X',type:'map',path:[ 90,4 ],c:[" +
                "{name:'Burst Time',type:'tuple',postfix:'s',sep:' ',separate:1,c:[" +
                "{name:'Upload Burst Time',type:'number',id:'u43'},{name:'Download Burst Time',type:'number',id:'u4a'}]}]}]}]",
                new[] { 90, 4 }, "/x", (0x43, "u32", (object)7u), (0x4A, "u32", (object)9u));

            Assert.AreEqual("7s", decoded["upload-burst-time"]);
            Assert.AreEqual("9s", decoded["download-burst-time"]);
        }

        [TestMethod]
        public void TrafficFlowTargetTemplateTimeoutIsADuration()
        {
            // {number,def:1800} with no postfix, and the API prints 44m where the wire carries 2640 (7.24.2).
            var decoded = DecodeWith(
                "[{name:'X',c:[{title:'X',type:'map',path:[ 90,5 ],c:[" +
                "{name:'v9/IPFIX Template Refresh',type:'number',id:'u4',def:20,min:1}," +
                "{name:'v9/IPFIX Template Timeout',type:'number',id:'u5',def:1800}]}]}]",
                new[] { 90, 5 }, "/ip/traffic-flow/target", (0x4, "u32", (object)33u), (0x5, "u32", (object)2640u));

            Assert.AreEqual("33", decoded["v9-template-refresh"], "refresh is a packet count");
            Assert.AreEqual("44m", decoded["v9-template-timeout"]);
        }

        [TestMethod]
        public void AContractionsApostropheIsNotPartOfTheApiName()
        {
            // /interface/vxlan "Don't Fragment" and /system/script "Don't Require Permissions" (7.24.2): the API
            // says dont-fragment and dont-require-permissions, and native reported don't-….
            Assert.AreEqual("dont-fragment", WinboxFieldResolver.NormalizeLabel("Don't Fragment"));
            Assert.AreEqual("dont-require-permissions", WinboxFieldResolver.NormalizeLabel("Don't Require Permissions"));
            Assert.AreEqual("dst-address", WinboxFieldResolver.NormalizeLabel("Dst. Address"), "the dot rule is unchanged");
        }

        [TestMethod]
        public void ANumberWithASecondsPostfixIsADuration()
        {
            Assert.AreEqual("1m", Decode(0xC, "u32", 60u), "watchdog ping-timeout");
            Assert.AreEqual("0s", Decode(0x43, "u32", 0u), "queue tree burst-time");
            Assert.AreEqual(60L, EncodedU32("ping-timeout", "1m"), "and the API's spelling writes back");
        }

        [TestMethod]
        public void ASecondsNumberTheApiPrintsBareStaysBare()
        {
            // /queue/type sfq-perturb is declared exactly like ping-timeout ({number,postfix:'s'}) and the API
            // prints it as the number: 5, not 5s (7.24.2). Nothing in the declaration tells the two apart.
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(
                "[{name:'X',c:[{title:'X',type:'map',path:[ 90,2 ],c:[" +
                "{name:'SFQ Perturb',type:'number',id:'u191',def:5,postfix:'s'}]}]}]"));
            var resolver = new WinboxFieldResolver("/x", new[] { 90, 2 }, catalog, new Dictionary<string, int>());
            var decoded = new WinboxRecordCodec(null, null).DecodeRecord(
                new Dictionary<int, Tuple<string, object>> { [0x191] = Tuple.Create("u32", (object)5u) },
                resolver.BuildKeyToApiName(), resolver.BuildKeyToField());

            Assert.AreEqual("5", decoded["sfq-perturb"]);
        }

        [TestMethod]
        public void AClockTimeIsATimeOfDay()
        {
            Assert.AreEqual("23:20:43", Decode(0x1E, "u32", 84043u));
            Assert.AreEqual("00:00:05", Decode(0x1E, "u32", 5u));
            Assert.AreEqual(84043L, EncodedU32("time", "23:20:43"));
        }

        [TestMethod]
        public void AnEnumWithAClockTimeElementIsItsWordOrATimeOfDay()
        {
            Assert.AreEqual("23:20:32", Decode(0x12E, "u32", 84032u), "a map miss renders through the element");
            Assert.AreEqual("startup", Decode(0x12E, "u32", 4294967295u), "the map's own member still wins");
            Assert.AreEqual(84032L, EncodedU32("start-time", "23:20:32"));
            Assert.AreEqual(4294967295L, EncodedU32("start-time", "startup"));
        }

        [TestMethod]
        public void APairDropdownKeepsEveryTableItDrawsFrom()
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(Window));
            var parent = catalog.GetHandlerFields(Handler).Values.Single(f => f.Key == 0x1);

            CollectionAssert.AreEqual(new[] { 20, 0 }, parent.RefHandler, "the first source stays the RefHandler");
            Assert.AreEqual(2, parent.RefHandlers!.Count);
            CollectionAssert.AreEqual(new[] { 20, 12 }, parent.RefHandlers[1], "and the queue table is kept after it");
        }

        [TestMethod]
        public void APairDropdownPrimesEveryTableItDrawsFrom()
        {
            // A queue's id is not in the interface table, so the decode will ask the queue table too — and
            // the prefetch must read both, or the second lookup blocks the decoding thread.
            var parent = new WinboxJgField("parent", 0x1, "u32", false, uiType: "enm",
                refHandler: new[] { 20, 0 }, refHandlers: new[] { new[] { 20, 0 }, new[] { 20, 12 } });
            var channel = new CountingChannel();
            new WinboxRecordCodec(new WinboxNativeM2Operations(channel), null).PrimeReferencesAsync(
                    new List<Dictionary<int, Tuple<string, object>>>
                    {
                        new Dictionary<int, Tuple<string, object>> { [0x1] = Tuple.Create("u32", (object)0x10002C1u) },
                    },
                    new Dictionary<int, string> { [0x1] = "parent" },
                    new Dictionary<int, WinboxJgField> { [0x1] = parent },
                    CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreEqual(2, channel.SendReceiveCount, "one getall per source table");
        }

        /// <summary>Counts requests and answers nothing — see WinboxReferencePrimeTests.</summary>
        private sealed class CountingChannel : IWinboxM2Channel
        {
            internal int SendReceiveCount;

            public bool IsEncrypted => true;
            public bool DataAvailable => false;
            public long BytesReceived => 0;
            public bool SupportsStaleDrain => false;
            public bool SendAbandoned => false;
            public bool SendStalled => false;
            public bool SupportsReaderLoop => false;

            public byte[] SendReceive(byte[] m2, int timeoutMs)
            {
                SendReceiveCount++;
                return null;
            }

            public void Open(string host, int port, string user, string password, int connectTimeoutMs, int ioTimeoutMs, int sendTimeoutMs = 0)
                => throw new NotSupportedException();
            public byte[] NextReqIdField() => new byte[0];
            public void Send(byte[] m2) => throw new NotSupportedException();
            public byte[] Receive(int timeoutMs) => null;
            public byte[] ReceiveNextFrame() => throw new NotSupportedException();
            public void StartIdleServicing() { }
            public void Dispose() { }
        }
    }
}
