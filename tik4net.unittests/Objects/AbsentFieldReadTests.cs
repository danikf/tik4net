// AbsentFieldReadTests.cs — a field the row does not carry reads null where the property can say so.
//
// A `string?` with no declared default used to read "" for a field the router did not print: a field another
// RouterOS version lacks, or one a CLI print leaves out — indistinguishable from a field the router printed empty.

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
        public string? Plain { get; set; }

        [TikProperty("with-default", DefaultValue = "main")]
        public string? WithDefault { get; set; }

        [TikProperty("number")]
        public int? Number { get; set; }

        [TikProperty("counter")]
        public long Counter { get; set; }
    }

#nullable disable
    // An entity compiled without nullable annotations cannot say "may be null", so it keeps the old reading.
    [TikEntity("/test/absent-oblivious")]
    public class AbsentFieldObliviousEntity
    {
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        [TikProperty("plain")]
        public string Plain { get; set; }
    }
#nullable enable

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
        public void AnAbsentNullableString_ReadsNull()
            => Assert.IsNull(ReadRowWithOnlyAnId().Plain);

        [TestMethod]
        public void AnEmptyString_IsStillAValue()
        {
            var row = Router("/test/absent/print", new Dictionary<string, string> { [".id"] = "*1", ["plain"] = "" })
                .LoadAll<AbsentFieldEntity>().Single();

            Assert.AreEqual("", row.Plain);
        }

        [TestMethod]
        public void AnAbsentStringWithADeclaredDefault_StillReadsTheDefault()
            => Assert.AreEqual("main", ReadRowWithOnlyAnId().WithDefault);

        [TestMethod]
        public void AnAbsentNullableNumber_ReadsNull_AsBefore()
            => Assert.IsNull(ReadRowWithOnlyAnId().Number);

        [TestMethod]
        public void AnAbsentNonNullableNumber_StillReadsItsClrDefault()
            => Assert.AreEqual(0L, ReadRowWithOnlyAnId().Counter);

        [TestMethod]
        public void AnObliviousString_KeepsReadingEmpty()
        {
            var row = Router("/test/absent-oblivious/print", new Dictionary<string, string> { [".id"] = "*1" })
                .LoadAll<AbsentFieldObliviousEntity>().Single();

            Assert.AreEqual("", row.Plain);
        }

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
            // It used to go out as `=plain=` (empty): the unset string?'s default was "", and an update of an
            // entity with no snapshot sends every non-null value — which cleared the field on the router.
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
            public string? Plain { get; set; }

            [TikProperty("number")]
            public int? Number { get; set; }
        }
    }
}
