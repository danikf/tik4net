// AbsentFieldReadTests.cs — a field the row does not carry reads Absent: a field another RouterOS version lacks, or one a
// CLI print leaves out, distinguishable from a field the router printed empty.

#nullable enable

using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    [TikEntity("/test/absent")]
    public class AbsentFieldEntity
    {
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        [TikProperty("plain")]
        public TikField<string?> Plain { get; set; }

        [TikProperty("with-default", DefaultValue = "main")]
        public TikField<string?> WithDefault { get; set; }

        [TikProperty("number")]
        public TikField<int?> Number { get; set; }
    }

    [TestClass]
    public class AbsentFieldReadTests
    {
        private static TikFakeConnection Router(string command, Dictionary<string, string> row)
            => new TikFakeConnection().WithResponse(
                cmd => cmd.FirstOrDefault() == command,
                new ITikSentence[] { new TikFakeReSentence(row), new TikFakeDoneSentence() })
                .WithNonQuery(cmd => cmd.First().EndsWith("/set"));

        private static AbsentFieldEntity ReadRowWithOnlyAnId()
            => Router("/test/absent/print", new Dictionary<string, string> { [".id"] = "*1" })
                .LoadAll<AbsentFieldEntity>().Single();

        [TestMethod]
        public void AnAbsentString_ReadsAbsent()
            => Assert.AreEqual(TikFieldState.Absent, ReadRowWithOnlyAnId().Plain.State);

        [TestMethod]
        public void AnEmptyString_IsAValue()
        {
            var row = Router("/test/absent/print", new Dictionary<string, string> { [".id"] = "*1", ["plain"] = "" })
                .LoadAll<AbsentFieldEntity>().Single();

            Assert.AreEqual(TikFieldState.Present, row.Plain.State);
            Assert.AreEqual("", row.Plain.Value);
        }

        [TestMethod]
        public void AnAbsentStringWithADeclaredDefault_ReadsAbsent_TheDefaultIsDocumentation()
            => Assert.AreEqual(TikFieldState.Absent, ReadRowWithOnlyAnId().WithDefault.State);

        [TestMethod]
        public void AnAbsentNumber_ReadsAbsent()
            => Assert.AreEqual(TikFieldState.Absent, ReadRowWithOnlyAnId().Number.State);

        [TestMethod]
        public void ALoadedEntityWithAnAbsentString_SavesOnlyWhatChanged()
        {
            var connection = Router("/test/absent/print", new Dictionary<string, string> { [".id"] = "*1" });
            var row = connection.LoadAll<AbsentFieldEntity>().Single();

            row.Number = 5;
            connection.Save(row);

            var set = connection.SentCommands.Single(c => c.First() == "/test/absent/set");
            CollectionAssert.AreEquivalent(new[] { "/test/absent/set", "=.id=*1", "=number=5" }, set);
        }

        [TestMethod]
        public void ANeverLoadedEntity_DoesNotSendAStringNobodySet()
        {
            var connection = Router("/test/absent/print", new Dictionary<string, string> { [".id"] = "*1" });

            connection.Save(new AbsentFieldEntityWithId("*1") { Number = 5 });

            var set = connection.SentCommands.Single(c => c.First() == "/test/absent/set");
            Assert.IsFalse(set.Any(w => w.StartsWith("=plain=")), string.Join(" ", set));
        }

        [TikEntity("/test/absent")]
        private sealed class AbsentFieldEntityWithId
        {
            public AbsentFieldEntityWithId() { }
            public AbsentFieldEntityWithId(string id) { Id = id; }

            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; private set; }

            [TikProperty("plain")]
            public TikField<string?> Plain { get; set; }

            [TikProperty("number")]
            public TikField<int?> Number { get; set; }
        }
    }
}
