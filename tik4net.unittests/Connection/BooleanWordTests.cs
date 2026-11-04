#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Connection;
using tik4net.Objects;
using tik4net.Objects.Ip;
using tik4net.Testing;
using tik4net.Winbox;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// What each place that reads or writes the RouterOS boolean word makes of the same spellings: the mapper, the
    /// connection string, WinBox native's encoder, the CLI where-builder and presence flags, the list sync's dynamic
    /// check, the listen's <c>.dead</c> and the fake connection's filter.
    /// </summary>
    [TestClass]
    public class BooleanWordTests
    {
        // ── The mapper's bool type ────────────────────────────────────────────

        [DataTestMethod]
        [DataRow("true", true)]
        [DataRow("yes", true)]
        [DataRow("TRUE", true)]
        [DataRow("Yes", true)]
        [DataRow("false", false)]
        [DataRow("no", false)]
        [DataRow("NO", false)]
        public void TheMapper_ReadsTheFourWordsInAnyCase(string word, bool expected)
        {
            Assert.AreEqual(expected, TikWireTypes.Find(typeof(bool))!.Parse(word));
        }

        [DataTestMethod]
        [DataRow("1")]
        [DataRow("")]
        [DataRow("on")]
        public void TheMapper_RefusesAnyOtherWord(string word)
        {
            Assert.ThrowsException<FormatException>(() => TikWireTypes.Find(typeof(bool))!.Parse(word));
        }

        [TestMethod]
        public void TheMapper_WritesYesOrNo()
        {
            Assert.AreEqual("yes", TikWireTypes.Find(typeof(bool))!.Format(true));
            Assert.AreEqual("no", TikWireTypes.Find(typeof(bool))!.Format(false));
        }

        // ── The connection string ─────────────────────────────────────────────

        [DataTestMethod]
        [DataRow("true", true)]
        [DataRow("yes", true)]
        [DataRow("TRUE", true)]
        [DataRow("false", false)]
        [DataRow("No", false)]
        public void TheConnectionString_ReadsTheFourWordsInAnyCase(string word, bool expected)
        {
            Assert.AreEqual(expected, TikConnectionSetup.FromConnectionString("host=192.0.2.1;user=admin;debug=" + word).DebugEnabled);
        }

        [TestMethod]
        public void TheConnectionString_NamesTheKeyOfAnyOtherWord()
        {
            var ex = Assert.ThrowsException<ArgumentException>(() => TikConnectionSetup.FromConnectionString("host=192.0.2.1;user=admin;debug=1"));
            StringAssert.Contains(ex.Message, "debug=1");
        }

        // ── WinBox native: a bool field's encoding ────────────────────────────

        private const string BoolWindow =
            "[{name:'X',c:[{title:'X',type:'map',path:[ 90,1 ],c:[{name:'Invert',type:'bool',id:'bcc'}]}]}]";

        [DataTestMethod]
        [DataRow("true", true)]
        [DataRow("yes", true)]
        [DataRow("1", true)]          // a WinBox wire fact, not a RouterOS spelling (D3)
        [DataRow("TRUE", true)]
        [DataRow("Yes", true)]
        [DataRow("false", false)]
        [DataRow("no", false)]
        [DataRow("0", false)]
        [DataRow("on", false)]
        public void WinboxNative_EncodesTrueOnlyForTrueYesOrOne(string word, bool expected)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(BoolWindow), "the trimmed window must parse");
            var resolver = new WinboxFieldResolver("/x", new[] { 90, 1 }, catalog, new Dictionary<string, int>());

            var encoded = resolver.EncodeField("invert", word);
            var msg = new[] { (byte)'M', (byte)'2' }.Concat(encoded.Last()).ToArray();
            Assert.AreEqual(expected, M2Message.ParseAllFields(msg).Single().Value.Value);
        }

        // ── The CLI: a where condition, and a presence flag ───────────────────

        [DataTestMethod]
        [DataRow("true", "yes")]
        [DataRow("TRUE", "yes")]
        [DataRow("false", "no")]
        [DataRow("False", "no")]
        [DataRow("yes", "yes")]
        [DataRow("Yes", "Yes")]       // a yes/no word is left as written: a string field may hold it
        [DataRow("no", "no")]
        [DataRow("ether", "ether")]
        public void TheCliWhere_SpellsTrueAndFalseAsYesAndNo(string word, string expected)
        {
            string where = CliCommandBuilder.BuildWhereClause(new List<ITikCommandParameter>
                { new TikCommandParameter("dynamic", word, TikCommandParameterFormat.Filter) });
            Assert.AreEqual("dynamic=" + expected, where);
        }

        [DataTestMethod]
        [DataRow("yes", true)]
        [DataRow("true", true)]
        [DataRow("YES", true)]
        [DataRow("True", true)]
        [DataRow("no", false)]
        [DataRow("false", false)]
        [DataRow("1", false)]
        public void TheCliPresenceFlag_IsWrittenForTrueOrYesInAnyCase(string word, bool written)
        {
            string add = CliCommandBuilder.BuildAdd("/routing/table/add", new List<ITikCommandParameter>
            {
                new TikCommandParameter("name", "t1", TikCommandParameterFormat.NameValue),
                new TikCommandParameter("fib", word, TikCommandParameterFormat.NameValue),
            });
            Assert.AreEqual(written, add.Split(' ', ']').Contains("fib"), add);
        }

        // ── The list sync's dynamic row ───────────────────────────────────────

        [DataTestMethod]
        [DataRow("true", true)]
        [DataRow("false", false)]
        public void TheListSync_SkipsADynamicRow(string word, bool dynamic)
        {
            var conn = new TikFakeConnection().WithResponse(rows => rows.First().EndsWith("/print", StringComparison.Ordinal),
                new ITikSentence[]
                {
                    new TikFakeReSentence(new Dictionary<string, string> { [".id"] = "*1", ["dynamic"] = word }),
                    new TikFakeDoneSentence(),
                });
            var row = conn.LoadAll<IpAddress>().Single();

            Assert.AreEqual(dynamic, TikListSync.IsDynamic(TikEntityMetadataCache.GetMetadata<IpAddress>(), row));
        }

        [TestMethod]
        public void TheListSync_ReadsAnAbsentDynamicAsStatic()
        {
            Assert.IsFalse(TikListSync.IsDynamic(TikEntityMetadataCache.GetMetadata<IpAddress>(), new IpAddress()));
        }

        // ── The listen's .dead ────────────────────────────────────────────────

        [DataTestMethod]
        [DataRow("true", true)]
        [DataRow("yes", true)]
        [DataRow("TRUE", true)]       // was false: the only case-sensitive reader of the word (TikBoolText)
        [DataRow("Yes", true)]        // was false
        [DataRow("false", false)]
        [DataRow(null, false)]
        public void TheListen_ReportsADeadRow(string? dead, bool deleted)
        {
            var words = new Dictionary<string, string> { [".id"] = "*1", ["address"] = "192.0.2.1/24" };
            if (dead != null)
                words[".dead"] = dead;
            var conn = new TikFakeConnection().WithResponse(rows => rows.First().EndsWith("/listen", StringComparison.Ordinal),
                new ITikSentence[] { new TikFakeReSentence(words) });

            string? deletedId = null;
            IpAddress? changed = null;
            var got = new ManualResetEventSlim(false);
            var cmd = conn.LoadListenWithCallback<IpAddress>(
                row => { changed = row; got.Set(); },
                id => { deletedId = id; got.Set(); });
            Assert.IsTrue(got.Wait(TimeSpan.FromSeconds(10)), "the row reached no callback");
            cmd.CancelAndJoin();

            Assert.AreEqual(deleted ? "*1" : null, deletedId);
            Assert.AreEqual(!deleted, changed != null);
        }

        // ── The fake connection's filter ──────────────────────────────────────

        [DataTestMethod]
        [DataRow("true", "*2")]
        [DataRow("yes", "*2")]
        [DataRow("false", "*1")]
        [DataRow("no", "*1")]
        [DataRow("TRUE", "*2")]       // was no row: the fake compared the word case-sensitively (TikBoolText)
        [DataRow("No", "*1")]         // was no row
        public void TheFake_MatchesABooleanInEitherSpelling(string word, string ids)
        {
            var conn = new TikFakeConnection().WithEntities(
                new IpAddress { Address = "10.0.0.1/24", Interface = "ether1", Disabled = false }.WithId("*1"),
                new IpAddress { Address = "10.0.0.2/24", Interface = "ether2", Disabled = true }.WithId("*2"));

            var rows = conn.LoadList<IpAddress>(conn.CreateParameter("disabled", word));

            Assert.AreEqual(ids, string.Join(",", rows.Select(r => r.Id)));
        }
    }
}
