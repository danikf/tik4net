using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;
using tik4net.Objects;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// G5: what the mapper puts on the wire must not depend on the thread's culture. Digits are the same
    /// everywhere that matters; the <b>negative sign</b> is not. A handful of cultures (sv-SE, fi-FI) render
    /// minus as U+2212 MINUS SIGN, which RouterOS will not parse — and in the other direction a value the
    /// router sent with U+002D fails to parse back.
    /// </summary>
    /// <remarks>
    /// Not hypothetical arithmetic: <c>/system/ntp/client</c> reports a negative <c>system-offset</c> and
    /// <c>freq-drift</c>, and a route distance or a signal strength is signed too. The B5 types
    /// (<c>uint</c>/<c>ulong</c>/<c>DateTime</c>) already pinned <see cref="CultureInfo.InvariantCulture"/>;
    /// <c>int</c>, <c>long</c> and <c>byte</c> predate that and did not.
    /// </remarks>
    [TestClass]
    public class CultureIndependenceTests
    {
        [TikEntity("/test/signed-entity")]
        internal class SignedEntity
        {
            [TikProperty("offset")]
            public int Offset { get; set; }

            [TikProperty("big-offset")]
            public long BigOffset { get; set; }

            [TikProperty("distance")]
            public byte Distance { get; set; }
        }

        private static TikEntityPropertyAccessor Accessor(string propertyName)
            => TikEntityMetadataCache.GetMetadata<SignedEntity>()
                .Properties.Single(p => p.PropertyName == propertyName);

        /// <summary>
        /// Runs <paramref name="body"/> under a culture whose negative sign is U+2212, restoring the
        /// thread's own culture afterwards whatever happens.
        /// </summary>
        private static void InMinusSignCulture(Action body)
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                var culture = (CultureInfo)CultureInfo.GetCultureInfo("sv-SE").Clone();
                // Asserted rather than assumed: ICU has changed sv-SE's sign before, and a test that
                // silently ran under U+002D would prove nothing at all.
                culture.NumberFormat.NegativeSign = "−";
                Thread.CurrentThread.CurrentCulture = culture;
                Assert.AreEqual("−", CultureInfo.CurrentCulture.NumberFormat.NegativeSign,
                    "the test culture must actually use U+2212, or this measures nothing");
                body();
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [TestMethod]
        public void ANegativeIntGoesToTheWireWithAnAsciiMinus()
        {
            InMinusSignCulture(() =>
            {
                var entity = new SignedEntity { Offset = -970 };
                Assert.AreEqual("-970", Accessor("Offset").GetEntityValue(entity),
                    "RouterOS parses U+002D and nothing else — a U+2212 here is a value the router rejects");
            });
        }

        [TestMethod]
        public void ANegativeLongGoesToTheWireWithAnAsciiMinus()
        {
            InMinusSignCulture(() =>
            {
                var entity = new SignedEntity { BigOffset = -5000000000L };
                Assert.AreEqual("-5000000000", Accessor("BigOffset").GetEntityValue(entity));
            });
        }

        /// <summary>
        /// A pin, not evidence: this one passes against the unfixed code too. The plan item expected the read
        /// direction to be broken as well, and it is not — .NET's number parser accepts a leading U+002D
        /// whatever the culture's own <c>NegativeSign</c> is, so only the FORMATTING side ever produced a
        /// value RouterOS could not read. Pinning it anyway costs nothing and states which half was real.
        /// </summary>
        [TestMethod]
        public void ANegativeIntFromTheRouterIsReadBackUnderAnyCulture()
        {
            InMinusSignCulture(() =>
            {
                var entity = new SignedEntity();
                Accessor("Offset").SetEntityValue(entity, "-970");
                Assert.AreEqual(-970, entity.Offset,
                    "the router sends U+002D, so parsing must not be looking for the culture's sign");

                Accessor("BigOffset").SetEntityValue(entity, "-5000000000");
                Assert.AreEqual(-5000000000L, entity.BigOffset);
            });
        }

        /// <summary>
        /// A <c>byte</c> property parses on the way in and had no branch at all on the way out, so writing
        /// one fell through to the converter lookup and threw. <c>InterfacePppoeClient.DefaultRouteDistance</c>
        /// is a live example of the type.
        /// </summary>
        [TestMethod]
        public void AByteRoundTrips()
        {
            var entity = new SignedEntity();
            Accessor("Distance").SetEntityValue(entity, "10");
            Assert.AreEqual((byte)10, entity.Distance);
            Assert.AreEqual("10", Accessor("Distance").GetEntityValue(entity),
                "a byte the mapper can read must also be one it can write");
        }
    }
}
