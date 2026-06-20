using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface
{
    /// <summary>
    /// /interface/bonding
    /// Bonding is a technology that allows aggregation of multiple ethernet-like interfaces into a
    /// single virtual link, thus getting higher data rates and providing failover.
    /// See https://help.mikrotik.com/docs/display/ROS/Bonding
    /// </summary>
    [TikEntity("/interface/bonding", IncludeDetails = true)]
    public class InterfaceBonding
    {
        /// <summary>.id — primary key</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>name — Name of the bonding interface.</summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>mtu — Maximum Transmit Unit in bytes. Real default: 1500. DefaultValue="0" prevents sending 0 on add.</summary>
        [TikProperty("mtu", DefaultValue = "0")]
        public int Mtu { get; set; }

        /// <summary>mac-address — MAC address of the bonding interface (assigned from slaves).</summary>
        [TikProperty("mac-address", IsReadOnly = true)]
        public string MacAddress { get; private set; }

        /// <summary>slaves — Ethernet-like interfaces to include in the bond (comma-separated). At least one required.</summary>
        [TikProperty("slaves", IsMandatory = true)]
        public string Slaves { get; set; }

        public enum BondingMode
        {
            /// <summary>balance-rr — Round-Robin transmit policy.</summary>
            [TikEnum("balance-rr")] BalanceRr,
            /// <summary>active-backup — Only one slave is active; another becomes active if the active slave fails.</summary>
            [TikEnum("active-backup")] ActiveBackup,
            /// <summary>balance-xor — Transmit based on hashed (src XOR dst) MAC addresses.</summary>
            [TikEnum("balance-xor")] BalanceXor,
            /// <summary>broadcast — Transmits everything on all slave interfaces.</summary>
            [TikEnum("broadcast")] Broadcast,
            /// <summary>802.3ad — IEEE 802.3ad Dynamic link aggregation (LACP).</summary>
            [TikEnum("802.3ad")] Ieee8023ad,
            /// <summary>balance-tlb — Adaptive Transmit Load Balancing.</summary>
            [TikEnum("balance-tlb")] BalanceTlb,
            /// <summary>balance-alb — Adaptive Load Balancing.</summary>
            [TikEnum("balance-alb")] BalanceAlb,
        }

        /// <summary>mode — Bonding policy. Default: balance-rr.</summary>
        /// <seealso cref="BondingMode"/>
        [TikProperty("mode", DefaultValue = "balance-rr")]
        public BondingMode Mode { get; set; }

        /// <summary>primary — Controls primary interface for active-backup, balance-tlb and balance-alb modes.</summary>
        [TikProperty("primary", DefaultValue = "none")]
        public string Primary { get; set; }

        public enum LinkMonitoringMode
        {
            /// <summary>mii — MII (Media Independent Interface) link monitoring.</summary>
            [TikEnum("mii")] Mii,
            /// <summary>arp — ARP monitoring.</summary>
            [TikEnum("arp")] Arp,
            /// <summary>none — No link monitoring.</summary>
            [TikEnum("none")] None,
        }

        /// <summary>link-monitoring — Method used for monitoring link status. Default: mii.</summary>
        /// <seealso cref="LinkMonitoringMode"/>
        [TikProperty("link-monitoring", DefaultValue = "mii")]
        public LinkMonitoringMode LinkMonitoring { get; set; }

        /// <summary>mii-interval — How often to monitor link failures when link-monitoring=mii. Real default: 100ms.</summary>
        [TikProperty("mii-interval", DefaultValue = "100ms")]
        public string/*time*/ MiiInterval { get; set; }

        /// <summary>arp-interval — How often to monitor ARP requests when link-monitoring=arp. Real default: 100ms.</summary>
        [TikProperty("arp-interval", DefaultValue = "100ms")]
        public string/*time*/ ArpInterval { get; set; }

        /// <summary>arp-ip-targets — IP addresses monitored when link-monitoring=arp (comma-separated).</summary>
        [TikProperty("arp-ip-targets", DefaultValue = "")]
        public string ArpIpTargets { get; set; }

        public enum ArpMode
        {
            /// <summary>enabled — Interface uses ARP.</summary>
            [TikEnum("enabled")] Enabled,
            /// <summary>disabled — Interface will not use ARP.</summary>
            [TikEnum("disabled")] Disabled,
            /// <summary>proxy-arp — Interface uses the ARP proxy feature.</summary>
            [TikEnum("proxy-arp")] ProxyArp,
            /// <summary>reply-only — Interface will only reply to requests matching static ARP entries.</summary>
            [TikEnum("reply-only")] ReplyOnly,
        }

        /// <summary>arp — Address Resolution Protocol setting. Default: enabled.</summary>
        /// <seealso cref="ArpMode"/>
        [TikProperty("arp", DefaultValue = "enabled")]
        public ArpMode Arp { get; set; }

        /// <summary>arp-timeout — How long to keep ARP entries. Real default: auto.</summary>
        [TikProperty("arp-timeout", DefaultValue = "auto")]
        public string/*time*/ ArpTimeout { get; set; }

        /// <summary>down-delay — Time to disable interface after link failure is detected. Default: 0ms.</summary>
        [TikProperty("down-delay", DefaultValue = "0ms")]
        public string/*time*/ DownDelay { get; set; }

        /// <summary>up-delay — Time to disable interface after a link is brought up. Default: 0ms.</summary>
        [TikProperty("up-delay", DefaultValue = "0ms")]
        public string/*time*/ UpDelay { get; set; }

        /// <summary>min-links — Minimum number of active slave links required for bonding to be active. Default: 0 (disabled).</summary>
        [TikProperty("min-links", DefaultValue = "0")]
        public int MinLinks { get; set; }

        /// <summary>forced-mac-address — Static MAC address to use for the bond interface instead of deriving from slaves.</summary>
        [TikProperty("forced-mac-address", DefaultValue = "")]
        public string/*MAC*/ ForcedMacAddress { get; set; }

        public enum LacpRateMode
        {
            /// <summary>30secs — Slow LACPDU exchange (every 30 seconds).</summary>
            [TikEnum("30secs")] ThirtySecs,
            /// <summary>1sec — Fast LACPDU exchange (every 1 second).</summary>
            [TikEnum("1sec")] OneSec,
        }

        /// <summary>lacp-rate — Frequency of LACPDU exchange with bonding peers in 802.3ad mode. Default: 30secs.</summary>
        /// <seealso cref="LacpRateMode"/>
        [TikProperty("lacp-rate", DefaultValue = "30secs")]
        public LacpRateMode LacpRate { get; set; }

        public enum LacpParticipationMode
        {
            /// <summary>active — Actively initiates LACP negotiation.</summary>
            [TikEnum("active")] Active,
            /// <summary>passive — Only responds to LACP negotiation initiated by peer.</summary>
            [TikEnum("passive")] Passive,
        }

        /// <summary>lacp-mode — LACP participation mode for ports in 802.3ad mode. Default: active.</summary>
        /// <seealso cref="LacpParticipationMode"/>
        [TikProperty("lacp-mode", DefaultValue = "active")]
        public LacpParticipationMode LacpMode { get; set; }

        /// <summary>lacp-system-id — MAC address to use as the LACP system ID (overrides the default).</summary>
        [TikProperty("lacp-system-id", DefaultValue = "")]
        public string/*MAC*/ LacpSystemId { get; set; }

        /// <summary>lacp-system-priority — LACP system priority (1–65535). Real default: 65535. DefaultValue="0" prevents sending 0 on add.</summary>
        [TikProperty("lacp-system-priority", DefaultValue = "0")]
        public int LacpSystemPriority { get; set; }

        /// <summary>lacp-user-key — Upper 10 bits of the LACP port key (0–1023). Default: 0.</summary>
        [TikProperty("lacp-user-key", DefaultValue = "0")]
        public int LacpUserKey { get; set; }

        public enum TransmitHashPolicyMode
        {
            /// <summary>layer-2 — Uses MAC addresses for slave selection. Default.</summary>
            [TikEnum("layer-2")] Layer2,
            /// <summary>layer-2-and-3 — Uses MAC and IP addresses for slave selection.</summary>
            [TikEnum("layer-2-and-3")] Layer2And3,
            /// <summary>layer-3-and-4 — Uses IP and port numbers for slave selection.</summary>
            [TikEnum("layer-3-and-4")] Layer3And4,
            /// <summary>encap-2-and-3 — For encapsulated traffic, uses inner MAC and IP.</summary>
            [TikEnum("encap-2-and-3")] Encap2And3,
            /// <summary>encap-3-and-4 — For encapsulated traffic, uses inner IP and port.</summary>
            [TikEnum("encap-3-and-4")] Encap3And4,
        }

        /// <summary>transmit-hash-policy — Hash policy for slave selection in balance-xor and 802.3ad modes. Default: layer-2.</summary>
        /// <seealso cref="TransmitHashPolicyMode"/>
        [TikProperty("transmit-hash-policy", DefaultValue = "layer-2")]
        public TransmitHashPolicyMode TransmitHashPolicy { get; set; }

        /// <summary>running — Whether the bonding interface is running (read-only).</summary>
        [TikProperty("running", IsReadOnly = true)]
        public bool Running { get; private set; }

        /// <summary>disabled — Whether the interface is disabled.</summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>comment — Short description of the interface.</summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => Name;
    }
}
