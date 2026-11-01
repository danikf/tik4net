#nullable enable
// TikListMergeSemanticsTests.cs — what a merge compares by, what it returns, and what it refuses when it is declared.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    [TestClass]
    public class TikListMergeSemanticsTests
    {
        [TikEntity("/box")]
        public class Row
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; private set; }

            [TikProperty("name")]
            public TikField<string?> Name { get; set; }

            [TikProperty("comment")]
            public TikField<string?> Comment { get; set; }

            [TikProperty("servers")]
            public TikField<TikValueList<string>?> Servers { get; set; }

            [TikProperty("state")]
            public TikNegatableField<TikValueList<FirewallConnectionState>?> State { get; set; }

            [TikProperty("flags")]
            public TikField<TikValueList<TikNegatableValue<FirewallTcpFlag>>?> Flags { get; set; }

            [TikProperty("timeout")]
            public TikField<TikDuration?> Timeout { get; set; }

            [TikProperty("dynamic", IsReadOnly = true)]
            public TikField<bool?> Dynamic { get; private set; }

            public string? NotMapped { get; set; }
        }

        [TikEntity("/box", IsOrdered = true)]
        public class OrderedRow
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; private set; }

            [TikProperty("name")]
            public TikField<string?> Name { get; set; }
        }

        private static TikFakeConnection Router(params Dictionary<string, string>[] rows)
            => new TikFakeConnection()
                .WithResponse(cmd => cmd.FirstOrDefault() == "/box/print",
                    _ => rows.Select(r => (ITikSentence)new TikFakeReSentence(r)).Concat(new[] { (ITikSentence)new TikFakeDoneSentence() }).ToArray())
                .WithNonQuery(cmd => cmd.First() == "/box/set" || cmd.First() == "/box/unset" || cmd.First() == "/box/remove")
                .WithScalarResponse(cmd => cmd.First() == "/box/add", "*9");

        private static Dictionary<string, string> Printed(string id, string name, params (string, string)[] fields)
        {
            var row = new Dictionary<string, string> { [".id"] = id, ["name"] = name };
            foreach (var (field, value) in fields)
                row[field] = value;
            return row;
        }

        private static bool Wrote(TikFakeConnection connection)
            => connection.SentCommands.Any(c => c.First() == "/box/set" || c.First() == "/box/unset");

        private static TikListMerge<Row> Merge(TikFakeConnection connection, IEnumerable<Row> expected, IEnumerable<Row> original)
            => connection.CreateMerge(expected, original).WithKey(r => r.Name.ToString());

        private static string[] Names(IEnumerable<Row> rows) => rows.Select(r => r.Name.ToString()).ToArray();

        // --- Which lists a merge compares in order ---

        [TestMethod]
        public void AListOfEnumMembers_InTheRoutersOrder_IsNotAChange()
        {
            // The router keeps these as a bitmask and prints its own order, so a comparison in order never settles.
            var connection = Router(Printed("*1", "x", ("state", "established,related")));
            var original = connection.LoadAll<Row>().ToList();
            var expected = new Row { Name = "x", State = new TikValueList<FirewallConnectionState>(FirewallConnectionState.Related, FirewallConnectionState.Established) };

            Merge(connection, new[] { expected }, original).Field(r => r.State).Save();

            Assert.IsFalse(Wrote(connection));
        }

        [TestMethod]
        public void AListOfNegatableMembers_InTheRoutersOrder_IsNotAChange()
        {
            var connection = Router(Printed("*1", "x", ("flags", "syn,!ack")));
            var original = connection.LoadAll<Row>().ToList();
            var expected = new Row { Name = "x", Flags = TikValueList<TikNegatableValue<FirewallTcpFlag>>.Parse("!ack,syn") };

            Merge(connection, new[] { expected }, original).Field(r => r.Flags).Save();

            Assert.IsFalse(Wrote(connection));
        }

        [TestMethod]
        public void AMemberThatChangedItsNegation_IsAChange()
        {
            var connection = Router(Printed("*1", "x", ("flags", "syn,!ack")));
            var original = connection.LoadAll<Row>().ToList();
            var expected = new Row { Name = "x", Flags = TikValueList<TikNegatableValue<FirewallTcpFlag>>.Parse("syn,ack") };

            Merge(connection, new[] { expected }, original).Field(r => r.Flags).Save();

            Assert.IsTrue(Wrote(connection));
        }

        [TestMethod]
        public void AWholeListNegation_IsAChange_InAnyOrder()
        {
            var connection = Router(Printed("*1", "x", ("state", "established,related")));
            var original = connection.LoadAll<Row>().ToList();
            var expected = new Row { Name = "x", State = TikNegatableField<TikValueList<FirewallConnectionState>?>.Not(
                new TikValueList<FirewallConnectionState>(FirewallConnectionState.Related, FirewallConnectionState.Established)) };

            Merge(connection, new[] { expected }, original).Field(r => r.State).Save();

            Assert.IsTrue(Wrote(connection));
        }

        [TestMethod]
        public void AnEnumListWithAnotherMember_IsAChange()
        {
            var connection = Router(Printed("*1", "x", ("state", "established,related")));
            var original = connection.LoadAll<Row>().ToList();
            var expected = new Row { Name = "x", State = new TikValueList<FirewallConnectionState>(FirewallConnectionState.Established) };

            Merge(connection, new[] { expected }, original).Field(r => r.State).Save();

            Assert.IsTrue(connection.SentCommands.Any(c => c.First() == "/box/set" && c.Contains("=state=established")));
        }

        [TestMethod]
        public void AListOfAddresses_InAnotherOrder_IsSent()
        {
            // The first DNS server a DHCP client gets is its primary: this order is configuration.
            var connection = Router(Printed("*1", "x", ("servers", "1.1.1.1,8.8.8.8")));
            var original = connection.LoadAll<Row>().ToList();
            var expected = new Row { Name = "x", Servers = new TikValueList<string>("8.8.8.8", "1.1.1.1") };

            Merge(connection, new[] { expected }, original).Field(r => r.Servers).Save();

            Assert.IsTrue(connection.SentCommands.Any(c => c.First() == "/box/set" && c.Contains("=servers=8.8.8.8,1.1.1.1")));
        }

        [TestMethod]
        public void AListOfAddresses_InTheSameOrder_IsNotAChange()
        {
            var connection = Router(Printed("*1", "x", ("servers", "1.1.1.1,8.8.8.8")));
            var original = connection.LoadAll<Row>().ToList();
            var expected = new Row { Name = "x", Servers = new TikValueList<string>("1.1.1.1", "8.8.8.8") };

            Merge(connection, new[] { expected }, original).Field(r => r.Servers).Save();

            Assert.IsFalse(Wrote(connection));
        }

        [TestMethod]
        public void AFieldMerge_ThatReordersAddresses_IsSent()
        {
            var connection = Router(Printed("*1", "x", ("servers", "1.1.1.1,8.8.8.8")));
            var original = connection.LoadAll<Row>().ToList();

            Merge(connection, new[] { new Row { Name = "x" } }, original)
                .Field(r => r.Servers, (expected, current) => new TikValueList<string>("8.8.8.8", "1.1.1.1"))
                .Save();

            Assert.IsTrue(connection.SentCommands.Any(c => c.First() == "/box/set" && c.Contains("=servers=8.8.8.8,1.1.1.1")));
        }

        [TestMethod]
        public void AnEmptyEnumList_AndNoValue_AreDifferent()
        {
            var connection = Router(Printed("*1", "x", ("state", "")));
            var original = connection.LoadAll<Row>().ToList();

            Merge(connection, new[] { new Row { Name = "x" } }, original).Field(r => r.State).Save();

            Assert.IsTrue(Wrote(connection));
        }

        [TestMethod]
        public void AValueTheTypeCannotHold_ComparedToTheSameWord_IsNotAChange()
        {
            var connection = Router(Printed("*1", "x", ("timeout", "10s")));
            var original = connection.LoadAll<Row>().ToList();
            var expected = new Row { Name = "x", Timeout = TikField<TikDuration?>.FromWire("10s") };

            Merge(connection, new[] { expected }, original).Field(r => r.Timeout).Save();

            Assert.IsFalse(Wrote(connection), "both sides send 10s");
        }

        // --- What Save and Simulate return ---

        [TestMethod]
        public void AVetoedInsert_IsNotInTheResult()
        {
            var connection = Router(Printed("*1", "x"));
            var original = connection.LoadAll<Row>().ToList();
            var merge = Merge(connection, new[] { new Row { Name = "x" }, new Row { Name = "new" } }, original)
                .WithOperationFilter((operation, oldEntity, newEntity) => operation != TikListMerge<Row>.MergeOperation.Insert);

            CollectionAssert.AreEqual(new[] { "x" }, Names(merge.Simulate(out _, out _, out _, out _)));
            CollectionAssert.AreEqual(new[] { "x" }, Names(merge.Save()));
        }

        [TestMethod]
        public void AVetoedDelete_IsInTheResult_AfterTheExpectedRows()
        {
            var connection = Router(Printed("*1", "keep1"), Printed("*2", "x"), Printed("*3", "keep2"));
            var original = connection.LoadAll<Row>().ToList();
            var merge = Merge(connection, new[] { new Row { Name = "x" } }, original)
                .WithOperationFilter((operation, oldEntity, newEntity) => operation != TikListMerge<Row>.MergeOperation.Delete);

            CollectionAssert.AreEqual(new[] { "x", "keep1", "keep2" }, Names(merge.Simulate(out _, out _, out _, out _)));
            CollectionAssert.AreEqual(new[] { "x", "keep1", "keep2" }, Names(merge.Save()));
        }

        [TestMethod]
        public void ADynamicRowLeftOutOfExpected_IsInTheResult()
        {
            var connection = Router(Printed("*1", "fasttrack", ("dynamic", "true")), Printed("*2", "x"));
            var original = connection.LoadAll<Row>().ToList();

            var result = Merge(connection, new[] { new Row { Name = "x" } }, original).Save();

            CollectionAssert.AreEqual(new[] { "x", "fasttrack" }, Names(result));
            Assert.IsFalse(connection.SentCommands.Any(c => c.First() == "/box/remove"));
        }

        [TestMethod]
        public void ADeletedRow_IsNotInTheResult()
        {
            var connection = Router(Printed("*1", "gone"), Printed("*2", "x"));
            var original = connection.LoadAll<Row>().ToList();

            var result = Merge(connection, new[] { new Row { Name = "x" } }, original).Save();

            CollectionAssert.AreEqual(new[] { "x" }, Names(result));
        }

        [TestMethod]
        public void Simulate_ReturnsAnUpdatedRow_WithItsNewValues_AndLeavesTheOriginalAlone()
        {
            var connection = Router(Printed("*1", "x", ("comment", "old")));
            var original = connection.LoadAll<Row>().ToList();
            var merge = Merge(connection, new[] { new Row { Name = "x", Comment = "new" } }, original).Field(r => r.Comment);

            var simulated = merge.Simulate(out _, out int updates, out _, out _).Single();

            Assert.AreEqual(1, updates);
            Assert.AreEqual("new", simulated.Comment.ToString());
            Assert.AreEqual("*1", simulated.Id);
            Assert.AreEqual("old", original.Single().Comment.ToString(), "Simulate changes nothing");
            Assert.IsFalse(Wrote(connection));
        }

        [TestMethod]
        public void Save_ReturnsAnUpdatedRow_WithItsNewValues()
        {
            var connection = Router(Printed("*1", "x", ("comment", "old")));
            var original = connection.LoadAll<Row>().ToList();

            var saved = Merge(connection, new[] { new Row { Name = "x", Comment = "new" } }, original).Field(r => r.Comment).Save().Single();

            Assert.AreEqual("new", saved.Comment.ToString());
        }

        [TestMethod]
        public void AVetoedUpdate_ReturnsTheRowAsItIs()
        {
            var connection = Router(Printed("*1", "x", ("comment", "old")));
            var original = connection.LoadAll<Row>().ToList();
            var merge = Merge(connection, new[] { new Row { Name = "x", Comment = "new" } }, original)
                .Field(r => r.Comment)
                .WithOperationFilter((operation, oldEntity, newEntity) => false);

            Assert.AreEqual("old", merge.Simulate(out _, out _, out _, out _).Single().Comment.ToString());
            Assert.AreEqual("old", merge.Save().Single().Comment.ToString());
        }

        // --- What is refused when a field is declared ---

        [TestMethod]
        public void AFieldThatIsNotMapped_IsRefusedWhenDeclared()
        {
            var merge = Merge(Router(), new Row[0], new Row[0]);

            var ex = Assert.ThrowsException<ArgumentException>(() => merge.Field(r => r.NotMapped));
            StringAssert.Contains(ex.Message, "NotMapped");
            Assert.ThrowsException<ArgumentException>(() => merge.JustForInsertField(r => r.NotMapped));
            Assert.ThrowsException<ArgumentException>(() => merge.Field(r => r.NotMapped, (expected, current) => expected));
        }

        [TestMethod]
        public void ANestedMember_IsRefusedWhenDeclared()
        {
            var merge = Merge(Router(), new Row[0], new Row[0]);

            Assert.ThrowsException<ArgumentException>(() => merge.Field(r => r.Name.State));
            Assert.ThrowsException<ArgumentException>(() => merge.JustForInsertField(r => r.Name.State));
        }

        // --- How often the caller's code and sequences run ---

        [TestMethod]
        public void AFieldMerge_RunsOncePerRow()
        {
            var connection = Router(Printed("*1", "x", ("comment", "old")));
            var original = connection.LoadAll<Row>().ToList();
            int calls = 0;

            Merge(connection, new[] { new Row { Name = "x", Comment = "new" } }, original)
                .Field(r => r.Comment, (expected, current) => { calls++; return expected; })
                .Save();

            Assert.AreEqual(1, calls);
            Assert.IsTrue(connection.SentCommands.Any(c => c.First() == "/box/set" && c.Contains("=comment=new")));
        }

        [TestMethod]
        public void TheSameFieldDeclaredTwice_KeepsTheLastDeclaration()
        {
            var connection = Router(Printed("*1", "x", ("comment", "old")));
            var original = connection.LoadAll<Row>().ToList();

            Merge(connection, new[] { new Row { Name = "x" } }, original)
                .Field(r => r.Comment)
                .Field(r => r.Comment, (expected, current) => expected.IfAbsent(current))
                .Save();

            Assert.IsFalse(Wrote(connection));
        }

        [TestMethod]
        public void LazySources_AreReadOnce()
        {
            var connection = new TikFakeConnection()
                .WithResponse(cmd => cmd.FirstOrDefault() == "/box/print",
                    _ => new ITikSentence[] { new TikFakeReSentence(Printed("*1", "x")), new TikFakeDoneSentence() });
            var loaded = connection.LoadAll<OrderedRow>().ToList();
            int expectedReads = 0, originalReads = 0;
            IEnumerable<OrderedRow> Expected() { expectedReads++; yield return new OrderedRow { Name = "x" }; }
            IEnumerable<OrderedRow> Original() { originalReads++; foreach (var row in loaded) yield return row; }

            connection.CreateMerge(Expected(), Original()).WithKey(r => r.Name.ToString()).Save();

            Assert.AreEqual(1, expectedReads);
            Assert.AreEqual(1, originalReads);
        }

        [TestMethod]
        public void ADuplicateKey_NamesTheKey_AndKeepsTheCause()
        {
            var merge = Merge(Router(), new[] { new Row { Name = "x" }, new Row { Name = "x" } }, new Row[0]);

            var ex = Assert.ThrowsException<ArgumentException>(() => merge.Simulate(out _, out _, out _, out _));

            StringAssert.Contains(ex.Message, "'x'");
            Assert.IsNotNull(ex.InnerException);
        }

        [TestMethod]
        public void SaveListDifferences_ADuplicateId_NamesTheId()
        {
            var connection = Router(Printed("*1", "x"));
            var loaded = connection.LoadAll<Row>().ToList();

            var ex = Assert.ThrowsException<ArgumentException>(
                () => connection.SaveListDifferences(new[] { loaded[0], loaded[0] }, loaded));

            StringAssert.Contains(ex.Message, "*1");
        }
    }
}
