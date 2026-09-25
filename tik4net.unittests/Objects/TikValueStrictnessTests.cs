#nullable enable
// TikValueStrictnessTests.cs — GetValueReport and EnsureStrict: how a caller finds out that a load met a value from
// another RouterOS version (design "Strictness and merge"; review U5).

using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    [TestClass]
    public class TikValueStrictnessTests
    {
        private static TikValueMapperTests.Box Load(params (string, string)[] fields)
        {
            var row = new Dictionary<string, string> { [".id"] = "*1" };
            foreach (var (k, v) in fields) row[k] = v;
            return new TikFakeConnection()
                .WithResponse(cmd => cmd.FirstOrDefault() == "/box/print",
                    _ => new ITikSentence[] { new TikFakeReSentence(row), new TikFakeDoneSentence() })
                .LoadAll<TikValueMapperTests.Box>().Single();
        }

        [TestMethod]
        public void TheReport_ListsEveryWrappedField_WithItsState()
        {
            var report = Load(("name", "a"), ("mode", "false"), ("state", "new,untracked")).GetValueReport();

            Assert.AreEqual(TikValueState.Present, report.Single(r => r.FieldName == "name").State);
            Assert.AreEqual("false", report.Single(r => r.FieldName == "mode").RawValue);
            Assert.AreEqual("untracked", report.Single(r => r.FieldName == "state").UnknownFlagWords);
            Assert.AreEqual(TikValueState.Absent, report.Single(r => r.FieldName == "port").State);
            Assert.IsFalse(report.Any(r => r.FieldName == ".id"), "a plain property has no state to report");
        }

        [TestMethod]
        public void EnsureStrict_ByDefault_RefusesUnparsed_AndNamesTheField()
        {
            var ex = Assert.ThrowsException<TikStrictValueException>(() => Load(("name", "a"), ("mode", "false")).EnsureStrict());

            Assert.AreEqual("mode", ex.Offenders.Single().FieldName);
            StringAssert.Contains(ex.Message, "mode: Unparsed 'false'");
        }

        [TestMethod]
        public void EnsureStrict_ByDefault_AcceptsAbsent()
        {
            var box = Load(("name", "a"));
            Assert.AreSame(box, box.EnsureStrict());
        }

        [TestMethod]
        public void EnsureStrict_Absent_HonoursTheAllowList()
        {
            var box = Load(("name", "a"), ("port", "80"));

            var ex = Assert.ThrowsException<TikStrictValueException>(() => box.EnsureStrict(TikStrictness.Absent, "comment", "mode"));
            CollectionAssert.AreEquivalent(new[] { "state", "fib", "running" }, ex.Offenders.Select(o => o.FieldName).ToArray());
        }

        [TestMethod]
        public void EnsureStrict_UnknownFlagWords_IsOptIn()
        {
            var box = Load(("state", "new,untracked"));

            box.EnsureStrict();
            Assert.ThrowsException<TikStrictValueException>(() => box.EnsureStrict(TikStrictness.UnknownFlagWords));
        }

        [TestMethod]
        public void EnsureStrict_OnAList_ChecksEveryRow()
        {
            var rows = new[] { Load(("name", "a")), Load(("name", "b"), ("port", "none")) };
            Assert.ThrowsException<TikStrictValueException>(() => rows.EnsureAllStrict().ToList());
        }
    }
}
