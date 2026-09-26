#nullable enable
// TikValueMergeTests.cs — CreateMerge / SaveListDifferences over TikValue<T> fields (design review H3). The merge
// compared fields through Convert.ToString, which renders Absent, Present("") and Present(null) alike.

using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    [TestClass]
    public class TikValueMergeTests
    {
        private static TikFakeConnection Router(params Dictionary<string, string>[] rows)
            => new TikFakeConnection()
                .WithResponse(cmd => cmd.FirstOrDefault() == "/box/print",
                    _ => rows.Select(r => (ITikSentence)new TikFakeReSentence(r)).Concat(new[] { (ITikSentence)new TikFakeDoneSentence() }).ToArray())
                .WithNonQuery(cmd => cmd.First() == "/box/set" || cmd.First() == "/box/unset");

        [TestMethod]
        public void AnEmptyValueAndAnAbsentOne_AreDifferent()
        {
            // The router holds name=x with comment="" (an empty list-like field); the expected row has no comment at all.
            var connection = Router(new Dictionary<string, string> { [".id"] = "*1", ["name"] = "x", ["comment"] = "" });
            var original = connection.LoadAll<TikValueMapperTests.Box>().ToList();
            var expected = new List<TikValueMapperTests.Box> { new TikValueMapperTests.Box { Name = "x" } };

            connection.CreateMerge(expected, original)
                .WithKey(b => b.Name.ToString())
                .Field(b => b.Comment)
                .Save();

            Assert.IsTrue(connection.SentCommands.Any(c => c.First() == "/box/set" || c.First() == "/box/unset"),
                "Absent and Present(\"\") compared equal through ToString, so the target was never touched");
        }

        [TestMethod]
        public void AnAbsentExpectedValue_UnsetsTheField_ByDefault()
        {
            var connection = Router(new Dictionary<string, string> { [".id"] = "*1", ["name"] = "x", ["comment"] = "keep" });
            var original = connection.LoadAll<TikValueMapperTests.Box>().ToList();

            connection.CreateMerge(new[] { new TikValueMapperTests.Box { Name = "x" } }, original)
                .WithKey(b => b.Name.ToString())
                .Field(b => b.Comment)
                .Save();

            Assert.IsTrue(connection.SentCommands.Any(c => c.First() == "/box/unset" && c.Contains("=value-name=comment")));
        }

        [TestMethod]
        public void AFieldMerge_IfAbsent_KeepsTheCurrentValue_AndTheRowIsUnchanged()
        {
            var connection = Router(new Dictionary<string, string> { [".id"] = "*1", ["name"] = "x", ["comment"] = "keep" });
            var original = connection.LoadAll<TikValueMapperTests.Box>().ToList();
            var logged = new List<TikListMerge<TikValueMapperTests.Box>.MergeOperation>();

            connection.CreateMerge(new[] { new TikValueMapperTests.Box { Name = "x" } }, original)
                .WithKey(b => b.Name.ToString())
                .Field(b => b.Comment, (expected, current) => expected.IfAbsent(current))
                .WithDmlLogCallback((op, oldE, newE) => logged.Add(op))
                .Save();

            Assert.IsFalse(connection.SentCommands.Any(c => c.First() == "/box/set" || c.First() == "/box/unset"));
            Assert.AreEqual(0, logged.Count, "the merged value equals the current one, so the row is not an update");
        }

        [TestMethod]
        public void AFieldMerge_StillTakesAPresentExpectedValue()
        {
            var connection = Router(new Dictionary<string, string> { [".id"] = "*1", ["name"] = "x", ["comment"] = "old" });
            var original = connection.LoadAll<TikValueMapperTests.Box>().ToList();

            connection.CreateMerge(new[] { new TikValueMapperTests.Box { Name = "x", Comment = "new" } }, original)
                .WithKey(b => b.Name.ToString())
                .Field(b => b.Comment, (expected, current) => expected.IfAbsent(current))
                .Save();

            Assert.IsTrue(connection.SentCommands.Any(c => c.First() == "/box/set" && c.Contains("=comment=new")));
        }

        [TestMethod]
        public void AnAssignedNull_AndAFieldTheRouterDoesNotPrint_AreTheSame()
        {
            // An expected row built in code (SrcAddress = upload ? ip : null) against a router row without the field:
            // both have no value, as for == null. Found by the shaper scenario, where every row read as changed.
            var connection = Router(new Dictionary<string, string> { [".id"] = "*1", ["name"] = "x" });
            var original = connection.LoadAll<TikValueMapperTests.Box>().ToList();

            connection.CreateMerge(new[] { new TikValueMapperTests.Box { Name = "x", Comment = null } }, original)
                .WithKey(b => b.Name.ToString())
                .Field(b => b.Comment)
                .Simulate(out _, out int updates, out _, out _);

            Assert.AreEqual(0, updates);
        }

        [TestMethod]
        public void AFieldTheRouterDoesNotPrint_IsAnUpdateEveryRun_UnlessTheFieldIsIfPrintedIn()
        {
            // Mangle passthrough on a jump rule: the row was added with the field, the router does not print it there,
            // and writing it again changes nothing - so a plain Field would update the row on every run.
            var connection = Router(new Dictionary<string, string> { [".id"] = "*1", ["name"] = "x" });
            var original = connection.LoadAll<TikValueMapperTests.Box>().ToList();
            var expected = new[] { new TikValueMapperTests.Box { Name = "x", Port = 80 } };

            connection.CreateMerge(expected, original)
                .WithKey(b => b.Name.ToString())
                .Field(b => b.Port)
                .Simulate(out _, out int plainUpdates, out _, out _);
            connection.CreateMerge(expected, original)
                .WithKey(b => b.Name.ToString())
                .Field(b => b.Port, (wanted, current) => wanted.IfPrintedIn(current))
                .Simulate(out _, out int ruleUpdates, out _, out _);

            Assert.AreEqual(1, plainUpdates);
            Assert.AreEqual(0, ruleUpdates);
        }

        [TestMethod]
        public void IfPrintedIn_StillUpdatesAFieldTheRouterPrints()
        {
            var connection = Router(new Dictionary<string, string> { [".id"] = "*1", ["name"] = "x", ["port"] = "8080" });
            var original = connection.LoadAll<TikValueMapperTests.Box>().ToList();

            connection.CreateMerge(new[] { new TikValueMapperTests.Box { Name = "x", Port = 80 } }, original)
                .WithKey(b => b.Name.ToString())
                .Field(b => b.Port, (wanted, current) => wanted.IfPrintedIn(current))
                .Save();

            Assert.IsTrue(connection.SentCommands.Any(c => c.First() == "/box/set" && c.Contains("=port=80")));
        }

        [TestMethod]
        public void EqualValues_SendNothing()
        {
            var connection = Router(new Dictionary<string, string> { [".id"] = "*1", ["name"] = "x", ["mode"] = "false" });
            var original = connection.LoadAll<TikValueMapperTests.Box>().ToList();
            var expected = new List<TikValueMapperTests.Box>
            {
                new TikValueMapperTests.Box { Name = "x", Mode = TikValue<TikValueMapperTests.Mode?>.FromWire("false") },
            };

            connection.CreateMerge(expected, original)
                .WithKey(b => b.Name.ToString())
                .Field(b => b.Mode)
                .Save();

            Assert.IsFalse(connection.SentCommands.Any(c => c.First() == "/box/set" || c.First() == "/box/unset"),
                "the same unparsed word on both sides is equal");
        }
    }
}
