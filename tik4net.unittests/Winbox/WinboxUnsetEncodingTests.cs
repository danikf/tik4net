// WinboxUnsetEncodingTests.cs — router-free tests for how a field is CLEARED over M2.
//
// WinBox does not clear a field by writing an empty value. webfig's unset(obj,id) deletes the field from
// the object and pushes `id2int[<prefix letter>] + <hex key>` onto Uff0014 — one u32[] listing the fields
// this write clears, with each field's ftype in bits 27+. Every type clears the same way.
//
// Before that was read off the JS, unset was implemented as "write the empty value", which is a different
// answer per type and the wrong one for most: a bool was written false (the API prints nothing), an enum
// was left alone entirely, and a number encoded to no bytes at all — which then surfaced as a client-side
// complaint that the caller had not named a field it had named.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxUnsetEncodingTests
    {
        private static readonly int[] Handler = { 12, 1 };

        // One window carrying the four shapes that answered differently: a plain string, a plain number, an
        // opt-WRAPPED bool (the wrapper has its own flag key) and an opt-wrapped one that has none.
        private const string Window =
            "[{name:'Firewall',title:'Firewall',group:'IP',c:[" +
            "{name:'Filter Rule',title:'Filter Rules',type:'map',path:[ 12,1 ],c:[" +
              "{name:'Chain',type:'string',id:'s2'}," +
              "{name:'Hits',type:'number',id:'u4a'}," +
              "{name:'Log Prefix',type:'opt',id:'b7f',c:[{type:'string',id:'s80'}]}," +
              "{name:'Client Isolation',type:'opt',showdef:1,c:[{type:'bool',id:'b91'}]}]}" +
            "]}]";

        private static WinboxFieldResolver Resolver()
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(Window), "the trimmed window must parse");
            return new WinboxFieldResolver("/ip/firewall/filter", Handler, catalog,
                                           new Dictionary<string, int>());
        }

        /// <summary>ftype in bits 27+: 0 bool, 1 u32, 4 string — id2int's own arithmetic.</summary>
        private static int Typed(int ftype, int key) => (ftype << 27) | key;

        [TestMethod]
        public void UnsetNamesAStringFieldByItsTypedId()
        {
            var ids = new List<int>();
            var extra = Resolver().EncodeUnsetField("chain", ids);

            CollectionAssert.AreEqual(new[] { Typed(4, 0x2) }, ids.ToArray());
            Assert.AreEqual(0, extra.Count, "a plain field clears through the list alone");
        }

        /// <summary>
        /// The case that used to encode to nothing: a number has no empty form, so writing one sent no
        /// bytes and the unset silently left the old value on the router.
        /// </summary>
        [TestMethod]
        public void UnsetNamesANumberFieldByItsTypedId()
        {
            var ids = new List<int>();
            Resolver().EncodeUnsetField("hits", ids);

            CollectionAssert.AreEqual(new[] { Typed(1, 0x4a) }, ids.ToArray());
        }

        /// <summary>
        /// An opt WRAPPER with its own flag key lowers the flag as well — types.opt.put with no value does
        /// both, and the two say different things: the flag says the option is absent, the list says the
        /// value underneath is gone.
        /// </summary>
        [TestMethod]
        public void UnsetOfAnOptWrappedFieldAlsoLowersTheFlag()
        {
            var ids = new List<int>();
            var extra = Resolver().EncodeUnsetField("log-prefix", ids);

            CollectionAssert.AreEqual(new[] { Typed(4, 0x80) }, ids.ToArray());

            var msg = new[] { (byte)'M', (byte)'2' }.Concat(extra.SelectMany(f => f)).ToArray();
            var fields = M2Message.ParseAllFields(msg);
            Assert.IsTrue(fields.ContainsKey(0x7f), "the wrapper's flag key must be written");
            Assert.AreEqual(false, fields[0x7f].Item2, "…and written down, not up");
        }

        /// <summary>
        /// An opt wrapper with NO id of its own — WinBox paints the default instead of a value — has no flag
        /// to lower, so the list is the entire request. This is the shape that used to write the bool false
        /// and leave the API printing a value where it should print none.
        /// </summary>
        [TestMethod]
        public void UnsetOfAFlaglessOptWrappedBoolIsTheListAlone()
        {
            var ids = new List<int>();
            var extra = Resolver().EncodeUnsetField("client-isolation", ids);

            CollectionAssert.AreEqual(new[] { Typed(0, 0x91) }, ids.ToArray());
            Assert.AreEqual(0, extra.Count);
        }

        /// <summary>A field the catalog does not know contributes no id — the caller has to report that
        /// rather than send a request that would clear nothing.</summary>
        [TestMethod]
        public void UnsetOfAnUnknownFieldContributesNothing()
        {
            var ids = new List<int>();
            var extra = Resolver().EncodeUnsetField("no-such-field", ids);

            Assert.AreEqual(0, ids.Count);
            Assert.AreEqual(0, extra.Count);
        }
    }
}
