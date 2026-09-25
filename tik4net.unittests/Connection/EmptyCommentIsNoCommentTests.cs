// EmptyCommentIsNoCommentTests.cs — an empty comment is no comment, on every transport.
//
// RouterOS has no "empty comment": `set comment=""` clears it, and the binary API and REST then leave the word out of
// the row, as for a row that never had one. Two transports deliver it anyway, empty — the RouterOS 6 CLI
// (6.49.13 prints `comment=;` for a row with none, 7.24.4 prints nothing) and WinBox native on both versions (the
// M2 record carries the comment key with an empty string). Read as Present("") there and absent over the API, the
// same row was two different states depending on the transport (V2/V4 validation of the 5.0 entity value model).

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Winbox;

namespace tik4net.unittests.Connection
{
    [TestClass]
    public class EmptyCommentIsNoCommentTests
    {
        [TestMethod]
        public void Cli_AnEmptyCommentIsLeftOut()
        {
            var row = CliOutputParser.ParseAsValue(".id=*2000012;comment=;dynamic=false;exclude=;include=;name=t4n").Single();

            Assert.IsNull(row.GetResponseFieldOrDefault("comment", null));
            Assert.AreEqual("", row.GetResponseFieldOrDefault("include", null), "other empty fields are values, as on the API");
        }

        [TestMethod]
        public void Cli_ACommentWithTextIsKept()
        {
            var row = CliOutputParser.ParseAsValue(".id=*1;comment=contains all interfaces;name=all").Single();

            Assert.AreEqual("contains all interfaces", row.GetResponseFieldOrDefault("comment", null));
        }

        private const string ListWindow = "[{name:'Interface List',type:'map',path:[ 24,1 ],c:[{name:'Name',type:'string',id:'sfe0010'}]}]";

        private static Dictionary<string, string> NativeDecode(string comment)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(ListWindow));
            var resolver = new WinboxFieldResolver(null, new[] { 24, 1 }, catalog, new Dictionary<string, int>());
            var rec = new Dictionary<int, Tuple<string, object>>
            {
                [0xFE0010] = Tuple.Create("str", (object)"t4n"),
                [0xFE0009] = Tuple.Create("str", (object)comment),
            };
            return new WinboxRecordCodec(null, catalog).DecodeRecord(rec, resolver.BuildKeyToApiName(), resolver.BuildKeyToField());
        }

        [TestMethod]
        public void WinboxNative_AnEmptyCommentIsLeftOut()
        {
            var fields = NativeDecode("");

            Assert.IsFalse(fields.ContainsKey("comment"), string.Join(";", fields.Select(kv => kv.Key + "=" + kv.Value)));
            Assert.AreEqual("t4n", fields["name"]);
        }

        [TestMethod]
        public void WinboxNative_ACommentWithTextIsKept()
        {
            Assert.AreEqual("hello", NativeDecode("hello")["comment"]);
        }
    }
}
