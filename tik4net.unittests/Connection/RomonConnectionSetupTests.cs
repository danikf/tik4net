#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Ssh;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// The public RoMON surface: <see cref="TikRouterAddress.FromRomonId"/>, <see cref="TikRomonAgentSetup"/>,
    /// <see cref="TikConnectionSetup.RomonAgentSetup"/> and <see cref="TikRomonConnectionExtensions.GetRomonConnectionInfo"/>.
    /// No router: the relay itself is covered by <c>RomonSshRelayTranscriptTests</c> and the opt-in live probe.
    /// </summary>
    /// <remarks>
    /// What matters most here is the one failure that would be silent: a RoMON setup handed to a transport
    /// that does not relay, which would open the AGENT and run every command there. Every such path must be
    /// refused before anything connects.
    /// </remarks>
    [TestClass]
    public class RomonConnectionSetupTests
    {
        private const string TargetId = "AA:BB:CC:DD:EE:FF";
        private const string AgentRomonId = "AA:BB:CC:00:00:01";

        private static readonly TikConnectionType[] RelayingTransports =
            { TikConnectionType.Telnet, TikConnectionType.Ssh, TikConnectionType.MacTelnet };

        // The relaying transports that reach the agent over IP.
        private static readonly TikConnectionType[] IpRelayingTransports = { TikConnectionType.Telnet, TikConnectionType.Ssh };

        private const string AgentMac = "AA:BB:CC:00:00:02";

        [ClassInitialize]
        public static void RegisterSatelliteTransports(TestContext context) => Tik4NetSsh.Register();

        private static TikConnectionSetup RomonSetup(TikRomonAgentSetup? agent = null)
            => new TikConnectionSetup(TikRouterAddress.FromRomonId(TargetId), "target-user", "target-pw")
            {
                RomonAgentSetup = agent ?? new TikRomonAgentSetup("192.0.2.1", "agent-user", "agent-pw") { Port = 2223 },
            };

        // ── the address ───────────────────────────────────────────────────────

        [TestMethod]
        public void FromRomonId_IsItsOwnCoordinate_NormalisedAndNeverAMac()
        {
            var a = TikRouterAddress.FromRomonId("aa-bb-cc-dd-ee-ff");

            Assert.AreEqual(TargetId, a.RomonId);
            Assert.IsTrue(a.HasRomonId);
            Assert.IsFalse(a.HasHost);
            Assert.IsFalse(a.HasMac);
            Assert.IsFalse(a.IsEmpty);
            Assert.AreEqual("RoMON " + TargetId, a.ToString());
            Assert.AreNotEqual(TikRouterAddress.FromMac(TargetId), a, "a RoMON id and a MAC of the same shape are different addresses");
        }

        [TestMethod]
        public void ABareStringIsNeverReadAsARomonId()
        {
            TikRouterAddress parsed = TargetId;
            Assert.IsTrue(parsed.HasMac);
            Assert.IsFalse(parsed.HasRomonId);
        }

        [TestMethod]
        public void FromRomonId_RefusesWhatIsNotSixHexOctets()
            => Assert.ThrowsException<ArgumentException>(() => TikRouterAddress.FromRomonId("AA:BB:CC:DD:EE"));

        // ── the agent setup ───────────────────────────────────────────────────

        [TestMethod]
        public void AnAgentIsReachedDirectly_NeverByARomonId()
            => Assert.ThrowsException<ArgumentException>(() =>
                new TikRomonAgentSetup(TikRouterAddress.FromRomonId(TargetId), "u", "p"));

        [TestMethod]
        public void TheAgentSetupNeverPrintsItsPassword()
            => Assert.IsFalse(new TikRomonAgentSetup("192.0.2.1", "agent-user", "agent-pw").ToString().Contains("agent-pw"));

        // ── what is refused, before anything connects ─────────────────────────

        [TestMethod]
        public void OnlyTelnetSshAndMacTelnetRelay_AndSupportsRomonSaysExactlyThat()
        {
            foreach (TikConnectionType type in Enum.GetValues(typeof(TikConnectionType)))
            {
                bool relays = RelayingTransports.Contains(type);
                Assert.AreEqual(relays, TikConnectionSetup.SupportsRomon(type), type + ": SupportsRomon");
                if (relays)
                    using (RomonSetup().CreateUnopened(type)) { }
                else
                    Assert.ThrowsException<NotSupportedException>(() => RomonSetup().CreateUnopened(type),
                        type + " must refuse a RoMON setup: ignoring it would run every command on the agent");
            }
        }

        [TestMethod]
        public void ARomonIdWithoutAnAgent_IsRefused()
        {
            var setup = new TikConnectionSetup(TikRouterAddress.FromRomonId(TargetId), "u", "p");
            foreach (TikConnectionType type in Enum.GetValues(typeof(TikConnectionType)))
                Assert.ThrowsException<InvalidOperationException>(() => setup.CreateUnopened(type), type.ToString());
        }

        [TestMethod]
        public void AnAgentWithATargetThatIsNotARomonId_IsRefused()
        {
            var setup = new TikConnectionSetup("192.0.2.9", "u", "p")
            {
                RomonAgentSetup = new TikRomonAgentSetup("192.0.2.1", "a", "b"),
            };
            Assert.ThrowsException<InvalidOperationException>(() => setup.CreateUnopened(TikConnectionType.Telnet));
        }

        [TestMethod]
        public void PortOnTheTargetSetup_IsRefused_BecauseTheAgentsPortIsTheOneDialled()
        {
            var setup = RomonSetup();
            setup.Port = 23;
            var ex = Assert.ThrowsException<InvalidOperationException>(() => setup.CreateUnopened(TikConnectionType.Telnet));
            StringAssert.Contains(ex.Message, "RomonAgentSetup.Port");
        }

        [TestMethod]
        public void AnAgentAddressedByMacOnly_IsRefusedByTheIpTransports()
        {
            var setup = RomonSetup(new TikRomonAgentSetup(TikRouterAddress.FromMac(AgentMac), "a", "b"));
            foreach (var type in IpRelayingTransports)
                Assert.ThrowsException<InvalidOperationException>(() => setup.CreateUnopened(type), type.ToString());
        }

        [TestMethod]
        public void MacTelnet_ReachesAnAgentByMacAlone_AndTheMacIsTheAgents()
        {
            var setup = RomonSetup(new TikRomonAgentSetup(TikRouterAddress.FromMac(AgentMac), "a", "b"));
            using (var conn = setup.CreateUnopened(TikConnectionType.MacTelnet))
                Assert.AreEqual(AgentMac, ((ITikMacLayerConnection)conn).RouterMac);
        }

        [TestMethod]
        public void MacTelnet_ToAnAgentGivenByHost_LeavesTheMacToMndp()
        {
            using (var conn = RomonSetup().CreateUnopened(TikConnectionType.MacTelnet))
                Assert.IsNull(((ITikMacLayerConnection)conn).RouterMac,
                    "no MAC was given for the agent; one taken from anywhere else would reach the wrong router");
        }

        [TestMethod]
        public void RouterMacOnTheTargetSetup_IsRefused_BecauseTheMacReachedIsTheAgents()
        {
            var setup = RomonSetup();
            setup.RouterMac = AgentMac;
            var ex = Assert.ThrowsException<InvalidOperationException>(() => setup.CreateUnopened(TikConnectionType.MacTelnet));
            StringAssert.Contains(ex.Message, "TikRomonAgentSetup");
        }

        [TestMethod]
        public void ACliTransportWithoutTheRelay_RefusesATargetAtOpen_BeforeItLogsIn()
        {
            var conn = new PlainCli { RomonTarget = new RomonSshTarget(TargetId, "u", "p") };

            Assert.ThrowsException<NotSupportedException>(() => conn.Open("192.0.2.1", "u", "p"));
            Assert.IsFalse(conn.LoginRan, "nothing may reach the agent");
        }

        // ── what travels where ────────────────────────────────────────────────

        [TestMethod]
        public void TheAgentsCoordinatesAreDialled_AndTheTargetsCredentialsGoToTheRelay()
        {
            var setup = RomonSetup();
            var conn = new RecordingRomonConnection();

            setup.ApplyTo(conn);
            setup.Open(conn);

            Assert.AreEqual("192.0.2.1", conn.OpenHost);
            Assert.AreEqual(2223, conn.OpenPort);
            Assert.AreEqual("agent-user", conn.OpenUser);
            Assert.AreEqual("agent-pw", conn.OpenPassword);

            var target = ((ITikRomonConnection)conn).RomonTarget!;
            Assert.AreEqual(TargetId, target.RomonId);
            Assert.AreEqual("target-user", target.User);
            Assert.AreEqual("target-pw", target.Password);
        }

        [TestMethod]
        public void TheSessionOptionsComeFromTheTargetSetup()
        {
            var setup = RomonSetup();
            setup.ReceiveTimeout = TimeSpan.FromSeconds(11);
            using (var conn = setup.CreateUnopened(TikConnectionType.Telnet))
                Assert.AreEqual(11000, conn.ReceiveTimeout);
        }

        [TestMethod]
        public void GetRomonConnectionInfo_DescribesTheRoute_OnceOpen_AndNeverCarriesAPassword()
        {
            var setup = RomonSetup();
            var conn = new RecordingRomonConnection();
            setup.ApplyTo(conn);

            Assert.IsNull(conn.GetRomonConnectionInfo(), "not open yet: there is no route to describe");

            setup.Open(conn);
            var info = conn.GetRomonConnectionInfo();

            Assert.IsNotNull(info);
            Assert.AreEqual(TikRomonRelay.Ssh, info.Relay);
            Assert.AreEqual(TikRouterAddress.FromHost("192.0.2.1"), info.Agent.Address);
            Assert.AreEqual(TikConnectionType.Telnet, info.Agent.ConnectionType);
            Assert.AreEqual("agent-user", info.Agent.User);
            Assert.AreEqual(AgentRomonId, info.Agent.RomonId);
            Assert.AreEqual(TargetId, info.Target.RomonId);
            Assert.AreEqual("target-user", info.Target.User);
            Assert.IsFalse(info.ToString().Contains("pw"), info.ToString());
        }

        [TestMethod]
        public void ADirectConnection_HasNoRomonInfo_AndIsDialledAsBefore()
        {
            var setup = new TikConnectionSetup("192.0.2.9", "u", "p") { Port = 23 };
            var conn = new RecordingRomonConnection();

            setup.ApplyTo(conn);
            setup.Open(conn);

            Assert.IsNull(((ITikRomonConnection)conn).RomonTarget);
            Assert.IsNull(conn.GetRomonConnectionInfo());
            Assert.AreEqual("192.0.2.9", conn.OpenHost);
            Assert.AreEqual(23, conn.OpenPort);
            Assert.AreEqual("u", conn.OpenUser);
        }

        [TestMethod]
        public void GetRomonConnectionInfo_IsNullOnATransportThatCannotRelay()
        {
            using (var conn = new TikConnectionSetup("192.0.2.9", "u", "p").CreateUnopened(TikConnectionType.Api))
                Assert.IsNull(conn.GetRomonConnectionInfo());
        }

        // ── fakes ─────────────────────────────────────────────────────────────

        /// <summary>A relaying CLI connection that records what it is opened with and simulates a relay that succeeds.</summary>
        private sealed class RecordingRomonConnection : CliConnectionBase, ITikRomonConnection
        {
            public string? OpenHost, OpenUser, OpenPassword;
            public int? OpenPort;

            protected override string TransportName => "RecordingRomon";

            RomonSshTarget? ITikRomonConnection.RomonTarget { get => RomonTarget; set => RomonTarget = value; }
            TikConnectionType ITikRomonConnection.RomonAgentConnectionType => TikConnectionType.Telnet;
            TikRomonConnectionInfo? ITikRomonConnection.RomonConnectionInfo => RomonConnectionInfo;

            private void OpenRecorded(string host, int? port, string user, string password)
            {
                OpenHost = host; OpenPort = port; OpenUser = user; OpenPassword = password;
                OpenWith(ct =>
                    {
                        if (RomonTarget != null) RomonEntered(TikConnectionType.Telnet, host, user, AgentRomonId);
                        return Task.FromResult(0);
                    },
                    (cmd, ct) => Task.FromResult(string.Empty),
                    (raw, ct) => Task.FromResult(string.Empty),
                    () => { });
            }

            public override void Open(string host, string user, string password) => OpenRecorded(host, null, user, password);
            public override void Open(string host, int port, string user, string password) => OpenRecorded(host, port, user, password);
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default)
            { OpenRecorded(host, null, user, password); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default)
            { OpenRecorded(host, port, user, password); return Task.FromResult(0); }
        }

        /// <summary>A CLI connection that does not relay (no <see cref="ITikRomonConnection"/>).</summary>
        private sealed class PlainCli : CliConnectionBase
        {
            public bool LoginRan;

            protected override string TransportName => "Plain";

            private void OpenPlain()
                => OpenWith(ct => { LoginRan = true; return Task.FromResult(0); },
                    (cmd, ct) => Task.FromResult(string.Empty),
                    (raw, ct) => Task.FromResult(string.Empty),
                    () => { });

            public override void Open(string host, string user, string password) => OpenPlain();
            public override void Open(string host, int port, string user, string password) => OpenPlain();
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default)
            { OpenPlain(); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default)
            { OpenPlain(); return Task.FromResult(0); }
        }
    }
}
