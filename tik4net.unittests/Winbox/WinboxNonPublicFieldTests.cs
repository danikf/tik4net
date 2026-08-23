// WinboxNonPublicFieldTests.cs — a name two declarations of one window claim belongs to the visible one.
//
// /file's window [72] declares BOTH {name:'type',id:'u3',nonpublic:1} — the numeric file kind — and
// {name:'Type',id:'s7',ro:1}, the text RouterOS prints. The router sends both on every row (verified on
// 7.24: 0x3=5 and 0x7=directory side by side in one record), and registration was first-wins on the name,
// so the numeric one took 'type' and /file reported type=5 where the API says type=directory.
//
// `nonpublic:1` is WinBox saying it never paints the field. That is what tells the two apart.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxNonPublicFieldTests
    {
        private static readonly int[] Handler = { 72 };

        // The Files window, cut to the collision and to one internal-only field that must survive it.
        private const string Window =
            "[{name:'Files',title:'Files',c:[" +
            "{name:'File',title:'File',type:'map',path:[ 72 ],nameval:'File Name',c:[" +
              "{name:'container',type:'number',id:'u5',nonpublic:1}," +
              "{name:'type',type:'number',id:'u3',nonpublic:1}," +
              "{name:'File Name',title:'Name',type:'string',id:'s1',width:200}," +
              "{name:'Type',type:'string',id:'s7',ro:1,width:100}," +
              "{name:'Size',type:'number',id:'ue',ro:1}]}]}]";

        private static WinboxFieldResolver Resolver()
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(Window), "the trimmed Files window must parse");
            return new WinboxFieldResolver("/file", Handler, catalog, new Dictionary<string, int>());
        }

        private static Dictionary<string, string> Decode(params (int key, string type, object val)[] fields)
        {
            var resolver = Resolver();
            var rec = new Dictionary<int, Tuple<string, object>>();
            foreach (var f in fields) rec[f.key] = Tuple.Create(f.type, f.val);
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(Window));
            return new WinboxRecordCodec(null, catalog)
                .DecodeRecord(rec, resolver.BuildKeyToApiName(), resolver.BuildKeyToField());
        }

        [TestMethod]
        public void TheVisibleFieldKeepsTheNameTheInvisibleOneAlsoClaims()
        {
            var fields = Decode((0x3, "u32", 5U), (0x7, "str", "directory"));

            Assert.AreEqual("directory", fields["type"],
                "'type' is the text WinBox shows, not the internal number beside it");
        }

        [TestMethod]
        public void TheInvisibleOneIsNotReportedUnderAnotherNameEither()
        {
            var fields = Decode((0x3, "u32", 5U), (0x7, "str", "directory"));

            foreach (var kv in fields)
                Assert.AreNotEqual("5", kv.Value,
                    $"the numeric kind resurfaced as '{kv.Key}'; a name it loses is not a name it gets renamed to");
        }

        [TestMethod]
        public void AnInvisibleFieldNoOneContestsStillAnswers()
        {
            // The point of demoting rather than dropping: /file's own container/family/basename are all
            // nonpublic and are the only declaration of their key.
            var fields = Decode((0x5, "u32", 134348884U));

            Assert.AreEqual("134348884", fields["container"]);
        }

        [TestMethod]
        public void TheVisibleFieldWinsWhicheverOrderTheRouterSends()
        {
            var forward = Decode((0x3, "u32", 0U), (0x7, "str", ".js file"));
            var reverse = Decode((0x7, "str", ".js file"), (0x3, "u32", 0U));

            Assert.AreEqual(".js file", forward["type"]);
            Assert.AreEqual(forward["type"], reverse["type"]);
        }

        [TestMethod]
        public void AWriteStillAddressesTheVisibleField()
        {
            // Not writable here (ro:1), and that is the answer — the important half is that the write side
            // does not quietly fall back to the numeric slot the read side stopped using.
            Assert.AreEqual(0, Resolver().EncodeField("type", "directory").Count);
        }
    }
}
