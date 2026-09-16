using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface
{
    /// <summary>
    /// interface/bridge
    /// Ethernet-like networks (Ethernet, Ethernet over IP, IEEE802.11 in ap-bridge or bridge mode, WDS, VLAN) can be connected together using MAC bridges. The bridge feature allows the interconnection of hosts connected to separate LANs (using EoIP, geographically distributed networks can be bridged as well if any kind of IP network interconnection exists between them) as if they were attached to a single LAN. As bridges are transparent, they do not appear in traceroute list, and no utility can make a distinction between a host working in one LAN and a host working in another LAN if these LANs are bridged (depending on the way the LANs are interconnected, latency and data rate between hosts may vary).
    /// Network loops may emerge (intentionally or not) in complex topologies. Without any special treatment, loops would prevent network from functioning normally, as they would lead to avalanche-like packet multiplication. Each bridge runs an algorithm which calculates how the loop can be prevented. STP and RSTP allows bridges to communicate with each other, so they can negotiate a loop free topology. All other alternative connections that would otherwise form loops, are put to standby, so that should the main connection fail, another connection could take its place. This algorithm exchanges  configuration messages (BPDU - Bridge Protocol Data Unit) periodically, so that all bridges are updated with the newest information about changes in network topology. (R)STP selects a root bridge which is responsible for network reconfiguration, such as blocking and opening ports on other bridges. The root bridge is the bridge with the lowest bridge ID.
    /// </summary>
    /// <remarks>
    /// Field set measured on RouterOS 7.24 (<c>/interface bridge add</c> Tab-completion plus <c>print detail</c>).
    /// Every writable property is nullable and a fresh instance holds <c>null</c> everywhere, so an <c>add</c>
    /// sends only what the caller assigned and the router applies its own defaults. The IGMP/MLD snooping
    /// fields are printed only while <see cref="IgmpSnooping"/> is on, and the VLAN and MSTP fields only while
    /// <see cref="VlanFiltering"/> is on or <see cref="ProtocolMode"/> is <c>mstp</c>; on other rows they
    /// read back as <c>null</c>.
    /// </remarks>
    [TikEntity("/interface/bridge", IncludeDetails = true)]
    public class InterfaceBridge
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// name: Name of the bridge interface
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string?/*text*/ Name { get; set; }

        /// <summary>
        /// comment: Short description of the bridge.
        /// </summary>
        [TikProperty("comment")]
        public string? Comment { get; set; }

        /// <summary>
        /// disabled: Whether the bridge is disabled.
        /// </summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool? Disabled { get; set; }

        /// <summary>
        /// admin-mac: Static MAC address of the bridge (takes effect if auto-mac=no)
        /// </summary>
        [TikProperty("admin-mac")]
        public string?/*MAC address*/ AdminMac { get; set; }

        /// <summary>
        /// ageing-time: How long a host's information will be kept in the bridge database. Router default: 5m.
        /// </summary>
        [TikProperty("ageing-time", DefaultValue = "5m")]
        public TikDuration? AgeingTime { get; set; }

        /// <summary>
        /// Address Resolution Protocol setting
        /// </summary>
        /// <seealso cref="Arp"/>
        public enum ArpMode
        {
            /// <summary>
            /// disabled - the interface will not use ARP
            /// </summary>
            [TikEnum("disabled")]
            Disabled,
            /// <summary>
            /// enabled - the interface will use ARP
            /// </summary>
            [TikEnum("enabled")]
            Enabled,
            /// <summary>
            /// proxy-arp - the interface will use the ARP proxy feature
            /// </summary>
            [TikEnum("proxy-arp")]
            ProxyArp,
            /// <summary>
            /// reply-only - the interface will only reply to requests originated from matching IP address/MAC address combinations which are entered as static entries in the "/ip arp" table. No dynamic entries will be automatically stored in the "/ip arp" table. Therefore for communications to be successful, a valid static entry must already exist.
            /// </summary>
            [TikEnum("reply-only")]
            ReplyOnly,
            /// <summary>
            /// local-proxy-arp - the interface performs proxy ARP and answers back out of the same interface,
            /// so hosts on one segment that cannot reach each other directly (client isolation) still resolve
            /// each other through the router.
            /// </summary>
            [TikEnum("local-proxy-arp")]
            LocalProxyArp,
        }

        /// <summary>
        /// arp
        /// Address Resolution Protocol setting
        ///          disabled - the interface will not use ARP
        ///          enabled - the interface will use ARP
        ///          proxy-arp - the interface will use the ARP proxy feature
        ///          local-proxy-arp - proxy ARP answered back out of the same interface
        ///          reply-only - the interface will only reply to requests originated from matching IP address/MAC address combinations which are entered as static entries in the "/ip arp" table. No dynamic entries will be automatically stored in the "/ip arp" table. Therefore for communications to be successful, a valid static entry must already exist.
        /// </summary>
        /// <seealso cref="ArpMode"/>
        [TikProperty("arp", DefaultValue = "enabled")]
        public ArpMode? Arp { get; set; }

        /// <summary>
        /// arp-timeout: How long an ARP entry learned on the bridge is kept. Router default: <c>auto</c>, which
        /// reads back as <see cref="TikDuration.Token"/>.
        /// </summary>
        [TikProperty("arp-timeout", DefaultValue = "auto")]
        public TikDuration? ArpTimeout { get; set; }

        /// <summary>
        /// auto-mac: Automatically select the smallest MAC address of bridge ports as a bridge MAC address
        /// </summary>
        [TikProperty("auto-mac", DefaultValue = "yes")]
        public bool? AutoMac { get; set; }

        /// <summary>
        /// fast-forward: Speeds up forwarding between exactly two ports of the bridge by skipping bridge
        /// processing where no bridge feature needs it. Router default: yes.
        /// </summary>
        [TikProperty("fast-forward", DefaultValue = "yes")]
        public bool? FastForward { get; set; }

        /// <summary>
        /// forward-delay: Time which is spent during the initialization phase of the bridge interface (i.e., after router startup or enabling the interface) in listening/learning state before the bridge will start functioning normally. Router default: 15s.
        /// </summary>
        [TikProperty("forward-delay", DefaultValue = "15s")]
        public TikDuration? ForwardDelay { get; set; }

        /// <summary>
        /// forward-reserved-addresses: Reserved multicast destination MAC addresses (01:80:C2:00:00:0x) the bridge
        /// forwards instead of consuming.
        /// </summary>
        [TikProperty("forward-reserved-addresses")]
        public string?/*MAC address list*/ ForwardReservedAddresses { get; set; }

        /// <summary>
        /// max-learned-entries: Maximum number of host entries the bridge learns. Router default: <c>auto</c>.
        /// </summary>
        [TikProperty("max-learned-entries", DefaultValue = "auto")]
        public string?/*integer | auto*/ MaxLearnedEntries { get; set; }

        /// <summary>
        /// max-message-age: How long to remember Hello messages received from other bridges. Router default: 20s.
        /// </summary>
        [TikProperty("max-message-age", DefaultValue = "20s")]
        public TikDuration? MaxMessageAge { get; set; }

        /// <summary>
        /// mtu: Maximum Transmission Unit. Router default: <c>auto</c> (see <see cref="ActualMtu"/>).
        /// </summary>
        [TikProperty("mtu", DefaultValue = "auto")]
        public string?/*integer | auto*/ Mtu { get; set; }

        /// <summary>
        /// priority
        /// Spanning tree protocol priority for bridge interface. Bridge with the smallest (lowest) bridge ID becomes a Root-Bridge. Bridge ID consists of two numbers - priority and MAC address of the bridge. To compare two bridge IDs, the priority is compared first. If two bridges have equal priority, then the MAC addresses are compared.
        /// The router writes it in hex; router default: <c>0x8000</c>.
        /// </summary>
        [TikProperty("priority", DefaultValue = "0x8000")]
        public string?/*integer: 0..65535 decimal format or 0x0000-0xffff hex format*/ Priority { get; set; }

        /// <summary>
        /// protocol-mode: Select Spanning tree protocol (STP), Rapid spanning tree protocol (RSTP) or Multiple spanning tree protocol (MSTP) to ensure a loop-free topology for any bridged LAN. RSTP provides for faster spanning tree convergence after a topology change; MSTP runs one tree per group of VLANs.
        /// </summary>
        /// <seealso cref="ProtocolMode"/>
        public enum ProtocolModeModes
        {
            /// <summary>
            /// none - no spanning tree protocol runs on the bridge.
            /// </summary>
            [TikEnum("none")]
            None,
            /// <summary>
            /// rstp - Rapid spanning tree protocol (RSTP), which converges faster than STP after a topology change.
            /// </summary>
            [TikEnum("rstp")]
            Rstp,
            /// <summary>
            /// stp - Spanning tree protocol (STP), to ensure a loop-free topology for any bridged LAN.
            /// </summary>
            [TikEnum("stp")]
            Stp,
            /// <summary>
            /// mstp - Multiple spanning tree protocol (MSTP), which runs a separate spanning tree for each group
            /// of VLANs (MST instance). RouterOS accepts it only together with <see cref="VlanFiltering"/>.
            /// </summary>
            [TikEnum("mstp")]
            Mstp,
        }

        /// <summary>
        /// protocol-mode: Select Spanning tree protocol (STP), Rapid spanning tree protocol (RSTP) or Multiple spanning tree protocol (MSTP) to ensure a loop-free topology for any bridged LAN. RSTP provides for faster spanning tree convergence after a topology change; MSTP runs one tree per group of VLANs.
        /// </summary>
        /// <seealso cref="ProtocolModeModes"/>
        [TikProperty("protocol-mode", DefaultValue = "rstp")]
        public ProtocolModeModes? ProtocolMode { get; set; }

        /// <summary>
        /// Path cost calculation for the bridge ports.
        /// </summary>
        /// <seealso cref="PortCostMode"/>
        public enum PortCostModeType
        {
            /// <summary>long - 32-bit path costs (IEEE 802.1D-2004 and later).</summary>
            [TikEnum("long")]
            Long,
            /// <summary>short - 16-bit path costs (IEEE 802.1D-1998).</summary>
            [TikEnum("short")]
            Short,
        }

        /// <summary>
        /// port-cost-mode: Whether port path costs are calculated in the long (32-bit) or short (16-bit) form.
        /// Router default: long.
        /// </summary>
        /// <seealso cref="PortCostModeType"/>
        [TikProperty("port-cost-mode", DefaultValue = "long")]
        public PortCostModeType? PortCostMode { get; set; }

        /// <summary>
        /// transmit-hold-count: The Transmit Hold Count used by the Port Transmit state machine to limit transmission rate. Router default: 6.
        /// </summary>
        [TikProperty("transmit-hold-count", DefaultValue = "6")]
        public int?/*integer: 1..10*/ TransmitHoldCount { get; set; }

        /// <summary>
        /// max-hops: MSTP — how many bridges a BPDU travels within a region before it is discarded. Router default: 20.
        /// </summary>
        [TikProperty("max-hops", DefaultValue = "20")]
        public int? MaxHops { get; set; }

        /// <summary>
        /// region-name: MSTP region name; bridges in one region must agree on it, on
        /// <see cref="RegionRevision"/> and on the VLAN-to-instance mapping.
        /// </summary>
        [TikProperty("region-name")]
        public string? RegionName { get; set; }

        /// <summary>
        /// region-revision: MSTP region revision. Router default: 0.
        /// </summary>
        [TikProperty("region-revision", DefaultValue = "0")]
        public int? RegionRevision { get; set; }

        /// <summary>
        /// vlan-filtering: Whether the bridge filters traffic by VLAN (the <c>/interface/bridge/vlan</c> table and
        /// each port's <c>pvid</c>). RouterOS refuses <see cref="ProtocolModeModes.Mstp"/> on a bridge without it
        /// ("mstp requires vlan-filtering"), so an MSTP bridge sets both. <c>null</c> leaves the router's
        /// default (<c>no</c>).
        /// </summary>
        [TikProperty("vlan-filtering", DefaultValue = "no")]
        public bool? VlanFiltering { get; set; }

        /// <summary>
        /// ether-type: The EtherType the bridge treats as the VLAN tag (<c>0x8100</c>, <c>0x88a8</c> or <c>0x9100</c>).
        /// Router default: 0x8100.
        /// </summary>
        [TikProperty("ether-type", DefaultValue = "0x8100")]
        public string? EtherType { get; set; }

        /// <summary>
        /// Which frames the bridge interface itself admits when <see cref="VlanFiltering"/> is on.
        /// </summary>
        /// <seealso cref="FrameTypes"/>
        public enum FrameTypesMode
        {
            /// <summary>admit-all - tagged, untagged and priority-tagged frames.</summary>
            [TikEnum("admit-all")]
            AdmitAll,
            /// <summary>admit-only-untagged-and-priority-tagged - untagged and priority-tagged frames only.</summary>
            [TikEnum("admit-only-untagged-and-priority-tagged")]
            AdmitOnlyUntaggedAndPriorityTagged,
            /// <summary>admit-only-vlan-tagged - VLAN-tagged frames only.</summary>
            [TikEnum("admit-only-vlan-tagged")]
            AdmitOnlyVlanTagged,
        }

        /// <summary>
        /// frame-types: Which frames the bridge interface admits when <see cref="VlanFiltering"/> is on.
        /// Router default: admit-all.
        /// </summary>
        /// <seealso cref="FrameTypesMode"/>
        [TikProperty("frame-types", DefaultValue = "admit-all")]
        public FrameTypesMode? FrameTypes { get; set; }

        /// <summary>
        /// ingress-filtering: Whether frames arriving at the bridge interface are dropped when their VLAN is not
        /// a member of it. Router default: yes.
        /// </summary>
        [TikProperty("ingress-filtering", DefaultValue = "yes")]
        public bool? IngressFiltering { get; set; }

        /// <summary>
        /// pvid: Port VLAN ID of the bridge interface itself, used for untagged traffic when
        /// <see cref="VlanFiltering"/> is on. Router default: 1.
        /// </summary>
        [TikProperty("pvid", DefaultValue = "1")]
        public int?/*integer: 1..4094*/ Pvid { get; set; }

        /// <summary>
        /// mvrp: Whether the Multiple VLAN Registration Protocol runs on the bridge. Router default: no.
        /// </summary>
        [TikProperty("mvrp", DefaultValue = "no")]
        public bool? Mvrp { get; set; }

        /// <summary>
        /// igmp-snooping: Whether the bridge snoops IGMP/MLD and forwards multicast only to ports that asked for it.
        /// Router default: no.
        /// </summary>
        [TikProperty("igmp-snooping", DefaultValue = "no")]
        public bool? IgmpSnooping { get; set; }

        /// <summary>
        /// igmp-version: IGMP version used by the snooping querier (2 or 3). Router default: 2.
        /// </summary>
        [TikProperty("igmp-version", DefaultValue = "2")]
        public int? IgmpVersion { get; set; }

        /// <summary>
        /// mld-version: MLD version used by the snooping querier (1 or 2). Router default: 1.
        /// </summary>
        [TikProperty("mld-version", DefaultValue = "1")]
        public int? MldVersion { get; set; }

        /// <summary>
        /// multicast-querier: Whether the bridge sends IGMP/MLD general queries itself. Router default: no.
        /// </summary>
        [TikProperty("multicast-querier", DefaultValue = "no")]
        public bool? MulticastQuerier { get; set; }

        /// <summary>
        /// Whether the bridge interface is treated as a multicast router port.
        /// </summary>
        /// <seealso cref="MulticastRouter"/>
        public enum MulticastRouterMode
        {
            /// <summary>disabled - never a multicast router port.</summary>
            [TikEnum("disabled")]
            Disabled,
            /// <summary>permanent - always a multicast router port.</summary>
            [TikEnum("permanent")]
            Permanent,
            /// <summary>temporary-query - a multicast router port while IGMP/MLD queries are seen on it.</summary>
            [TikEnum("temporary-query")]
            TemporaryQuery,
        }

        /// <summary>
        /// multicast-router: Whether the bridge interface is a multicast router port. Router default: temporary-query.
        /// </summary>
        /// <seealso cref="MulticastRouterMode"/>
        [TikProperty("multicast-router", DefaultValue = "temporary-query")]
        public MulticastRouterMode? MulticastRouter { get; set; }

        /// <summary>
        /// querier-uses-bridge-address: Whether the querier sends with the bridge's own IP address rather than
        /// 0.0.0.0. Router default: yes.
        /// </summary>
        [TikProperty("querier-uses-bridge-address", DefaultValue = "yes")]
        public bool? QuerierUsesBridgeAddress { get; set; }

        /// <summary>
        /// last-member-interval: Interval between group-specific queries after a leave. Router default: 1s.
        /// </summary>
        [TikProperty("last-member-interval", DefaultValue = "1s")]
        public TikDuration? LastMemberInterval { get; set; }

        /// <summary>
        /// last-member-query-count: How many group-specific queries are sent after a leave. Router default: 2.
        /// </summary>
        [TikProperty("last-member-query-count", DefaultValue = "2")]
        public int? LastMemberQueryCount { get; set; }

        /// <summary>
        /// membership-interval: How long a group membership is kept without a report. Router default: 4m20s.
        /// </summary>
        [TikProperty("membership-interval", DefaultValue = "4m20s")]
        public TikDuration? MembershipInterval { get; set; }

        /// <summary>
        /// querier-interval: How long another querier is considered present after its last query. Router default: 4m15s.
        /// </summary>
        [TikProperty("querier-interval", DefaultValue = "4m15s")]
        public TikDuration? QuerierInterval { get; set; }

        /// <summary>
        /// query-interval: Interval between general queries. Router default: 2m5s.
        /// </summary>
        [TikProperty("query-interval", DefaultValue = "2m5s")]
        public TikDuration? QueryInterval { get; set; }

        /// <summary>
        /// query-response-interval: Maximum response time advertised in general queries. Router default: 10s.
        /// </summary>
        [TikProperty("query-response-interval", DefaultValue = "10s")]
        public TikDuration? QueryResponseInterval { get; set; }

        /// <summary>
        /// startup-query-count: How many general queries are sent at startup. Router default: 2.
        /// </summary>
        [TikProperty("startup-query-count", DefaultValue = "2")]
        public int? StartupQueryCount { get; set; }

        /// <summary>
        /// startup-query-interval: Interval between general queries at startup. Router default: 31s250ms.
        /// </summary>
        [TikProperty("startup-query-interval", DefaultValue = "31s250ms")]
        public TikDuration? StartupQueryInterval { get; set; }

        /// <summary>
        /// dhcp-snooping: Whether the bridge snoops DHCP and drops server messages arriving on untrusted ports.
        /// Router default: no.
        /// </summary>
        [TikProperty("dhcp-snooping", DefaultValue = "no")]
        public bool? DhcpSnooping { get; set; }

        /// <summary>
        /// dhcp-agent-circuit-id: The DHCP Option 82 circuit ID the bridge inserts while <see cref="DhcpSnooping"/> is on.
        /// </summary>
        [TikProperty("dhcp-agent-circuit-id")]
        public string? DhcpAgentCircuitId { get; set; }

        /// <summary>
        /// dhcp-agent-remote-id: The DHCP Option 82 remote ID the bridge inserts while <see cref="DhcpSnooping"/> is on.
        /// </summary>
        [TikProperty("dhcp-agent-remote-id")]
        public string? DhcpAgentRemoteId { get; set; }

        /// <summary>
        /// dhcpv6-snooping: Whether the bridge snoops DHCPv6 and drops server messages arriving on untrusted ports.
        /// Router default: no.
        /// </summary>
        [TikProperty("dhcpv6-snooping", DefaultValue = "no")]
        public bool? Dhcpv6Snooping { get; set; }

        /// <summary>
        /// dhcpv6-agent-circuit-id: The DHCPv6 interface ID the bridge inserts while <see cref="Dhcpv6Snooping"/> is on.
        /// </summary>
        [TikProperty("dhcpv6-agent-circuit-id")]
        public string? Dhcpv6AgentCircuitId { get; set; }

        /// <summary>
        /// dhcpv6-agent-remote-id: The DHCPv6 remote ID the bridge inserts while <see cref="Dhcpv6Snooping"/> is on.
        /// </summary>
        [TikProperty("dhcpv6-agent-remote-id")]
        public string? Dhcpv6AgentRemoteId { get; set; }

        /// <summary>
        /// ra-guard: Whether the bridge drops IPv6 router advertisements arriving on untrusted ports. Router default: no.
        /// </summary>
        [TikProperty("ra-guard", DefaultValue = "no")]
        public bool? RaGuard { get; set; }

        /// <summary>
        /// mlag-peer-port: The interface that links this bridge to its MLAG peer, or <c>none</c>. Router default: none.
        /// </summary>
        [TikProperty("mlag-peer-port", DefaultValue = "none")]
        public string? MlagPeerPort { get; set; }

        /// <summary>
        /// mlag-priority: MLAG priority; the lower value becomes the primary peer. Router default: 128.
        /// </summary>
        [TikProperty("mlag-priority", DefaultValue = "128")]
        public int? MlagPriority { get; set; }

        /// <summary>
        /// mlag-heartbeat: Interval between MLAG heartbeat messages. Router default: 5s.
        /// </summary>
        [TikProperty("mlag-heartbeat", DefaultValue = "5s")]
        public TikDuration? MlagHeartbeat { get; set; }

        /// <summary>
        /// l2mtu: Layer2 Maximum transmission unit.  read more&#187;
        /// </summary>
        [TikProperty("l2mtu", IsReadOnly = true)]
        public string?/*integer; read-only*/ L2mtu { get; private set; }

        /// <summary>
        /// actual-mtu: The MTU in effect, which is what <see cref="Mtu"/> = <c>auto</c> resolved to.
        /// </summary>
        [TikProperty("actual-mtu", IsReadOnly = true)]
        public string? ActualMtu { get; private set; }

        /// <summary>
        /// mac-address: The MAC address the bridge currently uses.
        /// </summary>
        [TikProperty("mac-address", IsReadOnly = true)]
        public string?/*MAC address*/ MacAddress { get; private set; }

        /// <summary>
        /// running: Whether the bridge is up.
        /// </summary>
        [TikProperty("running", IsReadOnly = true)]
        public bool Running { get; private set; }

        /// <summary>
        /// dynamic: Whether the bridge was created by another feature rather than configured.
        /// </summary>
        [TikProperty("dynamic", IsReadOnly = true)]
        public bool Dynamic { get; private set; }

        /// <summary>
        /// managed: Whether the bridge is managed by another feature (e.g. CAPsMAN or quickset), which owns its configuration.
        /// </summary>
        [TikProperty("managed", IsReadOnly = true)]
        public bool Managed { get; private set; }

        /// <inheritdoc/>
        public override string ToString() => Name ?? string.Empty;
    }
}
