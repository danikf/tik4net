using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.CapsMan
{
    /// <summary>
    /// /caps-man/datapath
    ///
    /// CAPsMAN datapath profile (legacy CAPsMAN, RouterOS 6.x).  A datapath profile controls
    /// how client traffic is forwarded — either locally on the CAP itself (local-forwarding) or
    /// centrally via the CAPsMAN controller (manager forwarding) — and configures bridge
    /// membership, VLAN tagging, client-to-client forwarding, MTU, and ARP behaviour for the
    /// virtual wireless interface created on each provisioned CAP.
    ///
    /// Profiles are referenced by name from /caps-man/configuration (datapath field) or can be
    /// specified inline using dotted notation (e.g. datapath.bridge, datapath.local-forwarding).
    /// </summary>
    [TikEntity("/caps-man/datapath", IncludeDetails = true)]
    public class CapsManDatapath
    {
        // ── ARP mode ──────────────────────────────────────────────────────────

        /// <summary>ARP mode values for the <see cref="Arp"/> property.</summary>
        /// <seealso cref="Arp"/>
        public enum ArpMode
        {
            /// <summary>enabled — the interface will use ARP (default).</summary>
            [TikEnum("enabled")] Enabled,
            /// <summary>disabled — the interface will not use ARP.</summary>
            [TikEnum("disabled")] Disabled,
            /// <summary>local-proxy-arp — the interface will use the local proxy ARP feature.</summary>
            [TikEnum("local-proxy-arp")] LocalProxyArp,
            /// <summary>proxy-arp — the interface will use the ARP proxy feature.</summary>
            [TikEnum("proxy-arp")] ProxyArp,
            /// <summary>reply-only — the interface replies only to ARP requests that match static entries in /ip arp; no dynamic entries are created.</summary>
            [TikEnum("reply-only")] ReplyOnly,
        }

        // ── VLAN mode ─────────────────────────────────────────────────────────

        /// <summary>VLAN tagging mode values for the <see cref="VlanMode"/> property.</summary>
        /// <seealso cref="VlanMode"/>
        public enum VlanModeType
        {
            /// <summary>no-tag — no VLAN tagging (default).</summary>
            [TikEnum("no-tag")] NoTag,
            /// <summary>use-service-tag — tag frames with 802.1ad (QinQ) service tags.</summary>
            [TikEnum("use-service-tag")] UseServiceTag,
            /// <summary>use-tag — tag frames with 802.1q VLAN tags.</summary>
            [TikEnum("use-tag")] UseTag,
        }

        // ── Primary key ───────────────────────────────────────────────────────

        /// <summary>.id — primary key of row.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        // ── Identification ────────────────────────────────────────────────────

        /// <summary>
        /// name — unique name for this datapath profile.
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        // ── Forwarding mode ───────────────────────────────────────────────────

        /// <summary>
        /// local-forwarding — when true, the CAP forwards wireless client traffic locally without
        /// passing it through the CAPsMAN controller (local forwarding mode).
        /// When false (default), all traffic is tunnelled to the controller (manager forwarding).
        /// Default: no.
        /// </summary>
        [TikProperty("local-forwarding", DefaultValue = "no")]
        public bool LocalForwarding { get; set; }

        /// <summary>
        /// client-to-client-forwarding — when true, wireless clients connected to the same
        /// virtual interface may communicate directly with each other.
        /// Default: no.
        /// </summary>
        [TikProperty("client-to-client-forwarding", DefaultValue = "no")]
        public bool ClientToClientForwarding { get; set; }

        // ── Bridge membership ─────────────────────────────────────────────────

        /// <summary>
        /// bridge — bridge interface to which the CAP virtual wireless interface is automatically
        /// added as a bridge port.  Leave empty to skip automatic bridge-port assignment.
        /// </summary>
        [TikProperty("bridge")]
        public string Bridge { get; set; }

        /// <summary>
        /// bridge-cost — spanning-tree port cost assigned to the bridge port.
        /// Valid range 1..200000000; DefaultValue="0" prevents sending 0 on add (0 is out of range).
        /// </summary>
        [TikProperty("bridge-cost", DefaultValue = "0")]
        public int BridgeCost { get; set; }

        /// <summary>
        /// bridge-horizon — bridge horizon value assigned to the port.
        /// Valid range 0..4294967295; DefaultValue="0" makes the mapper skip an unset field on add.
        /// </summary>
        [TikProperty("bridge-horizon", DefaultValue = "0")]
        public int BridgeHorizon { get; set; }

        // ── VLAN ──────────────────────────────────────────────────────────────

        /// <summary>
        /// vlan-mode — controls how the virtual wireless interface tags outgoing frames and
        /// strips/accepts incoming VLAN-tagged frames.
        /// Default: no-tag.
        /// <seealso cref="VlanModeType"/>
        /// </summary>
        [TikProperty("vlan-mode", DefaultValue = "no-tag")]
        public VlanModeType VlanMode { get; set; }

        /// <summary>
        /// vlan-id — VLAN identifier applied when vlan-mode is use-tag or use-service-tag.
        /// Valid range 1..4095; DefaultValue="0" prevents sending 0 on add (0 is out of range).
        /// </summary>
        [TikProperty("vlan-id", DefaultValue = "0")]
        public int VlanId { get; set; }

        // ── Network layer ─────────────────────────────────────────────────────

        /// <summary>
        /// arp — Address Resolution Protocol behaviour on the virtual wireless interface.
        /// Default: enabled.
        /// <seealso cref="ArpMode"/>
        /// </summary>
        [TikProperty("arp", DefaultValue = "enabled")]
        public ArpMode Arp { get; set; }

        /// <summary>
        /// mtu — IP-layer maximum transmission unit for the virtual wireless interface (bytes).
        /// DefaultValue="0" prevents sending 0 on add when the field is not explicitly set.
        /// </summary>
        [TikProperty("mtu", DefaultValue = "0")]
        public int Mtu { get; set; }

        /// <summary>
        /// l2mtu — link-layer maximum transmission unit (bytes).
        /// DefaultValue="0" prevents sending 0 on add when the field is not explicitly set.
        /// </summary>
        [TikProperty("l2mtu", DefaultValue = "0")]
        public int L2Mtu { get; set; }

        // ── Interface list / OpenFlow ─────────────────────────────────────────

        /// <summary>
        /// interface-list — assigns the virtual wireless interface to the named interface list.
        /// </summary>
        [TikProperty("interface-list")]
        public string InterfaceList { get; set; }

        /// <summary>
        /// openflow-switch — name of the OpenFlow switch to which the virtual interface is assigned.
        /// </summary>
        [TikProperty("openflow-switch")]
        public string OpenflowSwitch { get; set; }

        // ── Administrative ────────────────────────────────────────────────────

        /// <summary>
        /// comment — short free-text description of this datapath profile.
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => Name;
    }
}
