using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;
using tik4net.Ssh;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// The matrix D1 exists for: <b>every option of <see cref="TikConnectionSetup"/> reaches every transport
    /// it can mean anything on</b>, checked by creating each transport and reading the values back off it —
    /// not by the property names lining up.
    /// </summary>
    /// <remarks>
    /// Nothing here opens a connection, so no router is involved and the whole file runs in CI. What it
    /// cannot see is whether a transport then <i>uses</i> the value it was handed; that is the integration
    /// suite's job. What it does catch is the failure that actually happened repeatedly through 4.x — an
    /// option added to the setup and wired to some transports, silently doing nothing on the rest.
    /// </remarks>
    [TestClass]
    public class TikConnectionSetupOptionMatrixTests
    {
        // Every transport tik4net ships, including the satellite SSH one.
        private static readonly TikConnectionType[] AllTransports =
        {
            TikConnectionType.Api,
            TikConnectionType.ApiSsl,
            TikConnectionType.Rest,
            TikConnectionType.RestSsl,
            TikConnectionType.Telnet,
            TikConnectionType.Ssh,
            TikConnectionType.MacTelnet,
            TikConnectionType.WinboxCli,
            TikConnectionType.WinboxCliMac,
            TikConnectionType.WinboxNative,
            TikConnectionType.WinboxNativeMac,
        };

        // The declared applicability of the three non-universal options. A transport is either in the set
        // and receives the option, or out of it and provably cannot use it — the tests below assert both
        // directions, so adding a transport without deciding shows up as a failure rather than as silence.
        private static readonly TikConnectionType[] TlsTransports =
        {
            // One class serves the plain and the TLS form of each of these, so both spellings are TLS-capable.
            TikConnectionType.Api, TikConnectionType.ApiSsl,
            TikConnectionType.Rest, TikConnectionType.RestSsl,
        };

        private static readonly TikConnectionType[] MacLayerTransports =
        {
            TikConnectionType.MacTelnet, TikConnectionType.WinboxCliMac, TikConnectionType.WinboxNativeMac,
        };

        private static readonly TikConnectionType[] TaggedTransports =
        {
            // Per-command .tag is the binary API's; the other transports that allow concurrent commands
            // correlate replies by an HTTP request or an M2 request id and have nothing to switch on.
            TikConnectionType.Api, TikConnectionType.ApiSsl,
        };

        private static readonly TikConnectionType[] CancellationModeTransports =
        {
            // The CLI family: a terminal byte stream has no framing to resynchronize on, so what a late
            // cancel does is a choice. Everything else cancels for real (CancelInFlight).
            TikConnectionType.Telnet, TikConnectionType.Ssh, TikConnectionType.MacTelnet,
            TikConnectionType.WinboxCli, TikConnectionType.WinboxCliMac,
        };

        [ClassInitialize]
        public static void RegisterSatelliteTransports(TestContext context)
            => Tik4NetSsh.Register();   // idempotent; makes TikConnectionType.Ssh creatable like a built-in

        private static readonly Func<object, System.Security.Cryptography.X509Certificates.X509Certificate,
            System.Security.Cryptography.X509Certificates.X509Chain,
            System.Net.Security.SslPolicyErrors, bool> CertCallback = (a, b, c, d) => true;

        // Every value differs from every default, so a value that did not travel cannot look like one that did.
        private static TikConnectionSetup NonDefaultSetup() => new TikConnectionSetup("192.0.2.1", "user", "pwd")
        {
            ConnectTimeout = TimeSpan.FromSeconds(7),
            ReceiveTimeout = TimeSpan.FromSeconds(11),
            SendTimeout = TimeSpan.FromSeconds(13),
            Encoding = Encoding.ASCII,
            SendTagWithSyncCommand = true,
            DebugEnabled = true,
            RouterMac = "AA:BB:CC:DD:EE:FF",
            CancellationMode = TikCancellationMode.AbandonAndClose,
            AllowInvalidCertificate = false,
            CertificateValidationCallback = new System.Net.Security.RemoteCertificateValidationCallback(CertCallback),
        };

        // ── The universal options ─────────────────────────────────────────────

        [TestMethod]
        public void EveryTransportReceivesEveryUniversalOption()
        {
            var setup = NonDefaultSetup();
            foreach (var type in AllTransports)
            {
                using (var conn = setup.CreateUnopened(type))
                {
                    Assert.AreEqual(7000, conn.ConnectTimeout, type + ": ConnectTimeout");
                    Assert.AreEqual(11000, conn.ReceiveTimeout, type + ": ReceiveTimeout");
                    Assert.AreEqual(13000, conn.SendTimeout, type + ": SendTimeout");
                    Assert.AreSame(Encoding.ASCII, conn.Encoding, type + ": Encoding");
                    Assert.IsTrue(conn.DebugEnabled, type + ": DebugEnabled");
                }
            }
        }

        [TestMethod]
        public void DebugEnabledLeftUnsetKeepsTheTransportDefault()
        {
            // null is "don't touch", which is not the same as false: the transports default it to
            // Debugger.IsAttached, and an unset option must not turn that off.
            var setup = new TikConnectionSetup("192.0.2.1", "user", "pwd") { DebugEnabled = null };
            foreach (var type in AllTransports)
            {
                using (var conn = setup.CreateUnopened(type))
                    Assert.AreEqual(System.Diagnostics.Debugger.IsAttached, conn.DebugEnabled, type.ToString());
            }
        }

        [TestMethod]
        public void AnUnboundedTimeoutSaturatesInsteadOfOverflowing()
        {
            // TimeSpan.MaxValue in milliseconds does not fit an int; wrapping would arrive as a negative
            // value, which several transports read as "do not wait at all" — the opposite of what was asked.
            var setup = new TikConnectionSetup("192.0.2.1", "user", "pwd")
            {
                ConnectTimeout = TimeSpan.MaxValue,
                ReceiveTimeout = TimeSpan.MaxValue,
                SendTimeout = TimeSpan.MaxValue,
            };
            using (var conn = setup.CreateUnopened(TikConnectionType.Api))
            {
                Assert.AreEqual(int.MaxValue, conn.ConnectTimeout);
                Assert.AreEqual(int.MaxValue, conn.ReceiveTimeout);
                Assert.AreEqual(int.MaxValue, conn.SendTimeout);
            }
        }

        // ── The three interface-scoped options ────────────────────────────────

        [TestMethod]
        public void TheCertificateOptionsReachExactlyTheTlsTransports()
        {
            var setup = NonDefaultSetup();
            foreach (var type in AllTransports)
            {
                using (var conn = setup.CreateUnopened(type))
                {
                    bool expected = TlsTransports.Contains(type);
                    Assert.AreEqual(expected, conn is ITikTlsConnection, type + ": ITikTlsConnection");
                    if (!expected)
                        continue;

                    var tls = (ITikTlsConnection)conn;
                    Assert.IsFalse(tls.AllowInvalidCertificate, type + ": AllowInvalidCertificate");
                    Assert.AreSame(setup.CertificateValidationCallback, tls.CertificateValidationCallback,
                        type + ": CertificateValidationCallback");
                }
            }
        }

        [TestMethod]
        public void TheRouterMacReachesExactlyTheMacLayerTransports()
        {
            var setup = NonDefaultSetup();
            foreach (var type in AllTransports)
            {
                using (var conn = setup.CreateUnopened(type))
                {
                    bool expected = MacLayerTransports.Contains(type);
                    Assert.AreEqual(expected, conn is ITikMacLayerConnection, type + ": ITikMacLayerConnection");
                    if (expected)
                        Assert.AreEqual("AA:BB:CC:DD:EE:FF", ((ITikMacLayerConnection)conn).RouterMac, type.ToString());
                }
            }
        }

        [TestMethod]
        public void TheCancellationModeReachesExactlyTheCliTransports()
        {
            var setup = NonDefaultSetup();
            foreach (var type in AllTransports)
            {
                using (var conn = setup.CreateUnopened(type))
                {
                    bool expected = CancellationModeTransports.Contains(type);
                    Assert.AreEqual(expected, conn is ITikCancellationModeConnection,
                        type + ": ITikCancellationModeConnection");
                    if (expected)
                        Assert.AreEqual(TikCancellationMode.AbandonAndClose,
                            ((ITikCancellationModeConnection)conn).CancellationMode, type.ToString());
                }
            }
        }

        [TestMethod]
        public void TheTagFlagReachesExactlyTheTaggingTransports()
        {
            var setup = NonDefaultSetup();
            foreach (var type in AllTransports)
            {
                using (var conn = setup.CreateUnopened(type))
                {
                    bool expected = TaggedTransports.Contains(type);
                    Assert.AreEqual(expected, conn is ITikTaggedConnection, type + ": ITikTaggedConnection");
                    Assert.AreEqual(expected, conn.Supports(TikConnectionCapability.Tagging),
                        type + ": the Tagging capability and the interface must agree");
                    if (expected)
                        Assert.IsTrue(((ITikTaggedConnection)conn).SendTagWithSyncCommand, type.ToString());
                }
            }
        }

        [TestMethod]
        public void SafeModeIsOfferedByExactlyTheTransportsThatDeclareIt()
        {
            // The interface and the capability flag answer the same question, and a caller may use either.
            foreach (var type in AllTransports)
            {
                using (var conn = ConnectionFactory.CreateConnection(type))
                    Assert.AreEqual(conn.Supports(TikConnectionCapability.SafeMode),
                        conn is ITikSafeModeConnection, type.ToString());
            }
        }

        [TestMethod]
        public void TheFakeConnectionsFlagsAgreeWithTheInterfacesItImplements()
        {
            // The same invariant as the two tests above, applied to tik4net.testing's fake — a test double
            // whose flags and interfaces disagreed would teach callers a rule the real transports keep.
            using (var fake = new tik4net.Testing.TikFakeConnection())
            {
                Assert.AreEqual(fake.Supports(TikConnectionCapability.SafeMode), fake is ITikSafeModeConnection,
                    "SafeMode");
                Assert.AreEqual(fake.Supports(TikConnectionCapability.Tagging), fake is ITikTaggedConnection,
                    "Tagging");
            }
        }

        [TestMethod]
        public void SafeModeGetIsAnswerableEvenWhereSafeModeIsNot()
        {
            // A finally block asking "am I holding safe mode?" must not throw on REST on the way out.
            using (var rest = ConnectionFactory.CreateConnection(TikConnectionType.Rest))
            {
                Assert.IsFalse(rest.SafeModeGet());
                Assert.ThrowsException<TikConnectionCapabilityNotSupportedException>(() => rest.SafeModeTake());
            }
        }

        [TestMethod]
        public void ATransportThatTakesNoCancellationModeCancelsForReal()
        {
            // The justification for leaving the option off, asserted rather than asserted-in-a-comment: a
            // transport only gets to ignore CancellationMode because a cancel there is genuine.
            var setup = NonDefaultSetup();
            foreach (var type in AllTransports)
            {
                using (var conn = setup.CreateUnopened(type))
                {
                    if (conn is ITikCancellationModeConnection)
                        continue;
                    Assert.IsTrue(conn.Supports(TikConnectionCapability.CancelInFlight),
                        type + " implements neither ITikCancellationModeConnection nor CancelInFlight — a "
                        + "cancellation on it is then neither real nor configurable");
                }
            }
        }

        // ── The two entry points ──────────────────────────────────────────────

        [TestMethod]
        public void EveryTransportIsReachableThroughBothEntryPoints()
        {
            var setup = NonDefaultSetup();
            foreach (var type in AllTransports)
            {
                using (var viaSetup = setup.CreateUnopened(type))
                using (var viaFactory = ConnectionFactory.CreateConnection(type))
                    Assert.AreEqual(viaSetup.GetType(), viaFactory.GetType(), type.ToString());
            }
        }

        [TestMethod]
        public void ConnectionFactoryHandsOutTransportDefaults()
        {
            // The documented difference between the two entry points: the shim has no options object, so
            // what it returns carries the transport's own defaults and nothing of any setup.
            foreach (var type in AllTransports)
            {
                using (var conn = ConnectionFactory.CreateConnection(type))
                {
                    Assert.AreEqual(15000, conn.ConnectTimeout, type + ": ConnectTimeout");
                    Assert.AreEqual(30000, conn.ReceiveTimeout, type + ": ReceiveTimeout");
                    if (conn is ITikTaggedConnection tagged)
                        Assert.IsFalse(tagged.SendTagWithSyncCommand, type + ": SendTagWithSyncCommand");
                }
            }
        }

        [TestMethod]
        public void TheConfigureHookRunsAfterTheOptionsSoItCanOverrideThem()
        {
            var setup = NonDefaultSetup();
            using (var conn = setup.CreateUnopened(TikConnectionType.MacTelnet,
                c => ((ITikMacLayerConnection)c).RouterMac = "11:22:33:44:55:66"))
            {
                Assert.AreEqual("11:22:33:44:55:66", ((ITikMacLayerConnection)conn).RouterMac);
            }
        }

        [TestMethod]
        public void AnUnregisteredSatelliteTransportSaysHowToRegisterIt()
        {
            var setup = NonDefaultSetup();
            var ex = Assert.ThrowsException<NotImplementedException>(
                () => setup.CreateUnopened((TikConnectionType)9999));
            StringAssert.Contains(ex.Message, "RegisterConnectionFactory");
        }

        [TestMethod]
        public void TheMatrixCoversEveryConnectionTypeTheEnumDeclares()
        {
            // Otherwise a transport added to the enum is simply absent from every test above, and the
            // matrix stays green while covering less than it claims.
            var obsolete = new HashSet<string> { "Api_v2", "ApiSsl_v2" };   // [Obsolete(error:true)] since 4.0
            var declared = Enum.GetNames(typeof(TikConnectionType)).Where(n => !obsolete.Contains(n));
            var covered = AllTransports.Select(t => t.ToString()).ToList();

            CollectionAssert.AreEquivalent(declared.ToList(), covered);
        }
    }
}
