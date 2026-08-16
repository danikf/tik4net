using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// Characterization of how the mapper turns a property into a wire value and decides whether it is worth
    /// sending — written <b>before</b> B4 touches any of it, so that a change to these rules has to be a
    /// deliberate edit to this file rather than something noticed later on a router.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These rules are not obvious and are not written down anywhere else. In particular: an undeclared
    /// <c>DefaultValue</c> is <i>computed</i> from the CLR default (so a <c>bool</c> silently gets
    /// <c>"no"</c>, an <c>int</c> <c>"0"</c>), and <c>GetEntityValue</c> substitutes <c>DefaultValue</c> when
    /// the property holds <c>null</c> — which is exactly the conflation of "unset" with "the default" that
    /// B4 exists to unpick. Pinning them first means the unpicking cannot quietly take something else with it.
    /// </para>
    /// <para>
    /// The entity below is deliberately local to the test: it exercises each shape once, without depending on
    /// how any shipped entity happens to be annotated today.
    /// </para>
    /// </remarks>
    [TestClass]
    public class MapperValueSemanticsTests
    {
        [TikEntity("/test/entity")]
        internal class SampleEntity
        {
            [TikProperty(".id", IsReadOnly = true)]
            public string Id { get; private set; }

            [TikProperty("plain-string")]
            public string PlainString { get; set; }

            [TikProperty("string-with-default", DefaultValue = "fallback")]
            public string StringWithDefault { get; set; }

            [TikProperty("plain-bool")]
            public bool PlainBool { get; set; }

            [TikProperty("bool-defaulting-on", DefaultValue = "yes")]
            public bool BoolDefaultingOn { get; set; }

            [TikProperty("plain-int")]
            public int PlainInt { get; set; }

            [TikProperty("int-with-default", DefaultValue = "1500")]
            public int IntWithDefault { get; set; }

            [TikProperty("unset-on-default", UnsetOnDefault = true)]
            public string UnsetOnDefault { get; set; }
        }

        /// <summary>
        /// The same shapes made nullable — what B4 adds. A nullable property has a state no non-nullable one
        /// has: "the caller said nothing", which is distinct from every value it can hold.
        /// </summary>
        [TikEntity("/test/nullable-entity")]
        internal class NullableEntity
        {
            [TikProperty(".id", IsReadOnly = true)]
            public string Id { get; private set; }

            /// <summary>A field the router turns on unless told otherwise — the A10 shape.</summary>
            [TikProperty("defaults-on", DefaultValue = "yes")]
            public bool? DefaultsOn { get; set; }

            [TikProperty("no-declared-default")]
            public bool? NoDeclaredDefault { get; set; }

            [TikProperty("optional-port", DefaultValue = "8291")]
            public int? OptionalPort { get; set; }

            [TikProperty("optional-unset", UnsetOnDefault = true, DefaultValue = "yes")]
            public bool? OptionalUnset { get; set; }
        }

        private static TikEntityPropertyAccessor Accessor(string propertyName)
            => TikEntityMetadataCache.GetMetadata<SampleEntity>()
                .Properties.Single(p => p.PropertyName == propertyName);

        private static TikEntityPropertyAccessor NullableAccessor(string propertyName)
            => TikEntityMetadataCache.GetMetadata<NullableEntity>()
                .Properties.Single(p => p.PropertyName == propertyName);

        // ── What DefaultValue is, when nobody declared one ──────────────────────

        [TestMethod]
        public void AnUndeclaredDefaultIsComputedFromTheClrDefault()
        {
            Assert.AreEqual("no", Accessor("PlainBool").DefaultValue, "bool → the wire form of false");
            Assert.AreEqual("0", Accessor("PlainInt").DefaultValue, "int → \"0\"");
            Assert.AreEqual("", Accessor("PlainString").DefaultValue, "reference type → empty string");
        }

        [TestMethod]
        public void ADeclaredDefaultIsTakenVerbatim()
        {
            Assert.AreEqual("yes", Accessor("BoolDefaultingOn").DefaultValue);
            Assert.AreEqual("1500", Accessor("IntWithDefault").DefaultValue);
            Assert.AreEqual("fallback", Accessor("StringWithDefault").DefaultValue);
        }

        // ── Serialization ───────────────────────────────────────────────────────

        [TestMethod]
        public void ABoolSerializesToTheRoutersYesAndNo()
        {
            var entity = new SampleEntity { PlainBool = true };
            Assert.AreEqual("yes", Accessor("PlainBool").GetEntityValue(entity));

            entity.PlainBool = false;
            Assert.AreEqual("no", Accessor("PlainBool").GetEntityValue(entity));
        }

        [TestMethod]
        public void ANullReferencePropertyReadsBackAsItsDefaultValueNotAsNull()
        {
            // The conflation B4 unpicks: "the caller said nothing" and "the caller said the default" produce
            // the same string here, so nothing downstream can tell them apart.
            var entity = new SampleEntity { PlainString = null, StringWithDefault = null };

            Assert.AreEqual("", Accessor("PlainString").GetEntityValue(entity));
            Assert.AreEqual("fallback", Accessor("StringWithDefault").GetEntityValue(entity));
        }

        // ── The decision that reaches the router ────────────────────────────────

        [TestMethod]
        public void AnUntouchedPropertyCountsAsHavingItsDefault()
        {
            var fresh = new SampleEntity();

            Assert.IsTrue(Accessor("PlainBool").HasDefaultValue(fresh));
            Assert.IsTrue(Accessor("PlainInt").HasDefaultValue(fresh));
            Assert.IsTrue(Accessor("PlainString").HasDefaultValue(fresh));
            Assert.IsTrue(Accessor("StringWithDefault").HasDefaultValue(fresh),
                "a null reference property takes its DefaultValue, so it equals it");
        }

        [TestMethod]
        public void ADefaultDeclaredAsTheRoutersRatherThanTheClrOneNeverMatches()
        {
            // A10 in one assertion, and the reason it cannot be fixed in the attribute: whichever of the two
            // states the property is in, one of them is misread.
            var fresh = new SampleEntity();
            Assert.IsFalse(Accessor("BoolDefaultingOn").HasDefaultValue(fresh),
                "untouched (false) does not match DefaultValue \"yes\" — so the field is sent on every /add");

            var explicitlyOff = new SampleEntity { BoolDefaultingOn = false };
            Assert.IsFalse(Accessor("BoolDefaultingOn").HasDefaultValue(explicitlyOff),
                "and an explicit false is indistinguishable from untouched");

            var fresh2 = new SampleEntity();
            Assert.IsTrue(Accessor("PlainInt").HasDefaultValue(fresh2));
            Assert.IsFalse(Accessor("IntWithDefault").HasDefaultValue(fresh2),
                "same shape for an int whose declared default is not 0 — it sends 0 on every /add");
        }

        [TestMethod]
        public void SettingAPropertyToItsDefaultIsIndistinguishableFromLeavingItAlone()
        {
            var untouched = new SampleEntity();
            var assigned = new SampleEntity { PlainBool = false, PlainInt = 0, PlainString = "" };

            var metadata = TikEntityMetadataCache.GetMetadata<SampleEntity>();
            foreach (var property in metadata.Properties.Where(p => !p.IsReadOnly))
                Assert.AreEqual(property.HasDefaultValue(untouched), property.HasDefaultValue(assigned),
                    property.PropertyName + " must answer the same either way — there is no notion of "
                    + "'assigned' today, which is what B4 introduces");
        }

        // ── What a nullable property adds (B4) ──────────────────────────────────

        [TestMethod]
        public void ANullablePropertyWithNoDeclaredDefaultHasNoDefaultAtAll()
        {
            // Not "no", which is what the CLR default of the underlying type would give — that would put the
            // conflation straight back.
            Assert.IsNull(NullableAccessor("NoDeclaredDefault").DefaultValue);
        }

        [TestMethod]
        public void AnUnassignedNullablePropertyReadsBackAsNullRatherThanAsItsDefault()
        {
            var fresh = new NullableEntity();

            Assert.IsNull(NullableAccessor("DefaultsOn").GetEntityValue(fresh));
            Assert.IsNull(NullableAccessor("OptionalPort").GetEntityValue(fresh));
            Assert.IsTrue(NullableAccessor("DefaultsOn").HasDefaultValue(fresh),
                "nothing to say still counts as 'no need to send it'");
        }

        [TestMethod]
        public void AnAssignedNullablePropertySerializesLikeItsUnderlyingType()
        {
            var entity = new NullableEntity { DefaultsOn = false, OptionalPort = 8291 };

            Assert.AreEqual("no", NullableAccessor("DefaultsOn").GetEntityValue(entity));
            Assert.AreEqual("8291", NullableAccessor("OptionalPort").GetEntityValue(entity));

            entity.DefaultsOn = true;
            Assert.AreEqual("yes", NullableAccessor("DefaultsOn").GetEntityValue(entity));
        }

        [TestMethod]
        public void ANullablePropertyTellsAnExplicitFalseApartFromSilence()
        {
            // A10, resolved. On a non-nullable bool these two are the same state (asserted above), which is
            // why no DefaultValue could ever be right for it.
            var untouched = new NullableEntity();
            var explicitlyOff = new NullableEntity { DefaultsOn = false };
            var explicitlyOn = new NullableEntity { DefaultsOn = true };

            var accessor = NullableAccessor("DefaultsOn");

            Assert.IsTrue(accessor.HasDefaultValue(untouched), "silence: leave it out, let the router choose");
            Assert.IsFalse(accessor.HasDefaultValue(explicitlyOff), "an explicit false must reach the router");
            Assert.IsTrue(accessor.HasDefaultValue(explicitlyOn),
                "asking for what the router would do anyway need not be sent");
        }

        [TestMethod]
        public void DeserializingANullablePropertyKeepsAnAbsentFieldAbsent()
        {
            var entity = new NullableEntity();
            var accessor = NullableAccessor("DefaultsOn");

            accessor.SetEntityValue(entity, null);
            Assert.IsNull(entity.DefaultsOn, "the router did not report the field");

            accessor.SetEntityValue(entity, "yes");
            Assert.AreEqual(true, entity.DefaultsOn);

            accessor.SetEntityValue(entity, "no");
            Assert.AreEqual(false, entity.DefaultsOn);
        }

        // ── …and what that means on the wire ────────────────────────────────────

        [TestMethod]
        public void ACreateLeavesOutAnUnassignedNullableAndSendsAnExplicitOne()
        {
            var conn = new tik4net.Testing.TikFakeConnection()
                .WithScalarResponse(rows => rows.First() == "/test/nullable-entity/add", "*1");

            conn.Save(new NullableEntity { DefaultsOn = false });

            string sent = string.Join(" ", conn.SentCommands.Single());
            StringAssert.Contains(sent, "=defaults-on=no", "an explicitly assigned false has to reach the router");
            Assert.IsFalse(sent.Contains("no-declared-default"), "an unassigned nullable is not mentioned at all");
            Assert.IsFalse(sent.Contains("optional-port"), "nor is an unassigned int?");
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
            typeof(NullableEntity).GetProperty("Id").SetValue(entity, "*1");

            conn.Save(entity, usedFieldsFilter: new[] { "defaults-on", "no-declared-default", "optional-port" });

            string sent = string.Join(" | ", conn.SentCommands.Select(rows => string.Join(" ", rows)));
            StringAssert.Contains(sent, "=defaults-on=yes");
            Assert.IsFalse(sent.Contains("no-declared-default"), "silence is not an instruction");
            Assert.IsFalse(sent.Contains("unset"), "and it is certainly not an unset");
        }

        [TestMethod]
        public void ClearingALoadedFieldToNullUnsetsItOnTheRouter()
        {
            // The other half of null's meaning, and the reason the snapshot is consulted: a field that was
            // loaded WITH a value and is now null was cleared by the caller, and clearing is an instruction.
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
            Assert.AreEqual(false, loaded.DefaultsOn, "precondition: it came back with a value");

            loaded.DefaultsOn = null;
            conn.Save(loaded);

            string sent = string.Join(" | ", conn.SentCommands.Select(rows => string.Join(" ", rows)));
            StringAssert.Contains(sent, "/test/nullable-entity/unset");
            StringAssert.Contains(sent, "=value-name=defaults-on");
            Assert.IsFalse(sent.Contains("=defaults-on="), "cleared, so it must not also be set");
        }

        [TestMethod]
        public void AFieldThatWasNullWhenLoadedAndIsStillNullIsNotUnset()
        {
            // A partial load leaves fields null that the router never reported. Re-saving must not read that
            // as an instruction to clear them.
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

        // ── Round trip ──────────────────────────────────────────────────────────

        [TestMethod]
        public void DeserializingAcceptsBothSpellingsTheRouterUses()
        {
            var entity = new SampleEntity();
            var accessor = Accessor("PlainBool");

            accessor.SetEntityValue(entity, "yes");
            Assert.IsTrue(entity.PlainBool, "writable fields come back as yes/no");

            accessor.SetEntityValue(entity, "true");
            Assert.IsTrue(entity.PlainBool, "read-only fields come back as true/false");

            accessor.SetEntityValue(entity, "no");
            Assert.IsFalse(entity.PlainBool);

            accessor.SetEntityValue(entity, "");
            Assert.IsFalse(entity.PlainBool, "an empty value is false — this is what makes a valueless "
                                           + "presence flag read back wrong");
        }
    }
}
