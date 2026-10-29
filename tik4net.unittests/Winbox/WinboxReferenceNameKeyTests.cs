// WinboxReferenceNameKeyTests.cs — name → id and id → name of a referenced table read a row's name from the same keys
// (WinBox + MAC review, §5 X3).
//
// /queue/type's window has no Name box: its rows are named by 'Type Name' at 0x16, which the window declares as its
// nameval, while the resolver seeds 'name' at 0x10006 for a table addressed without an API path (a referenced table
// is). id → name (WinboxReferenceTables.RefNameKeys) tried the seeded key and then the nameval key, so /queue/simple read
// queue=pcq-upload-default/…; name → id (then WinboxIdResolver, since C15 WinboxReferenceTables) tried the seeded key only, so a set of the value it had
// just read found no id.

using System.Collections.Generic;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    [DoNotParallelize]
    public class WinboxReferenceNameKeyTests
    {
        private static readonly int[] QueueTypeHandler = { 23, 3 };

        private const string QueueTypeWindow =
            "[{name:'Queues',c:[{name:'Queue Type',title:'Queue Types',type:'map',path:[ 23,3 ],nameval:'Type Name',c:[" +
            "{name:'Type Name',type:'string',id:'s16'},{name:'Kind',type:'string',id:'s17'}]}]}]";

        private static byte[][] Row(int id, string name)
            => new[] { M2Message.U32Sys(WinboxM2Protocol.RecordKey.Id, id), M2Message.StringSys(0x16, name) };

        private static (WinboxReferenceTables ids, WinboxRecordCodec codec, WinboxJgCatalog catalog) Setup()
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(QueueTypeWindow));
            var channel = new NativeConnectionHarness.ScriptedChannel
            {
                Answer = req => NativeConnectionHarness.RecordsReply(req,
                    Row(unchecked((int)0xFFFFFFFA), "pcq-upload-default"), Row(unchecked((int)0xFFFFFFF9), "pcq-download-default")),
            };
            var ops = new WinboxNativeM2Operations(channel, 5000);
            return (new WinboxReferenceTables(ops, catalog), new WinboxRecordCodec(ops, catalog), catalog);
        }

        [TestMethod]
        public void IdToName_ReadsTheNamevalKey()
        {
            // The direction that already worked — the reference both directions must agree with.
            var (_, codec, catalog) = Setup();
            var (nameKey, nameValKey) = WinboxReferenceTables.RefNameKeys(catalog, QueueTypeHandler);
            Assert.AreEqual(0x16, nameValKey, "'Type Name' is the window's nameval");
            Assert.AreNotEqual(0x16, nameKey, "the seeded name key is not the one the rows carry");
        }

        [TestMethod]
        public void NameToId_ReadsTheNamevalKey_Blocking()
        {
            var (ids, _, _) = Setup();
            Assert.AreEqual(unchecked((int)0xFFFFFFF9), ids.ResolveReference(QueueTypeHandler, "pcq-download-default"));
        }

        [TestMethod]
        public void NameToId_ReadsTheNamevalKey_Awaited()
        {
            var (ids, _, _) = Setup();
            var resolve = ids.ResolveReferencesAsync(
                    new[] { new KeyValuePair<int[], string>(QueueTypeHandler, "pcq-upload-default") }, CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert.AreEqual(unchecked((int)0xFFFFFFFA), resolve(QueueTypeHandler, "pcq-upload-default"));
        }

        [TestMethod]
        public void NameToId_AnIdWithTheHighBitSet_IsFound()
        {
            // The built-in rows sit at the top of the u32 id range (pcq-upload-default is *FFFFFFFA), so "not found"
            // cannot be -1 or any other negative int: the lookup said "no such row" for every one of them.
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(
                "[{name:'Queues',c:[{name:'Simple Queue',title:'Simple Queues',type:'map',path:[ 23,1 ],c:[" +
                "{name:'Name',type:'string',id:'s10006'}]}]}]"));
            var channel = new NativeConnectionHarness.ScriptedChannel
            {
                Answer = req => NativeConnectionHarness.RecordsReply(req,
                    new[] { M2Message.U32Sys(WinboxM2Protocol.RecordKey.Id, unchecked((int)0xFFFFFFFF)), M2Message.StringSys(0x10006, "top") }),
            };
            var ids = new WinboxReferenceTables(new WinboxNativeM2Operations(channel, 5000), catalog);
            Assert.AreEqual(-1, ids.ResolveReference(new[] { 23, 1 }, "top"), "*FFFFFFFF is a row like any other");
            Assert.IsNull(ids.ResolveReference(new[] { 23, 1 }, "nosuch"));
        }

        [TestMethod]
        public void TheDecoderAndTheEncoder_ShareOneMemberMap_AndAWriteForgetsOnlyTheNames()
        {
            // C15: one service, one member-map cache for both directions. A member map is the router's fixed
            // vocabulary and is kept for the connection; an id → name map is dropped by every write.
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(QueueTypeWindow));
            int getalls = 0;
            var channel = new NativeConnectionHarness.ScriptedChannel
            {
                Answer = req =>
                {
                    getalls++;
                    return NativeConnectionHarness.RecordsReply(req, Row(1, "pcq-upload-default"), Row(2, "pcq-download-default"));
                },
            };
            var refs = new WinboxReferenceTables(new WinboxNativeM2Operations(channel, 5000), catalog);

            Assert.AreEqual("pcq-upload-default", refs.ReadMemberTable(QueueTypeHandler)![1], "the encoder's read");
            Assert.AreEqual("pcq-download-default", refs.MembersOf(QueueTypeHandler, null)![2], "the decoder's read");
            Assert.AreEqual(1, getalls, "one member map for both directions");

            Assert.AreEqual("pcq-upload-default", refs.NameOf(QueueTypeHandler, 1, null));
            Assert.AreEqual(2, getalls);
            refs.Forget();
            Assert.AreEqual("pcq-download-default", refs.NameOf(QueueTypeHandler, 2, null));
            Assert.AreEqual(3, getalls, "a write drops the names");
            Assert.IsNotNull(refs.MembersOf(QueueTypeHandler, null));
            Assert.AreEqual(3, getalls, "and keeps the members");
        }
    }
}
