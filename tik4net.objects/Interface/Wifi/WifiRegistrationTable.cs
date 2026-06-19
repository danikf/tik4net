namespace tik4net.Objects.Interface.Wifi
{
    /// <summary>
    /// /interface/wifi/registration-table
    ///
    /// Read-only live monitoring table of wireless clients currently associated with
    /// any WiFi interface (ROS 7 wifi package).  Each row represents one connected peer
    /// and exposes its identity (MAC, SSID, interface), signal quality, data rates,
    /// traffic counters, and authentication state.  The table is populated automatically
    /// by the router; there is no add/set/remove — entries appear and disappear as
    /// clients connect and disconnect.
    /// </summary>
    [TikEntity("/interface/wifi/registration-table", IsReadOnly = true, IncludeDetails = true)]
    public class WifiRegistrationTable
    {
        // ── Primary key ───────────────────────────────────────────────────────

        /// <summary>.id — primary key of row.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        // ── Identification ────────────────────────────────────────────────────

        /// <summary>
        /// interface — name of the WiFi interface through which the peer is associated.
        /// WinBox: "Interface"
        /// </summary>
        [TikProperty("interface", IsReadOnly = true)]
        public string Interface { get; private set; }

        /// <summary>
        /// mac-address — hardware (MAC) address of the associated peer device.
        /// WinBox: "MAC Address"
        /// </summary>
        [TikProperty("mac-address", IsReadOnly = true)]
        public string/*MAC*/ MacAddress { get; private set; }

        /// <summary>
        /// ssid — SSID of the wireless network on which the client is connected.
        /// WinBox: "SSID"
        /// </summary>
        [TikProperty("ssid", IsReadOnly = true)]
        public string Ssid { get; private set; }

        /// <summary>
        /// band — frequency band on which the router communicates with the peer
        /// (e.g. "2ghz-ax", "5ghz-ac").
        /// WinBox: "Band"
        /// </summary>
        [TikProperty("band", IsReadOnly = true)]
        public string Band { get; private set; }

        // ── Authentication ────────────────────────────────────────────────────

        /// <summary>
        /// authorized — true when the peer has successfully completed authentication.
        /// WinBox: "Authorized"
        /// </summary>
        [TikProperty("authorized", IsReadOnly = true)]
        public bool Authorized { get; private set; }

        /// <summary>
        /// auth-type — authentication method used by this client (e.g. "wpa2-psk", "wpa3").
        /// WinBox: "Auth. Type"
        /// </summary>
        [TikProperty("auth-type", IsReadOnly = true)]
        public string AuthType { get; private set; }

        /// <summary>
        /// vlan-id — VLAN assigned by the AP or RADIUS server for this peer's traffic.
        /// WinBox: "VLAN ID"
        /// </summary>
        [TikProperty("vlan-id", IsReadOnly = true)]
        public string/*integer*/ VlanId { get; private set; }

        // ── Timing ────────────────────────────────────────────────────────────

        /// <summary>
        /// uptime — time elapsed since the peer first associated with this interface.
        /// WinBox: "Uptime"
        /// </summary>
        [TikProperty("uptime", IsReadOnly = true)]
        public string/*time*/ Uptime { get; private set; }

        /// <summary>
        /// last-activity — duration since the most recent data transmission or reception
        /// with this peer.
        /// WinBox: "Last Activity"
        /// </summary>
        [TikProperty("last-activity", IsReadOnly = true)]
        public string/*time*/ LastActivity { get; private set; }

        // ── Signal quality ────────────────────────────────────────────────────

        /// <summary>
        /// signal — signal strength received from the peer, in dBm.
        /// WinBox: "Signal"
        /// </summary>
        [TikProperty("signal", IsReadOnly = true)]
        public string/*integer dBm*/ Signal { get; private set; }

        // ── Data rates ────────────────────────────────────────────────────────

        /// <summary>
        /// rx-rate — bitrate string for data received from the peer (e.g. "144Mbps-HT20").
        /// WinBox: "Rx Rate"
        /// </summary>
        [TikProperty("rx-rate", IsReadOnly = true)]
        public string RxRate { get; private set; }

        /// <summary>
        /// tx-rate — bitrate string for data transmitted to the peer (e.g. "144Mbps-HT20").
        /// WinBox: "Tx Rate"
        /// </summary>
        [TikProperty("tx-rate", IsReadOnly = true)]
        public string TxRate { get; private set; }

        /// <summary>
        /// rx-bits-per-second — current incoming data rate from this peer in bits per second.
        /// WinBox: "Rx"
        /// </summary>
        [TikProperty("rx-bits-per-second", IsReadOnly = true)]
        public string/*integer bps*/ RxBitsPerSecond { get; private set; }

        /// <summary>
        /// tx-bits-per-second — current outgoing data rate to this peer in bits per second.
        /// WinBox: "Tx"
        /// </summary>
        [TikProperty("tx-bits-per-second", IsReadOnly = true)]
        public string/*integer bps*/ TxBitsPerSecond { get; private set; }

        // ── Traffic counters ──────────────────────────────────────────────────

        /// <summary>
        /// bytes — comma-separated byte counts: bytes transmitted to the peer and bytes received
        /// from it (e.g. "12345,67890").
        /// WinBox: "Bytes"
        /// </summary>
        [TikProperty("bytes", IsReadOnly = true)]
        public string Bytes { get; private set; }

        /// <summary>
        /// packets — comma-separated packet counts: packets transmitted to the peer and packets
        /// received from it (e.g. "100,200").
        /// WinBox: "Packets"
        /// </summary>
        [TikProperty("packets", IsReadOnly = true)]
        public string Packets { get; private set; }

        /// <summary>Human-readable identity: MAC address and signal strength.</summary>
        public override string ToString() => string.Format("{0} on {1} ({2} dBm)", MacAddress, Interface, Signal);
    }
}
