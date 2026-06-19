using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.CapsMan
{
    /// <summary>
    /// /caps-man/interface: Managed CAP radio interfaces on the CAPsMAN controller (legacy CAPsMAN,
    /// RouterOS 6.x). Each entry represents one wireless radio interface bound from a CAP device.
    /// Most entries are dynamic (created automatically when a CAP connects) and are effectively
    /// read-only; manual/master entries can be pre-created and have configuration assigned.
    /// Flags: M=master, D=dynamic, B=bound, X=disabled, I=inactive, R=running.
    /// </summary>
    [TikEntity("/caps-man/interface", IncludeDetails = true)]
    public class CapsManInterface
    {
        // ── ARP mode ──────────────────────────────────────────────────────────

        /// <summary>ARP mode values for the <see cref="Arp"/> property.</summary>
        /// <seealso cref="ArpMode"/>
        public enum ArpMode
        {
            /// <summary>enabled — ARP is enabled (default).</summary>
            [TikEnum("enabled")] Enabled,
            /// <summary>disabled — interface will not use ARP.</summary>
            [TikEnum("disabled")] Disabled,
            /// <summary>local-proxy-arp — router performs ARP on behalf of clients on different subnets.</summary>
            [TikEnum("local-proxy-arp")] LocalProxyArp,
            /// <summary>proxy-arp — router answers ARP requests for addresses it knows how to reach.</summary>
            [TikEnum("proxy-arp")] ProxyArp,
            /// <summary>reply-only — interface only sends ARP replies, never sends requests.</summary>
            [TikEnum("reply-only")] ReplyOnly,
        }

        // ── Primary key ───────────────────────────────────────────────────────

        /// <summary>.id — primary key of row.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        // ── Identification ────────────────────────────────────────────────────

        /// <summary>
        /// name — unique name of this CAPsMAN interface.
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// mac-address — MAC address of the virtual wireless interface.
        /// </summary>
        [TikProperty("mac-address")]
        public string/*MAC*/ MacAddress { get; set; }

        /// <summary>
        /// radio-mac — MAC address of the physical CAP radio that bound to this interface entry.
        /// Used to match a specific CAP radio device.
        /// </summary>
        [TikProperty("radio-mac")]
        public string/*MAC*/ RadioMac { get; set; }

        /// <summary>
        /// radio-name — identifier/name reported by the CAP device for this radio.
        /// </summary>
        [TikProperty("radio-name")]
        public string RadioName { get; set; }

        // ── Configuration references ──────────────────────────────────────────

        /// <summary>
        /// master-interface — name of the master CAPsMAN interface this entry is subordinate to,
        /// or "none" for a master interface itself. Default: none.
        /// </summary>
        [TikProperty("master-interface", DefaultValue = "none")]
        public string MasterInterface { get; set; }

        /// <summary>
        /// configuration — name of the /caps-man/configuration profile to apply to this interface.
        /// </summary>
        [TikProperty("configuration")]
        public string Configuration { get; set; }

        /// <summary>
        /// channel — name of the /caps-man/channel profile to apply, or empty for no channel override.
        /// </summary>
        [TikProperty("channel")]
        public string Channel { get; set; }

        /// <summary>
        /// datapath — name of the /caps-man/datapath profile to apply, or empty for no datapath override.
        /// </summary>
        [TikProperty("datapath")]
        public string Datapath { get; set; }

        /// <summary>
        /// security — name of the /caps-man/security profile to apply, or empty for no security override.
        /// </summary>
        [TikProperty("security")]
        public string Security { get; set; }

        /// <summary>
        /// rates — name of the /caps-man/rates profile to apply, or empty for no rates override.
        /// </summary>
        [TikProperty("rates")]
        public string Rates { get; set; }

        // ── Inline rates overrides (rates.*) ──────────────────────────────────

        /// <summary>
        /// rates.basic — comma-separated list of mandatory data rates (e.g. "1Mbps,2Mbps,5.5Mbps,11Mbps").
        /// </summary>
        [TikProperty("rates.basic")]
        public string RatesBasic { get; set; }

        /// <summary>
        /// rates.supported — comma-separated list of optional advertised data rates.
        /// </summary>
        [TikProperty("rates.supported")]
        public string RatesSupported { get; set; }

        /// <summary>
        /// rates.ht-basic-mcs — required 802.11n MCS indices (e.g. "mcs-0,mcs-1,...,mcs-7").
        /// </summary>
        [TikProperty("rates.ht-basic-mcs")]
        public string RatesHtBasicMcs { get; set; }

        /// <summary>
        /// rates.ht-supported-mcs — advertised 802.11n MCS indices.
        /// </summary>
        [TikProperty("rates.ht-supported-mcs")]
        public string RatesHtSupportedMcs { get; set; }

        /// <summary>
        /// rates.vht-basic-mcs — required 802.11ac MCS set per spatial stream.
        /// </summary>
        [TikProperty("rates.vht-basic-mcs")]
        public string RatesVhtBasicMcs { get; set; }

        /// <summary>
        /// rates.vht-supported-mcs — advertised 802.11ac MCS set per spatial stream.
        /// </summary>
        [TikProperty("rates.vht-supported-mcs")]
        public string RatesVhtSupportedMcs { get; set; }

        // ── Network parameters ────────────────────────────────────────────────

        /// <summary>
        /// arp — Address Resolution Protocol mode for this interface. Default: enabled.
        /// <seealso cref="ArpMode"/>
        /// </summary>
        [TikProperty("arp", DefaultValue = "enabled")]
        public ArpMode Arp { get; set; }

        /// <summary>
        /// arp-timeout — timeout for ARP cache entries. Default: auto.
        /// </summary>
        [TikProperty("arp-timeout", DefaultValue = "auto")]
        public string/*time*/ ArpTimeout { get; set; }

        /// <summary>
        /// mtu — IP layer maximum transmission unit in bytes.
        /// DefaultValue="0" prevents sending 0 on add when unset.
        /// </summary>
        [TikProperty("mtu", DefaultValue = "0")]
        public int Mtu { get; set; }

        /// <summary>
        /// l2mtu — link-layer maximum transmission unit in bytes.
        /// DefaultValue="0" prevents sending 0 on add when unset.
        /// </summary>
        [TikProperty("l2mtu", DefaultValue = "0")]
        public int L2Mtu { get; set; }

        // ── Administrative ────────────────────────────────────────────────────

        /// <summary>
        /// disable-running-check — when yes, the interface is always considered running even if
        /// no CAP is connected. Default: no.
        /// </summary>
        [TikProperty("disable-running-check", DefaultValue = "no")]
        public bool DisableRunningCheck { get; set; }

        /// <summary>
        /// disabled — whether the interface is administratively disabled (X flag).
        /// </summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>
        /// comment — short free-text description of this interface entry.
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        // ── Read-only status fields ───────────────────────────────────────────
        // These are set by the router when a CAP binds to this interface entry.
        // The table is usually empty when no CAPs are connected.

        /// <summary>
        /// running — whether the interface is currently running/operational (R flag). Read-only.
        /// </summary>
        [TikProperty("running", IsReadOnly = true)]
        public bool Running { get; private set; }

        /// <summary>
        /// master — whether this is a master interface (M flag). Read-only.
        /// </summary>
        [TikProperty("master", IsReadOnly = true)]
        public bool Master { get; private set; }

        /// <summary>
        /// dynamic — whether this interface entry was created dynamically by a CAP connection (D flag). Read-only.
        /// </summary>
        [TikProperty("dynamic", IsReadOnly = true)]
        public bool Dynamic { get; private set; }

        /// <summary>
        /// bound — whether the interface is bound to a physical CAP radio (B flag). Read-only.
        /// </summary>
        [TikProperty("bound", IsReadOnly = true)]
        public bool Bound { get; private set; }

        /// <summary>
        /// inactive — whether the interface is inactive (I flag). Read-only.
        /// </summary>
        [TikProperty("inactive", IsReadOnly = true)]
        public bool Inactive { get; private set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => Name;
    }
}
