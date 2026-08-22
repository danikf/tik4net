using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;
using tik4net.Ssh;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// Which router coordinate a setup must carry is a property of the <b>transport</b>, not of the setup —
    /// so the check belongs at <c>Create</c>, and this fixture asserts it there, in both directions: every
    /// IP transport refuses a MAC-only setup, and every MAC transport accepts one.
    /// </summary>
    /// <remarks>
    /// The "accepts" direction is the one that would otherwise rot silently. A MAC-only setup that is
    /// rejected fails loudly; one that is accepted and then quietly ignored — the router addressed by an
    /// empty host — fails five layers down in a socket call.
    /// </remarks>
    [TestClass]
    public class TikConnectionSetupAddressTests
    {
        private static readonly TikConnectionType[] IpTransports =
        {
            TikConnectionType.Api,
            TikConnectionType.ApiSsl,
            TikConnectionType.Rest,
            TikConnectionType.RestSsl,
            TikConnectionType.Telnet,
            TikConnectionType.Ssh,
            TikConnectionType.WinboxCli,
            TikConnectionType.WinboxNative,
        };

        private static readonly TikConnectionType[] MacTransports =
        {
            TikConnectionType.MacTelnet,
            TikConnectionType.WinboxCliMac,
            TikConnectionType.WinboxNativeMac,
        };

        [ClassInitialize]
        public static void RegisterSatelliteTransports(TestContext context) => Tik4NetSsh.Register();

        [TestMethod]
        public void AMacOnlySetupIsRefusedByEveryIpTransport()
        {
            var setup = new TikConnectionSetup(TikRouterAddress.FromMac("AA:BB:CC:DD:EE:FF"), "user", "pwd");
            foreach (var type in IpTransports)
            {
                var ex = Assert.ThrowsException<InvalidOperationException>(
                    () => setup.CreateUnopened(type), type.ToString());
                // The message has to name what is missing; "connection failed" would send the caller to
                // the network rather than to the setup.
                StringAssert.Contains(ex.Message, "host", type.ToString());
            }
        }

        [TestMethod]
        public void AMacOnlySetupIsAcceptedByEveryMacTransportAndReachesItAsTheRouterMac()
        {
            var setup = new TikConnectionSetup(TikRouterAddress.FromMac("AA:BB:CC:DD:EE:FF"), "user", "pwd");
            foreach (var type in MacTransports)
            {
                using (var conn = setup.CreateUnopened(type))
                    Assert.AreEqual("AA:BB:CC:DD:EE:FF", ((ITikMacLayerConnection)conn).RouterMac, type.ToString());
            }
        }

        [TestMethod]
        public void AHostOnlySetupIsAcceptedEverywhere()
        {
            var setup = new TikConnectionSetup("192.0.2.1", "user", "pwd");
            foreach (var type in IpTransports)
                setup.CreateUnopened(type).Dispose();
            foreach (var type in MacTransports)
                setup.CreateUnopened(type).Dispose();
        }

        [TestMethod]
        public void TheRouterMacOptionOverridesTheAddress()
        {
            var setup = new TikConnectionSetup(
                TikRouterAddress.FromHostAndMac("192.0.2.1", "AA:BB:CC:DD:EE:FF"), "user", "pwd")
            {
                RouterMac = "11:22:33:44:55:66",
            };

            using (var conn = setup.CreateUnopened(TikConnectionType.MacTelnet))
                Assert.AreEqual("11:22:33:44:55:66", ((ITikMacLayerConnection)conn).RouterMac);
        }

        [TestMethod]
        public void AMacFromTheAddressReachesTheConnectionWithoutTheOption()
        {
            var setup = new TikConnectionSetup(
                TikRouterAddress.FromHostAndMac("192.0.2.1", "AA:BB:CC:DD:EE:FF"), "user", "pwd");

            using (var conn = setup.CreateUnopened(TikConnectionType.WinboxNativeMac))
                Assert.AreEqual("AA:BB:CC:DD:EE:FF", ((ITikMacLayerConnection)conn).RouterMac);
        }

        [TestMethod]
        public void AnAddressWithNeitherCoordinateIsRefusedAtTheConstructor()
        {
            // default(TikRouterAddress) is the only way to get here, and it names no router at all.
            Assert.ThrowsException<ArgumentException>(
                () => new TikConnectionSetup(default(TikRouterAddress), "user", "pwd"));
        }
    }
}
