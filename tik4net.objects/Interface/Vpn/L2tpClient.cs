using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface.Vpn
{
    /// <summary>
    /// /interface/l2tp-client: L2TP client interface list.
    /// L2TP (Layer 2 Tunneling Protocol) is a tunneling protocol used to support VPNs. Each entry
    /// represents one outbound L2TP client tunnel connecting to a remote L2TP server.
    /// Supports L2TPv2 and L2TPv3 (IP or UDP encapsulation) as of RouterOS 6.40+.
    /// Use <see cref="TikConnectionExtensions.LoadAll{T}"/> / <see cref="TikConnectionExtensions.Save{T}"/>
    /// for CRUD operations.
    /// </summary>
    [TikEntity("/interface/l2tp-client", IncludeDetails = true)]
    public class L2tpClient
    {
        // ---- Enums ----

        /// <summary>Permitted authentication methods for <see cref="Allow"/>.</summary>
        public enum AuthMethod
        {
            /// <summary>pap — Password Authentication Protocol (plaintext, weakest).</summary>
            [TikEnum("pap")] Pap,
            /// <summary>chap — Challenge Handshake Authentication Protocol.</summary>
            [TikEnum("chap")] Chap,
            /// <summary>mschap1 — Microsoft CHAP version 1.</summary>
            [TikEnum("mschap1")] Mschap1,
            /// <summary>mschap2 — Microsoft CHAP version 2 (recommended).</summary>
            [TikEnum("mschap2")] Mschap2,
        }

        /// <summary>L2TP protocol version for <see cref="L2tpProtoVersion"/>.</summary>
        public enum L2tpProtoVersionType
        {
            /// <summary>l2tpv2 — standard L2TPv2 over UDP.</summary>
            [TikEnum("l2tpv2")] L2tpv2,
            /// <summary>l2tpv3-ip — L2TPv3 encapsulated directly in IP.</summary>
            [TikEnum("l2tpv3-ip")] L2tpv3Ip,
            /// <summary>l2tpv3-udp — L2TPv3 over UDP.</summary>
            [TikEnum("l2tpv3-udp")] L2tpv3Udp,
        }

        /// <summary>Cookie length for L2TPv3 pseudowire sessions; see <see cref="L2tpv3CookieLength"/>.</summary>
        public enum L2tpv3CookieLengthType
        {
            /// <summary>0 — no cookie (default).</summary>
            [TikEnum("0")] Zero,
            /// <summary>4-bytes — 4-byte cookie.</summary>
            [TikEnum("4-bytes")] FourBytes,
            /// <summary>8-bytes — 8-byte cookie.</summary>
            [TikEnum("8-bytes")] EightBytes,
        }

        /// <summary>Digest hash algorithm for L2TPv3; see <see cref="L2tpv3DigestHash"/>.</summary>
        public enum L2tpv3DigestHashType
        {
            /// <summary>md5 — MD5 hash (default).</summary>
            [TikEnum("md5")] Md5,
            /// <summary>sha1 — SHA-1 hash.</summary>
            [TikEnum("sha1")] Sha1,
            /// <summary>none — no digest hash.</summary>
            [TikEnum("none")] None,
        }

        // ---- Primary key ----

        /// <summary>.id — primary key of row.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        // ---- Writable properties ----

        /// <summary>
        /// name — unique interface name (mandatory).
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// connect-to — IP or IPv6 address of the remote L2TP server to connect to.
        /// </summary>
        [TikProperty("connect-to")]
        public string/*IP|IPv6*/ ConnectTo { get; set; }

        /// <summary>
        /// disabled — when <c>true</c> the interface will not initiate connections.
        /// Default: yes (disabled on creation).
        /// </summary>
        [TikProperty("disabled", DefaultValue = "yes")]
        public bool Disabled { get; set; }

        /// <summary>
        /// user — username sent during authentication.
        /// </summary>
        [TikProperty("user")]
        public string User { get; set; }

        /// <summary>
        /// password — password sent during authentication.
        /// </summary>
        [TikProperty("password")]
        public string Password { get; set; }

        /// <summary>
        /// profile — PPP profile applied when the tunnel is established.
        /// Default: default-encryption
        /// </summary>
        [TikProperty("profile", DefaultValue = "default-encryption")]
        public string Profile { get; set; }

        /// <summary>
        /// allow — comma-separated list of permitted authentication methods.
        /// Default: mschap2,mschap1,chap,pap (all methods allowed).
        /// Note: the router stores and returns this as a comma-joined string; map as string.
        /// </summary>
        [TikProperty("allow", DefaultValue = "mschap2,mschap1,chap,pap")]
        public string Allow { get; set; }

        /// <summary>
        /// add-default-route — whether to add the L2TP remote address as a default route.
        /// Default: no
        /// </summary>
        [TikProperty("add-default-route", DefaultValue = "no")]
        public bool AddDefaultRoute { get; set; }

        /// <summary>
        /// default-route-distance — distance (administrative distance) applied to the auto-created
        /// default route when <see cref="AddDefaultRoute"/> is enabled. Available since RouterOS 6.2.
        /// </summary>
        // router default 1; omitted on add when left 0
        [TikProperty("default-route-distance")]
        public int DefaultRouteDistance { get; set; }

        /// <summary>
        /// dial-on-demand — when <c>true</c> the tunnel connects only when outbound traffic is generated.
        /// Default: no
        /// </summary>
        [TikProperty("dial-on-demand", DefaultValue = "no")]
        public bool DialOnDemand { get; set; }

        /// <summary>
        /// keepalive-timeout — tunnel keepalive timeout in seconds; if the remote end does not respond
        /// within this interval the tunnel is torn down. Range: 1–4294967295 seconds.
        /// Default: 60; router default 60, omitted on add when left 0.
        /// </summary>
        // router default 60; omitted on add when left 0
        [TikProperty("keepalive-timeout")]
        public int KeepaliveTimeout { get; set; }

        /// <summary>
        /// max-mru — maximum receive unit (bytes) advertised to the peer; packets larger than this
        /// are fragmented. Default: 1450.
        /// </summary>
        // router default 1450; omitted on add when left 0
        [TikProperty("max-mru")]
        public int MaxMru { get; set; }

        /// <summary>
        /// max-mtu — maximum transmit unit (bytes) for the tunnel interface. Default: 1450.
        /// </summary>
        // router default 1450; omitted on add when left 0
        [TikProperty("max-mtu")]
        public int MaxMtu { get; set; }

        /// <summary>
        /// mrru — maximum received reconstructed unit (bytes); enables multilink PPP when set to a
        /// non-zero value. Wire value <c>disabled</c> disables MLPPP.
        /// Default: disabled
        /// </summary>
        [TikProperty("mrru", DefaultValue = "disabled")]
        public string Mrru { get; set; }

        /// <summary>
        /// use-ipsec — enable dynamic IPsec peer configuration for the L2TP tunnel.
        /// Default: no
        /// </summary>
        [TikProperty("use-ipsec", DefaultValue = "no")]
        public bool UseIpsec { get; set; }

        /// <summary>
        /// ipsec-secret — pre-shared key used when <see cref="UseIpsec"/> is enabled.
        /// </summary>
        [TikProperty("ipsec-secret")]
        public string IpsecSecret { get; set; }

        /// <summary>
        /// allow-fast-path — when <c>true</c> packets are forwarded by the fast-path engine without
        /// full kernel processing (may bypass some firewall rules).
        /// Default: no
        /// </summary>
        [TikProperty("allow-fast-path", DefaultValue = "no")]
        public bool AllowFastPath { get; set; }

        /// <summary>
        /// use-peer-dns — whether to use DNS servers advertised by the remote peer.
        /// Valid values: no, yes, exclusively. Stored as string to accommodate "exclusively".
        /// Default: no
        /// </summary>
        [TikProperty("use-peer-dns", DefaultValue = "no")]
        public string UsePeerDns { get; set; }

        /// <summary>
        /// random-source-port — when <c>true</c> a random UDP source port is used for outbound L2TP
        /// packets (improves load-balancing over ECMP links).
        /// Default: no
        /// </summary>
        [TikProperty("random-source-port", DefaultValue = "no")]
        public bool RandomSourcePort { get; set; }

        /// <summary>
        /// src-address — source IP address bound for outgoing L2TP packets; leave empty to use the
        /// routing-table-selected address.
        /// </summary>
        [TikProperty("src-address")]
        public string/*IP*/ SrcAddress { get; set; }

        /// <summary>
        /// l2tp-proto-version — L2TP protocol version and encapsulation to use.
        /// Default: l2tpv2
        /// </summary>
        /// <seealso cref="L2tpProtoVersionType"/>
        [TikProperty("l2tp-proto-version", DefaultValue = "l2tpv2")]
        public L2tpProtoVersionType L2tpProtoVersion { get; set; }

        /// <summary>
        /// l2tpv3-circuit-id — virtual circuit identifier string sent in L2TPv3 AVPs.
        /// Only relevant when <see cref="L2tpProtoVersion"/> is <c>l2tpv3-ip</c> or <c>l2tpv3-udp</c>.
        /// </summary>
        [TikProperty("l2tpv3-circuit-id")]
        public string L2tpv3CircuitId { get; set; }

        /// <summary>
        /// l2tpv3-cookie-length — L2TPv3 pseudowire session cookie length.
        /// Default: 0 (no cookie)
        /// </summary>
        /// <seealso cref="L2tpv3CookieLengthType"/>
        [TikProperty("l2tpv3-cookie-length", DefaultValue = "0")]
        public L2tpv3CookieLengthType L2tpv3CookieLength { get; set; }

        /// <summary>
        /// l2tpv3-digest-hash — hash algorithm used for L2TPv3 message digest.
        /// Default: md5
        /// </summary>
        /// <seealso cref="L2tpv3DigestHashType"/>
        [TikProperty("l2tpv3-digest-hash", DefaultValue = "md5")]
        public L2tpv3DigestHashType L2tpv3DigestHash { get; set; }

        /// <summary>comment — optional free-text description of this L2TP client interface.</summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        // ---- Read-only properties ----

        /// <summary>
        /// running — <c>true</c> when the tunnel is currently established and passing traffic.
        /// </summary>
        [TikProperty("running", IsReadOnly = true)]
        public bool Running { get; private set; }

        /// <summary>Human-readable identity of the L2TP client interface.</summary>
        public override string ToString() => string.Format("{0} -> {1} (proto={2} disabled={3})", Name, ConnectTo, L2tpProtoVersion, Disabled);
    }
}
