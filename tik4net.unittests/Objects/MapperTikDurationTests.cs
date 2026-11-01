using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// A <see cref="TikDuration"/> field through <see cref="TikEntityPropertyAccessor"/>;
    /// <see cref="TikDurationTests"/> covers the struct itself.
    /// </summary>
    /// <remarks>
    /// The point of <see cref="TikDuration"/> is that the router's two spellings of the same duration —
    /// <c>10s</c> over the API/REST/native WinBox and <c>00:00:10</c> over the CLI transports, which read
    /// <c>print as-value</c> — read back as the SAME value. A test that only ever feeds the mapper one
    /// spelling could not tell a real fix from one that merely stopped throwing on the other, so every
    /// round-trip case here is asserted from both spellings.
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
            public TikField<TikDuration?> PlainDuration { get; set; }
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

                Assert.IsFalse(entity.PlainDuration.Value!.Value.HasValue,
                    $"'{word}' is not zero time, it is the router saying the feature is off");
                Assert.AreEqual(word, accessor.GetEntityValue(entity),
                    "and it has to go back out exactly as the router sent it, not as a normalized spelling");
            }
        }

        // ── A word the library does not know ────────────────────────────────────

        [TestMethod]
        public void AWordTheLibraryDoesNotKnow_IsKeptAsTheRoutersWord()
        {
            var accessor = Accessor("PlainDuration");
            var entity = new DurationEntity();

            accessor.SetEntityValue(entity, "someday");

            Assert.AreEqual(TikFieldState.Unparsed, entity.PlainDuration.State);
            Assert.AreEqual("someday", accessor.GetEntityValue(entity), "written back as it came");
        }

        // ── Absence stays absence ───────────────────────────────────────────────

        [TestMethod]
        public void AnAbsentFieldIsNotTimeSpanZero()
        {
            var accessor = Accessor("PlainDuration");
            var entity = new DurationEntity();

            accessor.SetEntityValue(entity, null);

            Assert.AreEqual(TikFieldState.Absent, entity.PlainDuration.State, "the router did not report this field — that is "
                                               + "not the same state as a real zero-length duration");
            Assert.IsNull(accessor.GetEntityValue(entity), "nothing to say about it, so nothing is sent");
        }
    }
}
