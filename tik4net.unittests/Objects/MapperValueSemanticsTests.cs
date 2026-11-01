#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// How the mapper turns a field into a wire value and decides whether it is worth sending: silence is not an
    /// instruction, an explicit value always reaches the router whatever the router's default is, and a declared
    /// <c>DefaultValue</c> changes nothing.
    /// </summary>
    [TestClass]
    public class MapperValueSemanticsTests
    {
        [TikEntity("/test/nullable-entity")]
        internal class NullableEntity
        {
            [TikProperty(".id", IsReadOnly = true)]
            public string? Id { get; private set; }

            /// <summary>A field the router turns on unless told otherwise.</summary>
            [TikProperty("defaults-on", DefaultValue = "yes")]
            public TikField<bool?> DefaultsOn { get; set; }

            [TikProperty("no-declared-default")]
            public TikField<bool?> NoDeclaredDefault { get; set; }

            [TikProperty("optional-port", DefaultValue = "8291")]
            public TikField<int?> OptionalPort { get; set; }
        }

        private static TikEntityPropertyAccessor Accessor(string propertyName)
            => TikEntityMetadataCache.GetMetadata<NullableEntity>()
                .Properties.Single(p => p.PropertyName == propertyName);

        [TestMethod]
        public void AnUntouchedFieldReadsBackAsNullRatherThanAsItsDefault()
        {
            var fresh = new NullableEntity();

            Assert.IsNull(Accessor("DefaultsOn").GetEntityValue(fresh));
            Assert.IsNull(Accessor("OptionalPort").GetEntityValue(fresh));
        }

        [TestMethod]
        public void AnAssignedFieldSerializesLikeItsUnderlyingType()
        {
            var entity = new NullableEntity { DefaultsOn = false, OptionalPort = 8291 };

            Assert.AreEqual("no", Accessor("DefaultsOn").GetEntityValue(entity));
            Assert.AreEqual("8291", Accessor("OptionalPort").GetEntityValue(entity));

            entity.DefaultsOn = true;
            Assert.AreEqual("yes", Accessor("DefaultsOn").GetEntityValue(entity));
        }

        [TestMethod]
        public void ReadingKeepsAFieldTheRowLacksAbsent()
        {
            var entity = new NullableEntity();
            var accessor = Accessor("DefaultsOn");

            accessor.SetEntityValue(entity, null);
            Assert.AreEqual(TikFieldState.Absent, entity.DefaultsOn.State, "the router did not report the field");

            accessor.SetEntityValue(entity, "yes");
            Assert.IsTrue(entity.DefaultsOn == true);

            accessor.SetEntityValue(entity, "no");
            Assert.IsTrue(entity.DefaultsOn == false);
        }

        [TestMethod]
        public void ReadingAcceptsBothSpellingsTheRouterUses()
        {
            var entity = new NullableEntity();
            var accessor = Accessor("DefaultsOn");

            accessor.SetEntityValue(entity, "yes");
            Assert.IsTrue(entity.DefaultsOn == true, "writable fields come back as yes/no");

            accessor.SetEntityValue(entity, "true");
            Assert.IsTrue(entity.DefaultsOn == true, "read-only fields come back as true/false");

            accessor.SetEntityValue(entity, "false");
            Assert.IsTrue(entity.DefaultsOn == false);
        }

        // ── …and what that means on the wire ────────────────────────────────────

        [TestMethod]
        public void ACreateLeavesOutAnUntouchedFieldAndSendsAnAssignedOne()
        {
            var conn = new tik4net.Testing.TikFakeConnection()
                .WithScalarResponse(rows => rows.First() == "/test/nullable-entity/add", "*1");

            conn.Save(new NullableEntity { DefaultsOn = true });

            string sent = string.Join(" ", conn.SentCommands.Single());
            StringAssert.Contains(sent, "=defaults-on=yes", "equal to the declared default, and assigned: a DefaultValue changes nothing");
            Assert.IsFalse(sent.Contains("no-declared-default"), "an untouched field is not mentioned at all");
            Assert.IsFalse(sent.Contains("optional-port"), "nor is one with a declared default");
        }

        [TestMethod]
        public void AnUpdateSaysNothingAboutAFieldTheCallerSaidNothingAbout()
        {
            // The destructive alternative would be to read null as "unset it" — deleting whatever the router
            // holds on the strength of silence, including for every field a partial load never populated.
            var conn = new tik4net.Testing.TikFakeConnection()
                .WithNonQuery(rows => rows.First() == "/test/nullable-entity/set")
                .WithNonQuery(rows => rows.First() == "/test/nullable-entity/unset");

            var entity = new NullableEntity { DefaultsOn = true };
            typeof(NullableEntity).GetProperty("Id")!.SetValue(entity, "*1");

            conn.Save(entity, usedFieldsFilter: new[] { "defaults-on", "no-declared-default", "optional-port" });

            string sent = string.Join(" | ", conn.SentCommands.Select(rows => string.Join(" ", rows)));
            StringAssert.Contains(sent, "=defaults-on=yes");
            Assert.IsFalse(sent.Contains("no-declared-default"), "silence is not an instruction");
            Assert.IsFalse(sent.Contains("unset"), "and it is certainly not an unset");
        }

        [TestMethod]
        public void ClearingALoadedFieldToNullUnsetsItOnTheRouter()
        {
            var conn = new tik4net.Testing.TikFakeConnection()
                .WithResponse(rows => rows.First() == "/test/nullable-entity/print",
                    new ITikSentence[]
                    {
                        new tik4net.Testing.TikFakeReSentence(new Dictionary<string, string>
                        {
                            { ".id", "*1" }, { "defaults-on", "no" }, { "optional-port", "8291" },
                        }),
                        new tik4net.Testing.TikFakeDoneSentence(),
                    })
                .WithNonQuery(rows => rows.First() == "/test/nullable-entity/set")
                .WithNonQuery(rows => rows.First() == "/test/nullable-entity/unset");

            var loaded = conn.LoadAll<NullableEntity>().Single();
            Assert.IsTrue(loaded.DefaultsOn == false, "precondition: it came back with a value");

            loaded.DefaultsOn = null;
            conn.Save(loaded);

            string sent = string.Join(" | ", conn.SentCommands.Select(rows => string.Join(" ", rows)));
            StringAssert.Contains(sent, "/test/nullable-entity/unset");
            StringAssert.Contains(sent, "=value-name=defaults-on");
            Assert.IsFalse(sent.Contains("=defaults-on="), "cleared, so it must not also be set");
        }

        [TestMethod]
        public void AFieldTheLoadDidNotSeeIsNotUnset()
        {
            var conn = new tik4net.Testing.TikFakeConnection()
                .WithResponse(rows => rows.First() == "/test/nullable-entity/print",
                    new ITikSentence[]
                    {
                        new tik4net.Testing.TikFakeReSentence(new Dictionary<string, string>
                        {
                            { ".id", "*1" }, { "defaults-on", "no" },
                        }),
                        new tik4net.Testing.TikFakeDoneSentence(),
                    })
                .WithNonQuery(rows => rows.First() == "/test/nullable-entity/set")
                .WithNonQuery(rows => rows.First() == "/test/nullable-entity/unset");

            var loaded = conn.LoadAll<NullableEntity>().Single();
            loaded.DefaultsOn = true;   // one real change, so the save is not skipped outright
            conn.Save(loaded);

            string sent = string.Join(" | ", conn.SentCommands.Select(rows => string.Join(" ", rows)));
            Assert.IsFalse(sent.Contains("optional-port"),
                "the router never reported it, so nothing is known about it — and nothing is said about it");
        }

        // ── What a mapped property may be ───────────────────────────────────────

        [TikEntity("/bad")] internal class PlainStringBox { [TikProperty("name")] public string? Name { get; set; } }
        [TikEntity("/bad")] internal class PlainIntBox { [TikProperty("port")] public int Port { get; set; } }
        [TikEntity("/bad")] internal class PlainIdBox { [TikProperty(".id")] public int Id { get; set; } }
        [TikEntity("/bad")] internal class TimeSpanBox { [TikProperty("uptime")] public TikField<TimeSpan?> Uptime { get; set; } }
        [TikEntity("/bad")] internal class FlagsBox { [TikProperty("state")] public TikField<AFlagsEnum?> State { get; set; } }
        [TikEntity("/bad")] internal class FlagsListBox { [TikProperty("state")] public TikField<TikValueList<AFlagsEnum>?> State { get; set; } }

        [Flags] internal enum AFlagsEnum { [TikEnum("a")] A = 1, [TikEnum("b")] B = 2 }

        [DataTestMethod]
        [DataRow(typeof(PlainStringBox), "TikField<String?>")]
        [DataRow(typeof(PlainIntBox), "TikField<Int32?>")]
        [DataRow(typeof(PlainIdBox), "only the .id is a plain string")]
        [DataRow(typeof(TimeSpanBox), "TikField<TikDuration?>")]
        [DataRow(typeof(FlagsBox), "[Flags]")]
        [DataRow(typeof(FlagsListBox), "[Flags]")]
        public void WhatTheMapperCannotHonourIsRefusedAtMetadataBuild_NamingTheReplacement(Type entity, string named)
        {
            var ex = Assert.ThrowsException<ArgumentException>(() => new TikEntityMetadata(entity));
            StringAssert.Contains(ex.Message, named);
        }

        [TikEntity("/box")]
        internal class WritableIdBox
        {
            [TikProperty(".id")] public string? Id { get; set; }
            [TikProperty("name")] public TikField<string?> Name { get; set; }
        }

        [TestMethod]
        public void TheIdIsNeverAFieldAWriteSends()
        {
            var id = TikEntityMetadataCache.GetMetadata<WritableIdBox>().IdProperty!;
            Assert.IsTrue(id.IsReadOnly, "even declared with a public setter and without IsReadOnly");
            Assert.IsFalse(id.IsWrapped);
        }
    }
}
