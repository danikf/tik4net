namespace tik4net.Objects.Interface.Wifi
{
    /// <summary>
    /// /interface/wifi/datapath
    ///
    /// Datapath settings control data-forwarding related aspects of a WiFi interface (ROS 7 wifi package).
    /// On CAPsMAN, datapath settings are configured in this profile menu and can be referenced
    /// by name from /interface/wifi/configuration or directly from an /interface/wifi entry.
    ///
    /// A datapath profile controls how client traffic is bridged, tagged with a VLAN, isolated
    /// from other clients, and whether it is processed on the CAP or forwarded to CAPsMAN.
    /// </summary>
    [TikEntity("/interface/wifi/datapath", IncludeDetails = true)]
    public class WifiDatapath
    {
        // ── Traffic-processing ────────────────────────────────────────────────

        /// <summary>Traffic-processing location values for the <see cref="TrafficProcessing"/> property.</summary>
        /// <seealso cref="TrafficProcessing"/>
        public enum TrafficProcessingMode
        {
            /// <summary>on-cap — process WiFi traffic on the CAP itself (default).</summary>
            [TikEnum("on-cap")] OnCap,
            /// <summary>on-capsman — forward traffic to CAPsMAN for processing.</summary>
            [TikEnum("on-capsman")] OnCapsman,
            /// <summary>on-capsman-secure — forward traffic to CAPsMAN over an encrypted tunnel.</summary>
            [TikEnum("on-capsman-secure")] OnCapsmanSecure,
        }

        // ── Primary key ───────────────────────────────────────────────────────

        /// <summary>.id — primary key of row.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        // ── Identification ────────────────────────────────────────────────────

        /// <summary>
        /// name — unique name for this datapath profile.
        /// WinBox: "Name"
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        // ── Bridge settings ───────────────────────────────────────────────────

        /// <summary>
        /// bridge — bridge interface to add the WiFi interface to as a bridge port.
        /// Virtual (slave) interfaces are by default added to the same bridge, if any,
        /// as the corresponding master interface.
        /// WinBox: "Bridge"
        /// </summary>
        [TikProperty("bridge")]
        public string Bridge { get; set; }

        /// <summary>
        /// bridge-cost — STP path cost assigned when adding the interface as a bridge port (1..65535).
        /// Default: 10.  DefaultValue="0" so a freshly-constructed entity (CLR int = 0)
        /// does not send 0 to the router (which would be out of range).
        /// WinBox: "Bridge Cost"
        /// </summary>
        [TikProperty("bridge-cost", DefaultValue = "0")]
        public int BridgeCost { get; set; }

        /// <summary>
        /// bridge-horizon — bridge horizon for split-horizon bridging; "none" disables it.
        /// Accepts an integer or the literal string "none".
        /// Default: none.
        /// WinBox: "Bridge Horizon"
        /// </summary>
        [TikProperty("bridge-horizon", DefaultValue = "none")]
        public string/*integer|none*/ BridgeHorizon { get; set; }

        // ── Client settings ───────────────────────────────────────────────────

        /// <summary>
        /// client-isolation — when true, connected wireless clients are isolated from each other
        /// and cannot communicate directly through this AP.
        /// Default: no.
        /// WinBox: "Client Isolation"
        /// </summary>
        [TikProperty("client-isolation", DefaultValue = "no")]
        public bool ClientIsolation { get; set; }

        // ── Interface / VLAN ──────────────────────────────────────────────────

        /// <summary>
        /// interface-list — name of an interface list this datapath interface is a member of.
        /// WinBox: "Interface List"
        /// </summary>
        [TikProperty("interface-list")]
        public string InterfaceList { get; set; }

        /// <summary>
        /// vlan-id — default VLAN ID (1..4095) to assign to clients connecting to this AP,
        /// or "none" to disable VLAN tagging.
        /// Default: none.
        /// WinBox: "VLAN ID"
        /// </summary>
        [TikProperty("vlan-id", DefaultValue = "none")]
        public string/*1..4095|none*/ VlanId { get; set; }

        // ── OpenFlow ──────────────────────────────────────────────────────────

        /// <summary>
        /// openflow-switch — name of the OpenFlow switch this interface should be added to.
        /// Leave empty to not use OpenFlow.
        /// WinBox: "OpenFlow Switch"
        /// </summary>
        [TikProperty("openflow-switch")]
        public string OpenflowSwitch { get; set; }

        // ── CAPsMAN ───────────────────────────────────────────────────────────

        /// <summary>
        /// traffic-processing — determines where WiFi traffic is processed:
        /// on the CAP itself, or forwarded to CAPsMAN (optionally encrypted).
        /// Default: on-cap.
        /// <seealso cref="TrafficProcessingMode"/>
        /// WinBox: "Traffic Processing"
        /// </summary>
        [TikProperty("traffic-processing", DefaultValue = "on-cap")]
        public TrafficProcessingMode TrafficProcessing { get; set; }

        // ── Administrative ────────────────────────────────────────────────────

        /// <summary>
        /// disabled — when true this datapath profile is administratively disabled.
        /// Default: no.
        /// WinBox: "Disabled"
        /// </summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>
        /// comment — short free-text description.
        /// WinBox: "Comment"
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => Name;
    }
}
