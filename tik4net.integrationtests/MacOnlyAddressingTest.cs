// MacOnlyAddressingTest.cs — can the MAC transports reach the router with no IP address at all?
//
// That is what they exist for: a freshly reset device, or one whose addressing you are about to lock
// yourself out of. Until this test the library could not do it — every MAC session sent its DATA and ACK
// packets unicast to an IP taken from configuration, so a router without one was unreachable by the very
// transports meant for it. The protocol says otherwise (Docs/mactelnet-protocol.md, "UDP communication"):
// broadcast, then latch onto whatever address the router's first reply comes from, if any.
//
// Since TestBase.LabAddress addresses the MAC transports by MAC alone, a mac* run exercises this path
// throughout. This class earns its place on the OTHER runsettings: under api/rest/telnet/... it is the
// only thing that opens a MAC transport with no host, so the path stays covered by every run rather than
// by the three that select a MAC transport.
//
// What this test can and cannot establish against the lab router, which HAS an IP:
//
//   * covered — the whole MAC-only code path: no host in the setup, the local NIC found by rotating
//     candidates until one is answered, and the session running on broadcast until the latch.
//   * NOT covered — a router that answers from 0.0.0.0 because it has no address, where the latch never
//     happens and the session stays on broadcast for its whole life. Reaching that needs the lab router's
//     IP removed, which cannot be done from inside the suite that is talking to it over IP.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Configuration;
using System.Linq;
using tik4net;

namespace tik4net.integrationtests
{
    [TestClass]
    public class MacOnlyAddressingTest : TestBase
    {
        /// <summary>
        /// The MAC of the lab router. Without it there is nothing to address a MAC-only session by — MNDP
        /// cannot help, because looking a MAC up by MNDP needs the host address this test refuses to use.
        /// </summary>
        private static string RouterMac => ConfigurationManager.AppSettings["routerMac"];

        private static string Host => ConfigurationManager.AppSettings["host"];
        private static string User => ConfigurationManager.AppSettings["user"];
        private static string Password => ConfigurationManager.AppSettings["pass"] ?? "";

        private static TikConnectionType[] MacTransports => new[]
        {
            TikConnectionType.MacTelnet,
            TikConnectionType.WinboxCliMac,
            TikConnectionType.WinboxNativeMac,
        };

        /// <summary>
        /// Opens each MAC transport addressed by MAC alone and reads the router's identity over it,
        /// comparing against what the suite's own connection says. A session that opened but talked to
        /// the wrong router, or to nobody, fails on the value rather than passing on the absence of an
        /// exception.
        /// </summary>
        [TestMethod]
        public void MacTransportsWillConnectWithoutAnyHostAddress()
        {
            if (string.IsNullOrEmpty(RouterMac))
                Assert.Inconclusive("No routerMac in App.config — a MAC-only session has nothing to address.");

            string expectedIdentity = ReadIdentity(Connection);

            foreach (var type in MacTransports)
            {
                var setup = new TikConnectionSetup(TikRouterAddress.FromMac(RouterMac), User, Password)
                {
                    ConnectTimeout = TimeSpan.FromSeconds(30),   // a NIC rotation costs a SESSIONSTART round each
                };

                using (var conn = setup.Create(type))
                    Assert.AreEqual(expectedIdentity, ReadIdentity(conn),
                        type + " connected by MAC alone but did not reach the expected router.");
            }
        }

        /// <summary>
        /// The same three transports still work when the host is supplied as well — the case where the
        /// host names the local interface and the MAC saves the MNDP wait. Here so that the MAC-only path
        /// cannot be made to work by breaking the ordinary one.
        /// </summary>
        [TestMethod]
        public void MacTransportsStillConnectWithHostAndMacTogether()
        {
            if (string.IsNullOrEmpty(RouterMac))
                Assert.Inconclusive("No routerMac in App.config.");

            string expectedIdentity = ReadIdentity(Connection);

            foreach (var type in MacTransports)
            {
                var setup = new TikConnectionSetup(
                    TikRouterAddress.FromHostAndMac(Host, RouterMac), User, Password);

                using (var conn = setup.Create(type))
                    Assert.AreEqual(expectedIdentity, ReadIdentity(conn), type.ToString());
            }
        }

        /// <summary>
        /// An IP transport asked to use a MAC-only setup must say so at <c>Create</c>, naming the missing
        /// coordinate — not fail later inside a socket call that cannot.
        /// </summary>
        [TestMethod]
        public void AnIpTransportRefusesAMacOnlySetup()
        {
            var setup = new TikConnectionSetup(
                TikRouterAddress.FromMac(RouterMac ?? "AA:BB:CC:DD:EE:FF"), User, Password);

            var ex = Assert.ThrowsException<InvalidOperationException>(
                () => setup.CreateUnopened(TikConnectionType.Api));
            StringAssert.Contains(ex.Message, "host");
        }

        private static string ReadIdentity(ITikConnection conn)
            => conn.CreateCommandAndParameters("/system/identity/print")
                   .ExecuteList()
                   .Select(row => row.GetResponseField("name"))
                   .Single();
    }
}
