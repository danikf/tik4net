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
