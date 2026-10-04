#nullable enable
// TikValueListMapperTests.cs — a TikField<TikValueList<T>?> property through the O/R mapper: what a load reads and a save
// sends. The wire forms are the ones the router printed on 6.49.13 and 7.24.5 (2026-10-04, API, REST and CLI alike):
//   dst-port=8291,22,1000-2000,80     src-port=!443,53          (a whole-list '!' before the first item)
//   hotspot=from-client,http,!auth    topics=system,!debug      (a '!' per member)
//   tcp-flags=!,ack,!syn                                          (both: the whole-list '!' is a bare leading element)

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    [TestClass]
    public class TikValueListMapperTests
    {
        public enum Flag { [TikEnum("fin")] Fin, [TikEnum("syn")] Syn, [TikEnum("rst")] Rst, [TikEnum("ack")] Ack }

        public enum Hotspot { [TikEnum("from-client")] FromClient, [TikEnum("auth")] Auth, [TikEnum("http")] Http }

        [TikEntity("/rule")]
        public class Rule
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; private set; }

            [TikProperty("dst-port", Negatable = true)]
            public TikField<TikValueList<TikPortRange>?> DstPort { get; set; }

            [TikProperty("hotspot", NegatableMembers = true)]
            public TikField<TikValueList<Hotspot>?> Hotspot { get; set; }

            [TikProperty("tcp-flags", Negatable = true, NegatableMembers = true, SetKeepsUnnamedHalf = true)]
            public TikField<TikValueList<Flag>?> TcpFlags { get; set; }

            [TikProperty("names")]
            public TikField<TikValueList<string>?> Names { get; set; }
        }

        private static TikFakeConnection Router(params (string, string)[] fields)
        {
            var row = new Dictionary<string, string> { [".id"] = "*1" };
            foreach (var (k, v) in fields) row[k] = v;
            return new TikFakeConnection()
                .WithResponse(cmd => cmd.FirstOrDefault() == "/rule/print",
                    _ => new ITikSentence[] { new TikFakeReSentence(row), new TikFakeDoneSentence() })
                .WithNonQuery(cmd => cmd.First() == "/rule/set" || cmd.First() == "/rule/unset")
                .WithScalarResponse(cmd => cmd.First() == "/rule/add", "*9");
        }

        private static string[] Sent(TikFakeConnection c, string verb)
            => c.SentCommands.Where(x => x.First() == "/rule/" + verb).SelectMany(x => x.Skip(1)).ToArray();

        private static Rule Load(params (string, string)[] fields) => Router(fields).LoadAll<Rule>().Single();

        // ── Read ─────────────────────────────────────────────────────────────

        [TestMethod]
        public void APortList_ReadsItsItems_InTheOrderPrinted()
        {
            var rule = Load(("dst-port", "8291,22,1000-2000,80"));

            Assert.AreEqual(TikFieldState.Present, rule.DstPort.State);
            Assert.IsFalse(rule.DstPort.IsNegated);
            CollectionAssert.AreEqual(new[] { "8291", "22", "1000-2000", "80" }, rule.DstPort.Value!.Select(i => i.ToString()).ToArray());
            Assert.AreEqual(new TikPortRange(1000, 2000), rule.DstPort.Value![2].Value);
        }

        [TestMethod]
        public void AWholeListBang_IsTheFieldsNegation()
        {
            var rule = Load(("dst-port", "!443,53"));

            Assert.IsTrue(rule.DstPort.IsNegated);
            Assert.AreEqual("443,53", rule.DstPort.Value!.ToString());
            Assert.IsFalse(rule.DstPort.Value!.HasNegatedItems);
        }

        [TestMethod]
        public void MemberBangs_AreTheItemsNegations()
        {
            var rule = Load(("hotspot", "from-client,http,!auth"));

            Assert.IsFalse(rule.Hotspot.IsNegated);
            var auth = rule.Hotspot.Value!.Single(i => i.Value == Hotspot.Auth);
            Assert.IsTrue(auth.IsNegated);
            Assert.IsFalse(rule.Hotspot.Value!.First().IsNegated);
        }

        [TestMethod]
        public void TcpFlags_ABareLeadingBang_IsTheWholeList_ThenEachMembersOwn()
        {
            var rule = Load(("tcp-flags", "!,ack,!syn"));

            Assert.IsTrue(rule.TcpFlags.IsNegated);
            Assert.AreEqual(2, rule.TcpFlags.Value!.Count);
            Assert.AreEqual("ack,!syn", rule.TcpFlags.Value!.ToString());
        }

        [TestMethod]
        public void AMemberTheEnumLacks_IsKeptAsAWord_WithItsBang_AndTheRestKeepsItsType()
        {
            var rule = Load(("hotspot", "http,!newer-kind"));

            Assert.AreEqual(TikFieldState.Present, rule.Hotspot.State, "a list is never Unparsed as a whole");
            Assert.AreEqual(Hotspot.Http, rule.Hotspot.Value![0].Value);
            Assert.IsTrue(rule.Hotspot.Value![1].IsWord);
            Assert.IsTrue(rule.Hotspot.Value![1].IsNegated);
            Assert.AreEqual("newer-kind", rule.Hotspot.Value![1].RawValue);
        }

        [TestMethod]
        public void OnAListWithoutNegatableMembers_ABangInsideIsPartOfAWord()
        {
            var rule = Load(("dst-port", "22,!80"));

            Assert.IsFalse(rule.DstPort.IsNegated);
            Assert.IsTrue(rule.DstPort.Value![1].IsWord);
            Assert.AreEqual("!80", rule.DstPort.Value![1].RawValue);
        }

        [TestMethod]
        public void AnEmptyValue_IsAnEmptyList_AndAMissingOne_IsAbsent()
        {
            var rule = Load(("names", ""));
            Assert.AreEqual(0, rule.Names.Value!.Count);
            Assert.AreEqual(TikFieldState.Absent, rule.DstPort.State);
        }

        // ── Write ────────────────────────────────────────────────────────────

        [TestMethod]
        public void AnUnchangedList_SendsNothing_EvenInTheRoutersOwnOrder()
        {
            var connection = Router(("dst-port", "!443,53"), ("tcp-flags", "!,ack,!syn"), ("hotspot", "from-client,http,!auth"));
            var rule = connection.LoadAll<Rule>().Single();

            connection.Save(rule);

            Assert.AreEqual(0, Sent(connection, "set").Length);
        }

        [TestMethod]
        public void AnEditedList_IsSent_InTheListsOrder()
        {
            var connection = Router(("dst-port", "22,8291"));
            var rule = connection.LoadAll<Rule>().Single();

            rule.DstPort = rule.DstPort.Value!.With(new TikPortRange(1000, 2000));
            connection.Save(rule);

            CollectionAssert.AreEquivalent(new[] { "=dst-port=22,8291,1000-2000", "=.id=*1" }, Sent(connection, "set"));
        }

        [TestMethod]
        public void TheNegationsAreWritten_AsTheRouterSpellsThem()
        {
            var connection = Router();
            var rule = new Rule
            {
                DstPort = TikValue<TikValueList<TikPortRange>?>.Not(new TikValueList<TikPortRange>(443, 53)),
                Hotspot = new TikValueList<Hotspot>(TikValue<Hotspot>.Not(Hotspot.FromClient), Hotspot.Http),
                TcpFlags = TikValue<TikValueList<Flag>?>.Not(new TikValueList<Flag>(TikValue<Flag>.Not(Flag.Syn), Flag.Ack)),
            };

            connection.Save(rule);

            CollectionAssert.IsSubsetOf(new[] { "=dst-port=!443,53", "=hotspot=!from-client,http", "=tcp-flags=!,!syn,ack" },
                Sent(connection, "add"));
        }

        [TestMethod]
        public void AWordItem_IsWrittenBackAsTheRouterPrintedIt()
        {
            var connection = Router(("hotspot", "http,!newer-kind"));
            var rule = connection.LoadAll<Rule>().Single();

            rule.Hotspot = rule.Hotspot.Value!.With(Hotspot.Auth);
            connection.Save(rule);

            CollectionAssert.Contains(Sent(connection, "set"), "=hotspot=http,!newer-kind,auth");
        }

        [TestMethod]
        public void ANegatedItem_OnAFieldThatDoesNotNegateMembers_IsRefusedBeforeSending()
        {
            var connection = Router(("dst-port", "22"));
            var rule = connection.LoadAll<Rule>().Single();

            rule.DstPort = new TikValueList<TikPortRange>(TikValue<TikPortRange>.Not(80));
            var thrown = Assert.ThrowsException<InvalidOperationException>(() => connection.Save(rule));
            StringAssert.Contains(thrown.Message, "NegatableMembers");
            Assert.AreEqual(0, Sent(connection, "set").Length);
        }

        [TestMethod]
        public void ANegatedEmptyList_IsRefusedBeforeSending()
        {
            var connection = Router();
            var rule = new Rule { DstPort = TikValue<TikValueList<TikPortRange>?>.Not(TikValueList<TikPortRange>.Empty) };

            Assert.ThrowsException<InvalidOperationException>(() => connection.Save(rule));
            Assert.AreEqual(0, Sent(connection, "add").Length);
        }

        [TestMethod]
        public void AssigningNull_UnsetsTheList()
        {
            var connection = Router(("dst-port", "22"));
            var rule = connection.LoadAll<Rule>().Single();

            rule.DstPort = null;
            connection.Save(rule);

            CollectionAssert.Contains(Sent(connection, "unset"), "=value-name=dst-port");
        }

        // ── tcp-flags: a text set replaces only the half it names ────────────
        // Measured 2026-10-04 (API, REST, CLI; 6.49.13 and 7.24.5): `!,syn,!ack` + `set rst` → `rst,!ack`.

        [TestMethod]
        public void ASetThatWouldKeepTheNegatedHalf_IsRefusedBeforeSending()
        {
            var connection = Router(("tcp-flags", "!,syn,!ack"));
            var rule = connection.LoadAll<Rule>().Single();

            rule.TcpFlags = new TikValueList<Flag>(Flag.Rst);
            var thrown = Assert.ThrowsException<InvalidOperationException>(() => connection.Save(rule));

            StringAssert.Contains(thrown.Message, "negated members");
            Assert.AreEqual(0, Sent(connection, "set").Length);
        }

        [TestMethod]
        public void ASetThatWouldKeepThePlainHalf_IsRefusedBeforeSending()
        {
            var connection = Router(("tcp-flags", "fin,ack,!syn"));
            var rule = connection.LoadAll<Rule>().Single();

            rule.TcpFlags = new TikValueList<Flag>(TikValue<Flag>.Not(Flag.Rst));
            var thrown = Assert.ThrowsException<InvalidOperationException>(() => connection.Save(rule));

            StringAssert.Contains(thrown.Message, "plain members");
        }

        [TestMethod]
        public void ASetNamingBothHalves_IsSent()
        {
            var connection = Router(("tcp-flags", "fin,ack,!syn"));
            var rule = connection.LoadAll<Rule>().Single();

            rule.TcpFlags = new TikValueList<Flag>(Flag.Rst, TikValue<Flag>.Not(Flag.Fin));
            connection.Save(rule);

            CollectionAssert.Contains(Sent(connection, "set"), "=tcp-flags=rst,!fin");
        }

        [TestMethod]
        public void ASetOnARowThatHadNoTcpFlags_IsSent()
        {
            var connection = Router();
            var rule = connection.LoadAll<Rule>().Single();

            rule.TcpFlags = new TikValueList<Flag>(Flag.Syn);
            connection.Save(rule);

            CollectionAssert.Contains(Sent(connection, "set"), "=tcp-flags=syn");
        }

        [TestMethod]
        public void OverStructuredWrites_TheOneKindSetIsSent()
        {
            var connection = Router(("tcp-flags", "!,syn,!ack"));
            connection.Capabilities |= TikConnectionCapability.StructuredWrites;
            var rule = connection.LoadAll<Rule>().Single();

            rule.TcpFlags = new TikValueList<Flag>(Flag.Rst);
            connection.Save(rule);

            CollectionAssert.Contains(Sent(connection, "set"), "=tcp-flags=rst");
        }

        [TestMethod]
        public void AnAddIsAlwaysExact()
        {
            var connection = Router();
            connection.Save(new Rule { TcpFlags = new TikValueList<Flag>(Flag.Syn) });

            CollectionAssert.Contains(Sent(connection, "add"), "=tcp-flags=syn");
        }

        // ── Declaration ──────────────────────────────────────────────────────

        [TikEntity("/bad")]
        public class MembersOnAScalar
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; private set; }

            [TikProperty("x", NegatableMembers = true)]
            public TikField<string?> X { get; set; }
        }

        [TikEntity("/bad2")]
        public class NullableItems
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; private set; }

            [TikProperty("x")]
            public TikField<TikValueList<int?>?> X { get; set; }
        }

        [TestMethod]
        public void NegatableMembers_OnAScalar_IsRefused()
            => AssertRefused<MembersOnAScalar>("NegatableMembers needs a TikField<TikValueList<T>?>");

        [TikEntity("/bad3")]
        public class HalvesWithoutMembers
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; private set; }

            [TikProperty("x", SetKeepsUnnamedHalf = true)]
            public TikField<TikValueList<int>?> X { get; set; }
        }

        [TestMethod]
        public void SetKeepsUnnamedHalf_WithoutNegatableMembers_IsRefused()
            => AssertRefused<HalvesWithoutMembers>("declare NegatableMembers");

        [TestMethod]
        public void NullableItems_AreRefused()
            => AssertRefused<NullableItems>("never null");

        private static void AssertRefused<T>(string message) where T : new()
        {
            var thrown = Assert.ThrowsException<ArgumentException>(() =>
            {
                try { TikEntityMetadataCache.GetMetadata<T>(); }
                catch (TypeInitializationException e) when (e.InnerException != null) { throw e.InnerException; }
                catch (System.Reflection.TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
            });
            StringAssert.Contains(thrown.Message, message);
        }
    }
}
