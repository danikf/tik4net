using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;

namespace tik4net.integrationtests
{
    /// <summary>
    /// <c>/ip/firewall/connection/tracking</c> — twelve <see cref="TikDuration"/> fields on one singleton,
    /// the largest block of them in the library and the reason this test exists.
    /// </summary>
    /// <remarks>
    /// These were <c>string?</c> until the duration conversion. The bug that change fixes is invisible on
    /// any single transport: the router answers <c>tcp-established-timeout=1d</c> over the API and
    /// <c>1d00:00:00</c> over a terminal, so a caller comparing two loads saw a change that had not
    /// happened, and <c>Save</c> would write fields nobody touched. The assertions below therefore check the
    /// PARSED value rather than the text — run the suite over the API and over Telnet and both must produce
    /// the same <see cref="System.TimeSpan"/>, which is the whole point.
    /// </remarks>
    [TestClass]
    public class ConnectionTrackingTest : TestBase
    {
        [TestMethod]
        public void LoadConnectionTrackingWillNotFail()
        {
            EnsureCommandAvailable("/ip/firewall/connection/tracking");

            var tracking = Connection.LoadSingle<ConnectionTracking>();

            Assert.IsNotNull(tracking);
        }

        [TestMethod]
        public void TheTimeoutsParseIntoRealDurationsOnEveryTransport()
        {
            EnsureCommandAvailable("/ip/firewall/connection/tracking");

            var tracking = Connection.LoadSingle<ConnectionTracking>();

            // RouterOS defaults, and they are durations rather than words on every box: if the transport's
            // spelling had not been understood these would be null (unparsed) instead.
            AssertIsRealDuration(tracking.TcpEstablishedTimeout, nameof(tracking.TcpEstablishedTimeout));
            AssertIsRealDuration(tracking.TcpSynSentTimeout, nameof(tracking.TcpSynSentTimeout));
            AssertIsRealDuration(tracking.UdpTimeout, nameof(tracking.UdpTimeout));
            AssertIsRealDuration(tracking.IcmpTimeout, nameof(tracking.IcmpTimeout));
            AssertIsRealDuration(tracking.GenericTimeout, nameof(tracking.GenericTimeout));

            // Pinned because it is the field whose two spellings differ most: '1d' vs '1d00:00:00'. A
            // transport that read only one of them would land somewhere else entirely.
            Assert.AreEqual(System.TimeSpan.FromDays(1), tracking.TcpEstablishedTimeout.Value.Value,
                "tcp-established-timeout is 1 day by default, whichever spelling this transport reads");
        }

        [TestMethod]
        public void ADurationSerializesBackToTheFormEveryTransportAccepts()
        {
            // Read-only check on a live value: whatever spelling arrived, writing it out again must give the
            // compact form, because that is the one RouterOS accepts on write over all of them. Nothing is
            // sent to the router here — this is the string Save WOULD produce.
            EnsureCommandAvailable("/ip/firewall/connection/tracking");

            var tracking = Connection.LoadSingle<ConnectionTracking>();

            Assert.AreEqual("1d", tracking.TcpEstablishedTimeout.ToString());
        }

        private static void AssertIsRealDuration(TikDuration? value, string name)
        {
            Assert.IsTrue(value.HasValue, $"{name} was not reported by the router at all");
            Assert.IsTrue(value.Value.HasValue,
                $"{name} came back as the word '{value.Value.Token}' rather than a length — if that is a "
                + "real router state the assertion needs widening, but on a default box it means the "
                + "transport's spelling was not parsed");
            Assert.IsTrue(value.Value.Value.Value > System.TimeSpan.Zero, $"{name} should be a positive length");
        }
    }
}
