// EntityRouterOsRangeTests.cs — the MinRouterOs / MaxRouterOs bounds against what the lab measured.
//
// The table is VersionPresenceProbe's report (integration tests): for each mapped menu or field that is not on all three lab
// routers, whether 7.24.5, 7.21.5 and 6.49.13 have it — y yes, n no, x the menu itself is missing. Re-run the probe on a new
// RouterOS release and replace the table; a bound that no longer matches fails here.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;

namespace tik4net.unittests.Objects
{
    [TestClass]
    public class EntityRouterOsRangeTests
    {
        private static readonly Version[] LabRouters = { new Version(7, 24, 5), new Version(7, 21, 5), new Version(6, 49, 13) };

        /// <summary>(class, property or "" for the menu, presence on 7.24.5 / 7.21.5 / 6.49.13).</summary>
        private static readonly (string Class, string Property, string Presence)[] Measured =
        {
            ("Certificate", "TrustStore", "yyn"),
            ("Certificate", "AcmeStatus", "ynn"),
            ("Certificate", "DomainNames", "ynn"),
            ("Certificate", "DirectoryUrl", "ynn"),
            ("File", "CreationTime", "nny"),
            ("File", "LastModified", "yyn"),
            ("InterfaceBonding", "LacpMode", "yyn"),
            ("InterfaceBonding", "LacpSystemId", "yyn"),
            ("InterfaceBonding", "LacpSystemPriority", "yyn"),
            ("InterfaceBonding", "LacpUserKey", "yyn"),
            ("InterfaceBridge", "ForwardReservedAddresses", "yyn"),
            ("InterfaceBridge", "MaxLearnedEntries", "yyn"),
            ("InterfaceBridge", "PortCostMode", "yyn"),
            ("InterfaceBridge", "Mvrp", "yyn"),
            ("InterfaceBridge", "QuerierUsesBridgeAddress", "ynn"),
            ("InterfaceBridge", "DhcpAgentCircuitId", "ynn"),
            ("InterfaceBridge", "DhcpAgentRemoteId", "ynn"),
            ("InterfaceBridge", "Dhcpv6Snooping", "ynn"),
            ("InterfaceBridge", "Dhcpv6AgentCircuitId", "ynn"),
            ("InterfaceBridge", "Dhcpv6AgentRemoteId", "ynn"),
            ("InterfaceBridge", "RaGuard", "ynn"),
            ("InterfaceBridge", "MlagPeerPort", "ynn"),
            ("InterfaceBridge", "MlagPriority", "ynn"),
            ("InterfaceBridge", "MlagHeartbeat", "ynn"),
            ("InterfaceBridge", "Dynamic", "yyn"),
            ("InterfaceBridge", "Managed", "ynn"),
            ("BridgeVlan", "MvrpForbidden", "yyn"),
            ("InterfaceEthernet", "FullDuplex", "nny"),
            ("L2tpClient", "RandomSourcePort", "yyn"),
            ("L2tpClient", "L2tpProtoVersion", "yyn"),
            ("L2tpClient", "L2tpv3CircuitId", "yyn"),
            ("L2tpClient", "L2tpv3CookieLength", "yyn"),
            ("L2tpClient", "L2tpv3DigestHash", "yyn"),
            ("L2tpServer", "AcceptProtoVersion", "yyn"),
            ("L2tpServer", "AcceptPseudowireType", "yyn"),
            ("L2tpServer", "L2tpv3CircuitId", "yyn"),
            ("L2tpServer", "L2tpv3CookieLength", "yyn"),
            ("L2tpServer", "L2tpv3DigestHash", "yyn"),
            ("L2tpServer", "L2tpv3EtherInterfaceList", "yyn"),
            ("InterfaceLte", "MacAddress", "nny"),
            ("InterfaceLte", "NrBand", "yyn"),
            ("InterfaceLte", "SmsProtocol", "yyn"),
            ("InterfaceLte", "SmsRead", "yyn"),
            ("OvpnClient", "DisconnectNotify", "yyn"),
            ("OvpnClient", "Protocol", "yyn"),
            ("OvpnClient", "RouteNopull", "yyn"),
            ("OvpnClient", "TlsVersion", "yyn"),
            ("OvpnServer", "Inactive", "yyn"),
            ("OvpnServer", "Name", "yyn"),
            ("OvpnServer", "Disabled", "yyn"),
            ("OvpnServer", "EnableTunIpv6", "yyn"),
            ("OvpnServer", "Ipv6PrefixLen", "yyn"),
            ("OvpnServer", "Protocol", "yyn"),
            ("OvpnServer", "PushRoutes", "yyn"),
            ("OvpnServer", "PushRoutesIpv6", "yyn"),
            ("OvpnServer", "RedirectGateway", "yyn"),
            ("OvpnServer", "RenegSec", "yyn"),
            ("OvpnServer", "TlsVersion", "yyn"),
            ("OvpnServer", "TunServerIpv6", "yyn"),
            ("OvpnServer", "UserAuthMethod", "yyn"),
            ("OvpnServer", "Vrf", "yyn"),
            ("OvpnServer", "Comment", "yyn"),
            ("SstpClient", "Port", "yyn"),
            ("SstpClient", "Ciphers", "yyn"),
            ("SstpClient", "ProxyPort", "yyn"),
            ("SstpClient", "AddSni", "yyn"),
            ("SstpClient", "HwCrypto", "yyn"),
            ("SstpServer", "Ciphers", "yyn"),
            ("InterfaceVrrp", "OnFail", "yyn"),
            ("InterfaceVrrp", "GroupAuthority", "yyn"),
            ("InterfaceVrrp", "SyncConnectionTracking", "yyn"),
            ("InterfaceVrrp", "ConnectionTrackingMode", "yyn"),
            ("InterfaceVrrp", "ConnectionTrackingPort", "yyn"),
            ("InterfaceVrrp", "RemoteAddress", "yyn"),
            ("InterfaceVxlan", "", "yyn"),
            ("InterfaceWifi", "", "yyn"),
            ("WifiAccessList", "", "yyn"),
            ("WifiChannel", "", "yyn"),
            ("WifiChannel", "PreamblePuncturing", "ynx"),
            ("WifiConfiguration", "", "yyn"),
            ("WifiDatapath", "", "yyn"),
            ("WifiProvisioning", "", "yyn"),
            ("WifiRegistrationTable", "", "yyn"),
            ("WifiSecurity", "", "yyn"),
            ("InterfaceWireguard", "", "yyn"),
            ("WireguardPeer", "", "yyn"),
            ("WirelessRegistrationTable", "TxSignalStrengthCh0", "yyn"),
            ("WirelessRegistrationTable", "TxSignalStrengthCh1", "yyn"),
            ("WirelessRegistrationTable", "TxSignalStrengthCh2", "yyn"),
            ("IpAccounting", "", "nny"),
            ("AccountingSnapshot", "", "nny"),
            ("AccountingUncounted", "", "nny"),
            ("AccountingWebAccess", "", "nny"),
            ("IpArp", "Dhcp", "yyn"),
            ("IpDhcpServer", "SrcAddress", "nny"),
            ("FirewallFilter", "Realm", "yyn"),
            ("FirewallFilter", "Tos", "yyn"),
            ("FirewallMangle", "Tos", "yyn"),
            ("FirewallMangle", "Realm", "yyn"),
            ("FirewallRaw", "Tos", "yyn"),
            ("HotspotServerProfile", "InstallHotspotQueue", "yyn"),
            ("IpsecKey", "", "yyn"),
            ("IpsecPeer", "PpkSecret", "yyn"),
            ("IpsecProfile", "Ppk", "yyn"),
            ("IpNeighbor", "DiscoveredBy", "yyn"),
            ("IpProxyAccess", "ActionData", "yyn"),
            ("IpRoute", "GatewayStatus", "nny"),
            ("IpRoute", "BgpAsPath", "nny"),
            ("IpRoute", "BgpOrigin", "nny"),
            ("IpRoute", "BgpCommunities", "nny"),
            ("IpRoute", "ReceivedFrom", "nny"),
            ("IpService", "MaxSessions", "yyn"),
            ("IpService", "Vrf", "yyn"),
            ("IpService", "Proto", "yyn"),
            ("IpService", "Dynamic", "yyn"),
            ("IpService", "Local", "yyn"),
            ("IpService", "Remote", "yyn"),
            ("IpService", "Connection", "yyn"),
            ("IpSettings", "TcpTimestamps", "yyn"),
            ("IpSettings", "IcmpErrorsUseInboundInterfaceAddress", "yyn"),
            ("IpSettings", "Ipv4MultipathHashPolicy", "yyn"),
            ("IpSocks", "Vrf", "yyn"),
            ("IpSsh", "Ciphers", "yyn"),
            ("IpSsh", "HostKeyType", "yyn"),
            ("IpSsh", "PasswordAuthentication", "yyn"),
            ("IpSsh", "PublickeyAuthenticationOptions", "yyn"),
            ("Radius", "RadsecTimeout", "yyn"),
            ("Radius", "RequireMessageAuth", "yyn"),
            ("Radius", "Status", "yyn"),
            ("RadiusIncoming", "Vrf", "yyn"),
            ("BgpAdvertisements", "Prefix", "nny"),
            ("BgpConnection", "", "yyn"),
            ("BgpInstance", "RedistributeConnected", "nny"),
            ("BgpInstance", "RedistributeStatic", "nny"),
            ("BgpInstance", "RedistributeRip", "nny"),
            ("BgpInstance", "RedistributeOspf", "nny"),
            ("BgpInstance", "RedistributeOtherBgp", "nny"),
            ("BgpInstance", "ClientToClientReflection", "nny"),
            ("BgpInstance", "Default", "nny"),
            ("BgpNetwork", "", "nny"),
            ("BgpPeer", "", "nny"),
            ("RoutingFilterRule", "", "yyn"),
            ("OspfArea", "NoSummaries", "yyn"),
            ("OspfArea", "NssaTranslator", "yyn"),
            ("OspfArea", "Inactive", "yyn"),
            ("OspfInstance", "Version", "yyn"),
            ("OspfInstance", "Vrf", "yyn"),
            ("OspfInstance", "OriginateDefault", "yyn"),
            ("OspfInstance", "Redistribute", "yyn"),
            ("OspfInstance", "InFilterChain", "yyn"),
            ("OspfInstance", "OutFilterChain", "yyn"),
            ("OspfInstance", "OutFilterSelect", "yyn"),
            ("OspfInstance", "MplsTeAddress", "yyn"),
            ("OspfInstance", "Inactive", "yyn"),
            ("OspfInterfaceTemplate", "", "yyn"),
            ("OspfInterfaceTemplate", "VlinkNeighborId", "nyx"),
            ("OspfInterfaceTemplate", "VlinkTransitArea", "nyx"),
            ("OspfNeighbor", "Area", "yyn"),
            ("OspfNeighbor", "Dr", "yyn"),
            ("OspfNeighbor", "Bdr", "yyn"),
            ("OspfNeighbor", "Timeout", "yyn"),
            ("OspfNeighbor", "Dynamic", "yyn"),
            ("OspfNeighbor", "Virtual", "yyn"),
            ("OspfNeighbor", "Comment", "yyn"),
            ("RoutingRule", "", "yyn"),
            ("RoutingTable", "", "yyn"),
            ("Snmp", "EngineIdSuffix", "yyn"),
            ("Snmp", "Vrf", "yyn"),
            ("SystemLogging", "Regex", "yyn"),
            ("SystemLoggingAction", "RemoteProtocol", "yyn"),
            ("SystemLoggingAction", "RemoteLogFormat", "yyn"),
            ("SystemLoggingAction", "Vrf", "yyn"),
            ("SystemLoggingAction", "CefEventDelimiter", "yyn"),
            ("SystemNote", "ShowAtCliLogin", "yyn"),
            ("SystemNtpClient", "Servers", "yyn"),
            ("SystemNtpClient", "Vrf", "yyn"),
            ("SystemNtpClient", "FreqDrift", "yyn"),
            ("SystemNtpClient", "Status", "yyn"),
            ("SystemNtpClient", "SyncedServer", "yyn"),
            ("SystemNtpClient", "SyncedStratum", "yyn"),
            ("SystemNtpClient", "SystemOffset", "yyn"),
            ("SystemNtpServer", "", "yyn"),
            ("SystemPackage", "Size", "yyn"),
            ("SystemPackage", "Available", "yyn"),
            ("ToolBandwidthServer", "AllowedAddresses4", "yyn"),
            ("ToolBandwidthServer", "AllowedAddresses6", "yyn"),
            ("ToolEmail", "Tls", "yyn"),
            ("ToolEmail", "CertificateVerification", "yyn"),
            ("ToolEmail", "Vrf", "yyn"),
            ("ToolNetwatch", "Name", "yyn"),
            ("ToolNetwatch", "Type", "yyn"),
            ("ToolNetwatch", "StartDelay", "yyn"),
            ("ToolNetwatch", "StartupDelay", "yyn"),
            ("ToolNetwatch", "TestScript", "yyn"),
            ("ToolNetwatch", "IgnoreInitialUp", "yyn"),
            ("ToolNetwatch", "IgnoreInitialDown", "yyn"),
            ("ToolNetwatch", "SrcAddress", "yyn"),
            ("ToolNetwatch", "PacketCount", "yyn"),
            ("ToolNetwatch", "PacketInterval", "yyn"),
            ("ToolNetwatch", "PacketSize", "yyn"),
            ("ToolNetwatch", "Ttl", "yyn"),
            ("ToolNetwatch", "AcceptIcmpTimeExceeded", "yyn"),
            ("ToolNetwatch", "EarlyFailureDetection", "yyn"),
            ("ToolNetwatch", "EarlySuccessDetection", "yyn"),
            ("ToolNetwatch", "ThrMax", "yyn"),
            ("ToolNetwatch", "ThrAvg", "yyn"),
            ("ToolNetwatch", "ThrStdev", "yyn"),
            ("ToolNetwatch", "ThrJitter", "yyn"),
            ("ToolNetwatch", "ThrLossPercent", "yyn"),
            ("ToolNetwatch", "ThrLossCount", "yyn"),
            ("ToolNetwatch", "Port", "yyn"),
            ("ToolNetwatch", "ThrTcpConnTime", "yyn"),
            ("ToolNetwatch", "HttpCodes", "yyn"),
            ("ToolNetwatch", "ThrHttpTime", "yyn"),
            ("ToolNetwatch", "Certificate", "yyn"),
            ("ToolNetwatch", "CheckCertificate", "yyn"),
            ("ToolNetwatch", "RecordType", "yyn"),
            ("ToolNetwatch", "DnsServer", "yyn"),
            ("ToolNetwatch", "DoneTests", "yyn"),
            ("ToolNetwatch", "FailedTests", "yyn"),
            ("ToolNetwatch", "SentCount", "yyn"),
            ("ToolNetwatch", "ResponseCount", "yyn"),
            ("ToolNetwatch", "LossCount", "yyn"),
            ("ToolNetwatch", "LossPercent", "yyn"),
            ("ToolNetwatch", "TcpConnectTime", "yyn"),
        };

        private static Type EntityType(string name)
            => typeof(TikEntityAttribute).Assembly.GetTypes().Single(t => t.Name == name && t.GetCustomAttribute<TikEntityAttribute>() != null);

        private static (string? Min, string? Max) Bounds(Type type, string property)
        {
            if (property.Length == 0)
            {
                var entity = type.GetCustomAttribute<TikEntityAttribute>()!;
                return (entity.MinRouterOs, entity.MaxRouterOs);
            }
            var field = type.GetProperty(property)!.GetCustomAttribute<TikPropertyAttribute>()!;
            return (field.MinRouterOs, field.MaxRouterOs);
        }

        [TestMethod]
        public void EveryMeasuredDifferenceHasTheBoundThatSaysIt()
        {
            var wrong = new List<string>();
            foreach (var (cls, property, presence) in Measured)
            {
                var (min, max) = Bounds(EntityType(cls), property);
                for (int i = 0; i < LabRouters.Length; i++)
                {
                    if (presence[i] == 'x')
                        continue;   // the menu is missing there: the menu's own bound says so
                    bool said = TikRouterOsRange.Includes(min, max, LabRouters[i]);
                    if (said != (presence[i] == 'y'))
                        wrong.Add($"{cls}.{(property.Length == 0 ? "[menu]" : property)} on {LabRouters[i]}: measured {presence[i]}, "
                            + $"bounds {min ?? "-"}..{max ?? "-"}");
                }
            }
            Assert.AreEqual(0, wrong.Count, string.Join(Environment.NewLine, wrong));
        }

        [TestMethod]
        public void EveryBoundWasMeasured()
        {
            var measured = new HashSet<string>(Measured.Select(m => m.Class + "." + m.Property));
            var unmeasured = new List<string>();
            foreach (var type in typeof(TikEntityAttribute).Assembly.GetTypes().Where(t => t.GetCustomAttribute<TikEntityAttribute>() != null))
            {
                var entity = type.GetCustomAttribute<TikEntityAttribute>()!;
                if ((entity.MinRouterOs ?? entity.MaxRouterOs) != null && !measured.Contains(type.Name + "."))
                    unmeasured.Add(type.Name);
                foreach (var property in type.GetProperties())
                {
                    var field = property.GetCustomAttribute<TikPropertyAttribute>();
                    if (field != null && (field.MinRouterOs ?? field.MaxRouterOs) != null && !measured.Contains(type.Name + "." + property.Name))
                        unmeasured.Add(type.Name + "." + property.Name);
                }
            }
            Assert.AreEqual(0, unmeasured.Count, "a bound with no measurement behind it: " + string.Join(", ", unmeasured));
        }

        [DataTestMethod]
        [DataRow(null, null, "6.49.13", true)]
        [DataRow("7", null, "7.0.1", true)]
        [DataRow("7", null, "6.49.13", false)]
        [DataRow("7.22", null, "7.21.5", false)]
        [DataRow("7.22", null, "7.22.0", true)]
        [DataRow(null, "6", "6.49.13", true)]
        [DataRow(null, "6", "7.1", false)]
        [DataRow(null, "7.21", "7.21.5", true)]
        [DataRow(null, "7.21", "7.24.5", false)]
        public void ABoundComparesOnItsOwnParts(string? min, string? max, string version, bool expected)
            => Assert.AreEqual(expected, TikRouterOsRange.Includes(min, max, new Version(version)));

        [TestMethod]
        public void HasReadsTheMenuAndThePropertyBounds()
        {
            var v6 = new Version(6, 49, 13);
            var v7 = new Version(7, 24, 5);
            Assert.IsTrue(TikRouterOsRange.Has<tik4net.Objects.Ip.Firewall.FirewallFilter>(v6));
            Assert.IsFalse(TikRouterOsRange.Has<tik4net.Objects.Ip.Firewall.FirewallFilter>(v6, nameof(tik4net.Objects.Ip.Firewall.FirewallFilter.Realm)));
            Assert.IsTrue(TikRouterOsRange.Has<tik4net.Objects.Ip.Firewall.FirewallFilter>(v7, nameof(tik4net.Objects.Ip.Firewall.FirewallFilter.Realm)));
            Assert.ThrowsException<ArgumentException>(() => TikRouterOsRange.Has<tik4net.Objects.Ip.Firewall.FirewallFilter>(v7, "NoSuchProperty"));
        }
    }
}
