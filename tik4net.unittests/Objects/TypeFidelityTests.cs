using System;
using System.Globalization;
using System.Linq;
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;
using tik4net.Objects;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// B5: the property types the mapper understands (<c>uint</c>, <c>ulong</c>, <c>DateTime</c>,
    /// <see cref="MacAddress"/>), and <see cref="ITikTypeConverter"/> for the ones it does not.
    /// </summary>
    /// <remarks>
    /// Every method here fails against the old code with <see cref="NotImplementedException"/> — these are
    /// types the mapper had no branch for — so unlike the B1/B2 files this one is evidence rather than a pin.
    /// The RouterOS date formats are not invented: they were read off a live 7.23 router across
    /// <c>/system/clock</c>, <c>/system/resource</c>, <c>/certificate</c> and <c>/log</c>, and the accepted
    /// INPUT format was confirmed by filtering a query on a supplied date. See <see cref="TikDateTimeHelper"/>.
    /// </remarks>
    [TestClass]
    public class TypeFidelityTests
    {
        [TikEntity("/test/typed-entity")]
        internal class TypedEntity
        {
            [TikProperty("counter")]
            public uint Counter { get; set; }

            [TikProperty("bytes")]
            public ulong Bytes { get; set; }

            [TikProperty("when")]
            public DateTime When { get; set; }

            [TikProperty("when-optional")]
            public DateTime? WhenOptional { get; set; }

            [TikProperty("mac")]
            public MacAddress Mac { get; set; }

            [TikProperty("text")]
            public string Text { get; set; }
        }

        [TikEntity("/test/unsupported-entity")]
        internal class UnsupportedEntity
        {
            [TikProperty("endpoint")]
            public IPAddress Endpoint { get; set; }
        }

        private static TikEntityPropertyAccessor Accessor(string propertyName)
            => TikEntityMetadataCache.GetMetadata<TypedEntity>()
                .Properties.Single(p => p.PropertyName == propertyName);

        // ── The widened numeric range ───────────────────────────────────────────

        [TestMethod]
        public void AUintCarriesValuesAnIntCannot()
        {
            var entity = new TypedEntity();

            // The point of the type: 3000000000 does not fit in an int, and RouterOS counters reach it.
            Accessor("Counter").SetEntityValue(entity, "3000000000");
            Assert.AreEqual(3000000000u, entity.Counter);
            Assert.AreEqual("3000000000", Accessor("Counter").GetEntityValue(entity));
        }

        [TestMethod]
        public void AUlongCarriesValuesALongCannot()
        {
            var entity = new TypedEntity();

            Accessor("Bytes").SetEntityValue(entity, "18446744073709551615");
            Assert.AreEqual(ulong.MaxValue, entity.Bytes);
            Assert.AreEqual("18446744073709551615", Accessor("Bytes").GetEntityValue(entity));
        }

        // ── Dates ───────────────────────────────────────────────────────────────

        [TestMethod]
        public void ADateTimeReadsTheFormatRouterOs7Prints()
        {
            var entity = new TypedEntity();

            Accessor("When").SetEntityValue(entity, "2026-07-25 10:24:52");
            Assert.AreEqual(new DateTime(2026, 7, 25, 10, 24, 52), entity.When);
        }

        [TestMethod]
        public void ADateTimeIsWrittenInTheFormatRouterOsAccepts()
        {
            var entity = new TypedEntity { When = new DateTime(2026, 7, 25, 10, 24, 52) };

            // Measured on 7.23: this shape filters correctly as an INPUT value, and 'jul/25/2026' does not —
            // it comes back with the wrong rows rather than a trap, so writing the legacy shape fails silently.
            Assert.AreEqual("2026-07-25 10:24:52", Accessor("When").GetEntityValue(entity));
        }

        [TestMethod]
        public void ADateTimeStillReadsTheLegacyRouterOs6Format()
        {
            var entity = new TypedEntity();

            Accessor("When").SetEntityValue(entity, "jul/25/2026 10:24:52");
            Assert.AreEqual(new DateTime(2026, 7, 25, 10, 24, 52), entity.When);

            Accessor("When").SetEntityValue(entity, "2026-08-16");
            Assert.AreEqual(new DateTime(2026, 8, 16), entity.When, "a date alone — /system/clock prints one");
        }

        [TestMethod]
        public void ADateTimeCarriesNoTimeZone()
        {
            var entity = new TypedEntity();

            // The router's string has no zone, and what it MEANS is per field: /system/clock is local time,
            // a certificate's invalid-before is UTC. Applying either would corrupt the other.
            Accessor("When").SetEntityValue(entity, "2026-07-25 10:24:52");
            Assert.AreEqual(DateTimeKind.Unspecified, entity.When.Kind);
        }

        [TestMethod]
        public void AnUnparseableDateNamesTheProperty()
        {
            var ex = Assert.ThrowsException<FormatException>(
                () => Accessor("When").SetEntityValue(new TypedEntity(), "not-a-date"));

            StringAssert.Contains(ex.Message, "When(when)");
        }

        [TestMethod]
        public void ANullableDateTimeKeepsItsNull()
        {
            var entity = new TypedEntity();

            Assert.IsNull(Accessor("WhenOptional").GetEntityValue(entity));

            Accessor("WhenOptional").SetEntityValue(entity, "2026-07-25 10:24:52");
            Assert.AreEqual(new DateTime(2026, 7, 25, 10, 24, 52), entity.WhenOptional);
        }

        [TestMethod]
        public void TheDateHelperIsCultureIndependent()
        {
            // A machine whose culture writes dates differently must still speak the router's format. The
            // helper pins InvariantCulture; this is what would catch someone "simplifying" that away.
            var original = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo("cs-CZ");

                Assert.AreEqual("2026-07-25 10:24:52",
                    TikDateTimeHelper.ToTikDateTime(new DateTime(2026, 7, 25, 10, 24, 52)));
                Assert.AreEqual(new DateTime(2026, 7, 25),
                    TikDateTimeHelper.FromTikDateTime("2026-07-25"));
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = original;
            }
        }

        // ── MacAddress ──────────────────────────────────────────────────────────

        [TestMethod]
        public void AMacAddressRoundTrips()
        {
            var entity = new TypedEntity();

            Accessor("Mac").SetEntityValue(entity, "AA:BB:CC:DD:EE:FF");
            Assert.AreEqual("AA:BB:CC:DD:EE:FF", entity.Mac.Address);
            Assert.AreEqual("AA:BB:CC:DD:EE:FF", Accessor("Mac").GetEntityValue(entity));
        }

        [TestMethod]
        public void AnInvalidMacIsRejectedByTheTypeThatValidatesIt()
        {
            var ex = Assert.ThrowsException<FormatException>(
                () => Accessor("Mac").SetEntityValue(new TypedEntity(), "not-a-mac"));

            StringAssert.Contains(ex.Message, "Mac(mac)");
        }

        // ── The extension point ─────────────────────────────────────────────────

        private sealed class IPAddressConverter : ITikTypeConverter
        {
            public bool CanConvert(Type type) => type == typeof(IPAddress);
            public object ConvertFromString(string value, Type targetType) => IPAddress.Parse(value);
            public string ConvertToString(object value, Type sourceType) => ((IPAddress)value).ToString();
        }

        private sealed class GreedyConverter : ITikTypeConverter
        {
            public bool CanConvert(Type type) => true;
            public object ConvertFromString(string value, Type targetType) => throw new InvalidOperationException("must never be reached");
            public string ConvertToString(object value, Type sourceType) => throw new InvalidOperationException("must never be reached");
        }

        [TestMethod]
        public void AnUnsupportedTypeWithNoConverterSaysWhatToDoAboutIt()
        {
            var metadata = new TikEntityMetadata(typeof(UnsupportedEntity));
            var accessor = metadata.Properties.Single();

            var ex = Assert.ThrowsException<NotImplementedException>(
                () => accessor.SetEntityValue(new UnsupportedEntity(), "10.0.0.1"));

            StringAssert.Contains(ex.Message, "ITikTypeConverter");
        }

        [TestMethod]
        public void ARegisteredConverterHandlesATypeTheMapperDoesNot()
        {
            var converter = new IPAddressConverter();
            TikTypeConverters.Register(converter);
            try
            {
                var accessor = new TikEntityMetadata(typeof(UnsupportedEntity)).Properties.Single();
                var entity = new UnsupportedEntity();

                accessor.SetEntityValue(entity, "10.0.0.1");
                Assert.AreEqual(IPAddress.Parse("10.0.0.1"), entity.Endpoint);
                Assert.AreEqual("10.0.0.1", accessor.GetEntityValue(entity));
            }
            finally
            {
                TikTypeConverters.Unregister(converter);
            }
        }

        [TestMethod]
        public void AConverterRegisteredAfterTheEntityWasUsedStillTakesEffect()
        {
            // Entity metadata is cached for the life of the process, so resolving the converter in the
            // accessor's ctor would make a late registration a silent no-op. It resolves on first conversion
            // instead, and only for a type no built-in claimed.
            var accessor = new TikEntityMetadata(typeof(UnsupportedEntity)).Properties.Single();
            var entity = new UnsupportedEntity();

            Assert.ThrowsException<NotImplementedException>(() => accessor.SetEntityValue(entity, "10.0.0.1"));

            var converter = new IPAddressConverter();
            TikTypeConverters.Register(converter);
            try
            {
                accessor.SetEntityValue(entity, "10.0.0.1");
                Assert.AreEqual(IPAddress.Parse("10.0.0.1"), entity.Endpoint);
            }
            finally
            {
                TikTypeConverters.Unregister(converter);
            }
        }

        [TestMethod]
        public void AConverterCannotTakeOverABuiltInType()
        {
            // A converter answering CanConvert for everything must not be reached for string/int/DateTime —
            // it would silently re-route every entity in the process, including ones it knows nothing about,
            // and the damage would show up as wrong data on the router rather than as an error.
            var greedy = new GreedyConverter();
            TikTypeConverters.Register(greedy);
            try
            {
                var entity = new TypedEntity { Text = "ether1", When = new DateTime(2026, 7, 25, 10, 24, 52) };

                Assert.AreEqual("ether1", Accessor("Text").GetEntityValue(entity));
                Assert.AreEqual("2026-07-25 10:24:52", Accessor("When").GetEntityValue(entity));

                Accessor("Counter").SetEntityValue(entity, "42");
                Assert.AreEqual(42u, entity.Counter);
            }
            finally
            {
                TikTypeConverters.Unregister(greedy);
            }
        }

        [TestMethod]
        public void UnregisterReportsWhetherItRemovedAnything()
        {
            var converter = new IPAddressConverter();

            Assert.IsFalse(TikTypeConverters.Unregister(converter), "not registered yet");
            TikTypeConverters.Register(converter);
            Assert.IsTrue(TikTypeConverters.Registered.Contains(converter));
            Assert.IsTrue(TikTypeConverters.Unregister(converter));
            Assert.IsFalse(TikTypeConverters.Registered.Contains(converter));
        }
    }
}
