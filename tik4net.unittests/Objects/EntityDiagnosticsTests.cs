// EntityDiagnosticsTests.cs — what a read reports to the connection's ITikEntityDiagnostics.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    [TikEntity("/test/diag")]
    public class DiagEntity
    {
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        [TikProperty("name")]
        public TikField<string?> Name { get; set; }

        [TikProperty("mtu")]
        public TikField<int?> Mtu { get; set; }

        [TikProperty("routing-table", AlternateNames = new[] { "routing-mark" })]
        public TikField<string?> Table { get; set; }

        [TikProperty("comment")]
        public TikField<string?> Comment { get; set; }
    }

    [TestClass]
    public class EntityDiagnosticsTests
    {
        private static TikFakeConnection Router(params Dictionary<string, string>[] rows)
            => new TikFakeConnection().WithResponse(
                cmd => cmd.FirstOrDefault() == "/test/diag/print",
                rows.Select(r => (ITikSentence)new TikFakeReSentence(r)).Concat(new ITikSentence[] { new TikFakeDoneSentence() }).ToArray());

        private static Dictionary<string, string> Row(string id, params (string Name, string Value)[] fields)
        {
            var row = new Dictionary<string, string> { [".id"] = id };
            foreach (var f in fields)
                row[f.Name] = f.Value;
            return row;
        }

        private static List<TikEntityReadReport> Watch(ITikConnection connection)
        {
            var reports = new List<TikEntityReadReport>();
            connection.SetEntityDiagnostics(reports.Add);
            return reports;
        }

        [TestMethod]
        public void AValueTheTypeCannotHold_IsReportedWithTheRoutersWord()
        {
            var router = Router(
                Row("*1", ("name", "a"), ("mtu", "auto"), ("routing-table", "main"), ("comment", "")),
                Row("*2", ("name", "b"), ("mtu", "auto"), ("routing-table", "main"), ("comment", "")),
                Row("*3", ("name", "c"), ("mtu", "1500"), ("routing-table", "main"), ("comment", "")));
            var reports = Watch(router);

            router.LoadAll<DiagEntity>().ToList();

            var unparsed = reports.Single().UnparsedFields.Single();
            Assert.AreEqual("mtu", unparsed.FieldName);
            Assert.AreEqual(nameof(DiagEntity.Mtu), unparsed.PropertyName);
            Assert.AreEqual("auto", unparsed.RawValue);
            Assert.AreEqual(2, unparsed.Rows);
        }

        [TestMethod]
        public void AMappedFieldNoRowCarried_IsReported_AndOneSomeRowsCarryIsNot()
        {
            var router = Router(
                Row("*1", ("name", "a"), ("mtu", "1500"), ("comment", "x")),
                Row("*2", ("name", "b"), ("mtu", "1500")));
            var reports = Watch(router);

            router.LoadAll<DiagEntity>().ToList();

            CollectionAssert.AreEqual(new[] { "routing-table" }, reports.Single().FieldsAbsentFromEveryRow.ToArray(),
                "comment is on one row, so it is a per-row field, not a missing one");
            Assert.IsTrue(reports.Single().IsComplete);
        }

        [TestMethod]
        public void AFieldUnderItsAlternateName_IsNeitherAbsentNorUnmapped()
        {
            var router = Router(Row("*1", ("name", "a"), ("mtu", "1500"), ("routing-mark", "m"), ("comment", "")));
            var reports = Watch(router);

            router.LoadAll<DiagEntity>().ToList();

            Assert.AreEqual(0, reports.Count, reports.FirstOrDefault()?.ToString());
        }

        [TestMethod]
        public void ANameNoPropertyMaps_IsReported_ButProtocolWordsAreNot()
        {
            // A rename looks like this: the old name absent from every row, the new one unmapped.
            var router = Router(Row("*1", ("name", "a"), ("mtu", "1500"), ("comment", ""), ("routing-vrf", "main"),
                (".tag", "7"), (".nextid", "*2")));
            var reports = Watch(router);

            router.LoadAll<DiagEntity>().ToList();

            var report = reports.Single();
            CollectionAssert.AreEqual(new[] { "routing-vrf" }, report.UnmappedFields.ToArray());
            CollectionAssert.AreEqual(new[] { "routing-table" }, report.FieldsAbsentFromEveryRow.ToArray());
            StringAssert.Contains(report.ToString(), "unmapped: routing-vrf");
        }

        [TestMethod]
        public void OnWinboxNative_UnmappedNamesAreNotReported()
        {
            // Its windows carry fields the API never prints; every read would list them.
            var router = Router(Row("*1", ("name", "a"), ("mtu", "1500"), ("routing-table", "main"), ("comment", ""),
                ("window-only", "1")));
            router.Capabilities |= TikConnectionCapability.FieldLabels;
            var reports = Watch(router);

            router.LoadAll<DiagEntity>().ToList();

            Assert.AreEqual(0, reports.Count, reports.FirstOrDefault()?.ToString());
        }

        [TestMethod]
        public void AFieldTheCallerDidNotAskFor_IsNotAbsent()
        {
            var router = Router(Row("*1", ("name", "a")));
            var reports = Watch(router);

            router.LoadList<DiagEntity>(router.CreateParameter(TikSpecialProperties.Proplist, "name")).ToList();

            Assert.AreEqual(0, reports.Count, reports.FirstOrDefault()?.ToString());
        }

        [TestMethod]
        public void ACleanRead_OrNoSink_ReportsNothing()
        {
            var router = Router(Row("*1", ("name", "a"), ("mtu", "1500"), ("routing-table", "main"), ("comment", "")));
            var reports = Watch(router);
            router.LoadAll<DiagEntity>().ToList();
            Assert.AreEqual(0, reports.Count);

            router.ClearEntityDiagnostics();
            Assert.IsNull(router.GetEntityDiagnostics());
        }

        [TestMethod]
        public void ASinkThatThrows_DoesNotFailTheLoad()
        {
            var router = Router(Row("*1", ("name", "a")));
            router.SetEntityDiagnostics(_ => throw new InvalidOperationException("sink"));

            Assert.AreEqual("a", router.LoadAll<DiagEntity>().Single().Name.Value);
        }

        [TestMethod]
        public async System.Threading.Tasks.Task AnAsyncLoad_IsReportedToo()
        {
            var router = Router(Row("*1", ("name", "a"), ("mtu", "auto"), ("routing-table", "main"), ("comment", "")));
            var reports = Watch(router);

            await router.LoadAllAsync<DiagEntity>();

            Assert.AreEqual("mtu", reports.Single().UnparsedFields.Single().FieldName);
        }
    }
}
