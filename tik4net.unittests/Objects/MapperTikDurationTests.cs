using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// The mapper path for a <see cref="TikDuration"/>-typed property — <c>ConvertFromString</c>,
    /// <c>ConvertToString</c> and <see cref="TikEntityPropertyAccessor.HasDefaultValue"/> at the point where
    /// they branch on <c>ValueType == typeof(TikDuration)</c> — none of which any test exercised before this
    /// file. <see cref="TikDurationTests"/> covers the struct itself thoroughly; it never goes through
    /// <see cref="TikEntityPropertyAccessor"/>, so the 24 entity fields already declared <c>TikDuration?</c>
    /// rested on untested wiring.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The point of <see cref="TikDuration"/> is that the router's two spellings of the same duration —
    /// <c>10s</c> over the API/REST/native WinBox and <c>00:00:10</c> over the CLI transports, which read
    /// <c>print as-value</c> — read back as the SAME value. A test that only ever feeds the mapper one
    /// spelling could not tell a real fix from one that merely stopped throwing on the other, so every
    /// round-trip case here is asserted from both spellings.
    /// </para>
    /// <para>
    /// Modelled on <see cref="MapperValueSemanticsTests"/>: a small entity local to this file, one shape
    /// exercised per test, comments that say why the case matters rather than what the code does.
    /// </para>
    /// </remarks>
    [TestClass]
    public class MapperTikDurationTests
    {
        [TikEntity("/test/duration-entity")]
        internal class DurationEntity
        {
            [TikProperty(".id", IsReadOnly = true)]
            public string Id { get; private set; }

            [TikProperty("plain-duration")]
            public TikDuration? PlainDuration { get; set; }

            // The declared default is written in the CLOCK form on purpose — several shipped entities do
            // this (e.g. SystemScheduler's DefaultValue="00:00:00") — while the router and the caller both
            // deal in the compact form on every other transport. NormalizeDefaultValue exists to make the
            // two comparable.
            [TikProperty("duration-with-clock-default", DefaultValue = "00:05:00")]
            public TikDuration? DurationWithClockDefault { get; set; }
        }

        private static TikEntityPropertyAccessor Accessor(string propertyName)
            => TikEntityMetadataCache.GetMetadata<DurationEntity>()
                .Properties.Single(p => p.PropertyName == propertyName);

        // ── Deserialization: both router spellings land on the same value ───────

        [TestMethod]
        public void CompactAndClockFormsOfTheSameDurationDeserializeEqual()
        {
            var accessor = Accessor("PlainDuration");

            var viaCompact = new DurationEntity();
            var viaClock = new DurationEntity();

            accessor.SetEntityValue(viaCompact, "10s");
            accessor.SetEntityValue(viaClock, "00:00:10");
            Assert.AreEqual(viaCompact.PlainDuration, viaClock.PlainDuration,
                "same router field, same moment, different transport — this is the entire reason the type exists");

            accessor.SetEntityValue(viaCompact, "200ms");
            accessor.SetEntityValue(viaClock, "00:00:00.200");
            Assert.AreEqual(viaCompact.PlainDuration, viaClock.PlainDuration, "sub-second: the CLI's fraction is milliseconds");

            accessor.SetEntityValue(viaCompact, "1d");
            accessor.SetEntityValue(viaClock, "1d00:00:00");
            Assert.AreEqual(viaCompact.PlainDuration, viaClock.PlainDuration, "the CLI's day prefix");
        }

        // ── Serialization: always the compact form, on every transport ─────────

        [TestMethod]
        public void ASetDurationSerializesToTheCompactFormRegardlessOfWhichFormItWasReadFrom()
        {
            var accessor = Accessor("PlainDuration");
            var entity = new DurationEntity();

            accessor.SetEntityValue(entity, "00:00:10"); // read the CLI's spelling in...
            Assert.AreEqual("10s", accessor.GetEntityValue(entity), "...and write back what every transport accepts");

            accessor.SetEntityValue(entity, "1d00:00:00");
            Assert.AreEqual("1d", accessor.GetEntityValue(entity));
        }

        // ── The router's words survive instead of being flattened to zero ──────

        [TestMethod]
        public void TheRoutersNonNumericWordsRoundTripVerbatim()
        {
            var accessor = Accessor("PlainDuration");

            foreach (string word in new[] { "none", "disabled", "auto" })
            {
                var entity = new DurationEntity();
                accessor.SetEntityValue(entity, word);

                Assert.IsFalse(entity.PlainDuration!.Value.HasValue,
                    $"'{word}' is not zero time, it is the router saying the feature is off");
                Assert.AreEqual(word, accessor.GetEntityValue(entity),
                    "and it has to go back out exactly as the router sent it, not as a normalized spelling");
            }
        }

        // ── Absence stays absence ───────────────────────────────────────────────

        [TestMethod]
        public void AnAbsentFieldLeavesThePropertyNullNotTimeSpanZero()
        {
            var accessor = Accessor("PlainDuration");
            var entity = new DurationEntity();

            accessor.SetEntityValue(entity, null);

            Assert.IsNull(entity.PlainDuration, "the router did not report this field — that is not the same "
                                               + "state as a real zero-length duration");
        }

        [TestMethod]
        public void ANullDurationIsNotSent()
        {
            var accessor = Accessor("PlainDuration");
            var entity = new DurationEntity();

            Assert.IsNull(accessor.GetEntityValue(entity), "nothing to say about it, so nothing is sent");
            Assert.IsTrue(accessor.HasDefaultValue(entity), "and Save must not treat silence as a change");
        }

        // ── DefaultValue comparison works across the two spellings ─────────────

        [TestMethod]
        public void ADefaultDeclaredInClockFormIsNormalizedToTheCompactFormOnce()
        {
            // Otherwise every comparison below would fail even though the value the router reports and the
            // declared default are the same duration — the comparison is a plain string compare downstream.
            Assert.AreEqual("5m", Accessor("DurationWithClockDefault").DefaultValue,
                "the declared \"00:05:00\" is normalized to the compact spelling once, at metadata build time");
        }

        [TestMethod]
        public void ADefaultValuedDurationCountsAsUnchangedRegardlessOfWhichFormItArrivedIn()
        {
            var accessor = Accessor("DurationWithClockDefault");

            // This is "Save sees a default-valued field as changed" made concrete: a field sitting at the
            // router's default must not become CHANGED merely because this load happened to come from the
            // CLI (clock form) instead of the API (compact form), or vice-versa.
            var loadedViaApi = new DurationEntity();
            accessor.SetEntityValue(loadedViaApi, "5m");
            Assert.IsTrue(accessor.HasDefaultValue(loadedViaApi), "compact-form default reads back as default");

            var loadedViaCli = new DurationEntity();
            accessor.SetEntityValue(loadedViaCli, "00:05:00");
            Assert.IsTrue(accessor.HasDefaultValue(loadedViaCli), "clock-form default reads back as default too");

            var changed = new DurationEntity();
            accessor.SetEntityValue(changed, "10m");
            Assert.IsFalse(accessor.HasDefaultValue(changed), "a real change is still detected as one");
        }
    }
}
