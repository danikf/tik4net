using System;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;
using tik4net.Ssh;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// <see cref="ConnectionFactory"/> is a front for <see cref="TikConnectionSetup"/>, not a second way of
    /// building a connection — these pin that it stays one.
    /// </summary>
    /// <remarks>
    /// The failure being guarded against is the one the class already had: each overload opened a connection
    /// by hand, so "what a connection carries" depended on which entry point produced it, and nothing said so.
    /// Two halves, and they are not equally strong evidence:
    /// <list type="bullet">
    /// <item><description>
    /// The <c>(type, setup)</c> overloads are new, so their tests could not have been run against the old
    /// code — there was no member to call. They pin behaviour rather than demonstrate a fix.
    /// </description></item>
    /// <item><description>
    /// <see cref="TheOptionlessOverloadsAgreeWithABareSetup"/> would have passed before the change too: the
    /// transports' own defaults and the setup's defaults already matched. That is exactly why it is worth
    /// writing down — the agreement is a coincidence of two independently maintained sets of literals until
    /// something checks it, and the XML docs on <c>CreateConnection(TikConnectionType)</c> now claim it.
    /// </description></item>
    /// </list>
    /// </remarks>
    [TestClass]
    public class ConnectionFactoryRoutesThroughSetupTests
    {
        [ClassInitialize]
        public static void RegisterSatelliteTransports(TestContext context)
            => Tik4NetSsh.Register();

        private static readonly TikConnectionType[] AllTransports =
        {
            TikConnectionType.Api, TikConnectionType.ApiSsl,
            TikConnectionType.Rest, TikConnectionType.RestSsl,
            TikConnectionType.Telnet, TikConnectionType.Ssh, TikConnectionType.MacTelnet,
            TikConnectionType.WinboxCli, TikConnectionType.WinboxCliMac,
            TikConnectionType.WinboxNative, TikConnectionType.WinboxNativeMac,
        };

        // Every value differs from the default it would otherwise take, so a value that failed to travel
        // cannot be mistaken for one that did.
        private static TikConnectionSetup NonDefaultSetup() => new TikConnectionSetup("192.0.2.1", "user", "pwd")
        {
            ConnectTimeout = TimeSpan.FromSeconds(7),
            ReceiveTimeout = TimeSpan.FromSeconds(11),
            SendTimeout = TimeSpan.FromSeconds(13),
            Encoding = Encoding.ASCII,
            DebugEnabled = true,
            RouterMac = "AA:BB:CC:DD:EE:FF",
        };

        [TestMethod]
        public void CreateConnectionWithASetupAppliesIt()
        {
            var setup = NonDefaultSetup();

            foreach (var type in AllTransports)
            {
                using (var conn = ConnectionFactory.CreateConnection(type, setup))
                {
                    Assert.AreEqual(7000, conn.ConnectTimeout, $"ConnectTimeout on {type}");
                    Assert.AreEqual(11000, conn.ReceiveTimeout, $"ReceiveTimeout on {type}");
                    Assert.AreEqual(13000, conn.SendTimeout, $"SendTimeout on {type}");
                    Assert.AreSame(Encoding.ASCII, conn.Encoding, $"Encoding on {type}");
                    Assert.IsTrue(conn.DebugEnabled, $"DebugEnabled on {type}");
                    Assert.IsFalse(conn.IsOpened, $"{type} must be created unopened");

                    if (conn is ITikMacLayerConnection mac)
                        Assert.AreEqual("AA:BB:CC:DD:EE:FF", mac.RouterMac, $"RouterMac on {type}");
                }
            }
        }

        /// <summary>
        /// The claim made by the XML docs on <c>CreateConnection(TikConnectionType)</c>: the overload that
        /// has no setup to apply leaves defaults that are the ones a setup would have applied anyway.
        /// </summary>
        [TestMethod]
        public void TheOptionlessOverloadsAgreeWithABareSetup()
        {
            var bare = new TikConnectionSetup("192.0.2.1", "user", "pwd");
            var divergences = new System.Collections.Generic.List<string>();

            void Compare(TikConnectionType type, string option, object setupValue, object factoryValue)
            {
                if (!Equals(setupValue, factoryValue))
                    divergences.Add($"{type}.{option}: setup={setupValue}, factory={factoryValue}");
            }

            foreach (var type in AllTransports)
            {
                using (var fromFactory = ConnectionFactory.CreateConnection(type))
                using (var fromSetup = bare.CreateUnopened(type))
                {
                    Compare(type, "ConnectTimeout", fromSetup.ConnectTimeout, fromFactory.ConnectTimeout);
                    Compare(type, "ReceiveTimeout", fromSetup.ReceiveTimeout, fromFactory.ReceiveTimeout);
                    Compare(type, "SendTimeout", fromSetup.SendTimeout, fromFactory.SendTimeout);
                    Compare(type, "Encoding", fromSetup.Encoding.WebName, fromFactory.Encoding.WebName);

                    if (fromSetup is ITikTaggedConnection taggedSetup && fromFactory is ITikTaggedConnection taggedFactory)
                        Compare(type, "SendTagWithSyncCommand",
                            taggedSetup.SendTagWithSyncCommand, taggedFactory.SendTagWithSyncCommand);

                    if (fromSetup is ITikTlsConnection tlsSetup && fromFactory is ITikTlsConnection tlsFactory)
                        Compare(type, "AllowInvalidCertificate",
                            tlsSetup.AllowInvalidCertificate, tlsFactory.AllowInvalidCertificate);
                }
            }

            // Reported together rather than one at a time: the interesting answer is how far the two sets of
            // defaults have drifted apart, and a first-failure assert hides everything behind it.
            Assert.AreEqual(0, divergences.Count,
                "CreateConnection(type) and a bare setup disagree on:" + Environment.NewLine
                + string.Join(Environment.NewLine, divergences));
        }

        /// <summary>
        /// The address guard belongs to the setup, so the factory route must not be a way round it — a
        /// MAC-only setup is legitimate for the MAC-layer transports and unusable for the rest.
        /// </summary>
        [TestMethod]
        public void AMacOnlySetupIsRejectedByTheIpTransportsThroughTheFactoryToo()
        {
            var macOnly = new TikConnectionSetup(TikRouterAddress.FromMac("AA:BB:CC:DD:EE:FF"), "user", "pwd");

            using (var conn = ConnectionFactory.CreateConnection(TikConnectionType.MacTelnet, macOnly))
                Assert.AreEqual("AA:BB:CC:DD:EE:FF", ((ITikMacLayerConnection)conn).RouterMac);

            Assert.ThrowsException<InvalidOperationException>(
                () => ConnectionFactory.CreateConnection(TikConnectionType.Api, macOnly));
        }

        [TestMethod]
        public void ANullSetupIsRejected()
        {
            Assert.ThrowsException<ArgumentNullException>(
                () => ConnectionFactory.CreateConnection(TikConnectionType.Api, (TikConnectionSetup)null));
            Assert.ThrowsException<ArgumentNullException>(
                () => ConnectionFactory.OpenConnection(TikConnectionType.Api, (TikConnectionSetup)null));
        }

        /// <summary>
        /// The short overloads document their first argument as a host, so it is read as one — the implicit
        /// string conversion on <see cref="TikRouterAddress"/> guesses between a host and a MAC, and a
        /// router whose name happens to look like hexadecimal must not become a MAC-layer address here.
        /// </summary>
        [TestMethod]
        public void TheShortOverloadsRejectAnEmptyHost()
        {
            Assert.ThrowsException<ArgumentException>(
                () => ConnectionFactory.OpenConnection(TikConnectionType.Api, "", "user", "pwd"));
        }
    }
}
