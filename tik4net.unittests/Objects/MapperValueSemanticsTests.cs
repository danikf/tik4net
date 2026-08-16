using System;
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

        private static TikEntityPropertyAccessor Accessor(string propertyName)
            => TikEntityMetadataCache.GetMetadata<SampleEntity>()
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
