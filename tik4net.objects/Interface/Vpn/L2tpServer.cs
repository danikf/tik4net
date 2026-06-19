using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface.Vpn
{
    /// <summary>
    /// /interface/l2tp-server/server: L2TP server configuration singleton.
    /// L2TP (Layer 2 Tunneling Protocol) is a secure VPN tunneling protocol that supports
    /// both L2TPv2 and L2TPv3. The server accepts incoming L2TP connections from clients and
    /// can optionally enforce IPSec encryption. L2TPv3 additionally supports Ethernet pseudowires.
    /// This is a singleton menu — use <see cref="TikConnectionExtensions.LoadSingle{T}"/> to load it.
    /// </summary>
    [TikEntity("/interface/l2tp-server/server", IsSingleton = true)] // no =detail= — this server singleton rejects it
    public class L2tpServer
    {
        // ---- Enums ----

        /// <summary>Accepted L2TP protocol versions for <see cref="AcceptProtoVersion"/>.</summary>
        public enum AcceptProtoVersionType
        {
            /// <summary>all — accept both L2TPv2 and L2TPv3.</summary>
            [TikEnum("all")] All,
            /// <summary>l2tpv2 — accept only L2TPv2 connections.</summary>
            [TikEnum("l2tpv2")] L2tpv2,
            /// <summary>l2tpv3 — accept only L2TPv3 connections.</summary>
            [TikEnum("l2tpv3")] L2tpv3,
        }

        /// <summary>Accepted pseudowire types for <see cref="AcceptPseudowireType"/>.</summary>
        public enum AcceptPseudowireTypeValue
        {
            /// <summary>all — accept all pseudowire types.</summary>
            [TikEnum("all")] All,
            /// <summary>ether — accept Ethernet pseudowires only.</summary>
            [TikEnum("ether")] Ether,
            /// <summary>ppp — accept PPP pseudowires only.</summary>
            [TikEnum("ppp")] Ppp,
        }

        /// <summary>Caller ID identification method for <see cref="CallerIdType"/>.</summary>
        public enum CallerIdTypeValue
        {
            /// <summary>ip-address — identify caller by source IP address.</summary>
            [TikEnum("ip-address")] IpAddress,
            /// <summary>number — identify caller by tunnel ID number.</summary>
            [TikEnum("number")] Number,
        }

        /// <summary>L2TPv3 cookie length for <see cref="L2tpv3CookieLength"/>.</summary>
        public enum L2tpv3CookieLengthType
        {
            /// <summary>0 — no cookie (disabled).</summary>
            [TikEnum("0")] Zero,
            /// <summary>4-bytes — 4-byte cookie.</summary>
            [TikEnum("4-bytes")] FourBytes,
            /// <summary>8-bytes — 8-byte cookie.</summary>
            [TikEnum("8-bytes")] EightBytes,
        }

        /// <summary>L2TPv3 digest hash algorithm for <see cref="L2tpv3DigestHash"/>.</summary>
        public enum L2tpv3DigestHashType
        {
            /// <summary>md5 — use MD5 hash for L2TPv3 control channel.</summary>
            [TikEnum("md5")] Md5,
            /// <summary>none — no digest hash.</summary>
            [TikEnum("none")] None,
            /// <summary>sha1 — use SHA1 hash for L2TPv3 control channel.</summary>
            [TikEnum("sha1")] Sha1,
        }

        /// <summary>IPSec usage mode for <see cref="UseIpsec"/>.</summary>
        public enum UseIpsecType
        {
            /// <summary>no — IPSec is not used.</summary>
            [TikEnum("no")] No,
            /// <summary>yes — IPSec is offered but not required.</summary>
            [TikEnum("yes")] Yes,
            /// <summary>require — only IPSec-wrapped connections are accepted.</summary>
            [TikEnum("require")] Require,
        }

        // ---- Writable properties ----

        /// <summary>
        /// accept-proto-version — which L2TP protocol version(s) the server accepts.
        /// Default: all
        /// <seealso cref="AcceptProtoVersionType"/>
        /// </summary>
        [TikProperty("accept-proto-version", DefaultValue = "all")]
        public AcceptProtoVersionType AcceptProtoVersion { get; set; }

        /// <summary>
        /// accept-pseudowire-type — pseudowire signaling type(s) the server accepts.
        /// Default: all
        /// <seealso cref="AcceptPseudowireTypeValue"/>
        /// </summary>
        [TikProperty("accept-pseudowire-type", DefaultValue = "all")]
        public AcceptPseudowireTypeValue AcceptPseudowireType { get; set; }

        /// <summary>
        /// allow-fast-path — when <c>true</c>, enables kernel fast-path packet forwarding bypass.
        /// Default: no
        /// </summary>
        [TikProperty("allow-fast-path", DefaultValue = "no")]
        public bool AllowFastPath { get; set; }

        /// <summary>
        /// authentication — comma-separated list of authentication protocols the server accepts.
        /// Valid values: pap, chap, mschap1, mschap2.
        /// Default: mschap1,mschap2
        /// </summary>
        [TikProperty("authentication", DefaultValue = "mschap1,mschap2")]
        public string Authentication { get; set; }

        /// <summary>
        /// caller-id-type — how connected clients are identified when multiple share the same source IP.
        /// Default: ip-address
        /// <seealso cref="CallerIdTypeValue"/>
        /// </summary>
        [TikProperty("caller-id-type", DefaultValue = "ip-address")]
        public CallerIdTypeValue CallerIdType { get; set; }

        /// <summary>
        /// default-profile — PPP profile applied to new L2TP sessions.
        /// Default: default-encryption
        /// </summary>
        [TikProperty("default-profile", DefaultValue = "default-encryption")]
        public string DefaultProfile { get; set; }

        /// <summary>
        /// enabled — when <c>true</c> the L2TP server accepts incoming connections.
        /// Default: no
        /// </summary>
        [TikProperty("enabled", DefaultValue = "no")]
        public bool Enabled { get; set; }

        /// <summary>
        /// ipsec-secret — pre-shared key used for IPSec encryption when <see cref="UseIpsec"/> is enabled.
        /// Leave empty to disable PSK-based IPSec.
        /// </summary>
        [TikProperty("ipsec-secret")]
        public string IpsecSecret { get; set; }

        /// <summary>
        /// keepalive-timeout — inactivity timeout in seconds before a client is considered disconnected.
        /// Default: 30; router default 30, omitted on add when left 0.
        /// </summary>
        // router default 30; omitted on add when left 0
        [TikProperty("keepalive-timeout")]
        public int KeepaliveTimeout { get; set; }

        /// <summary>
        /// l2tpv3-circuit-id — virtual circuit identifier sent in L2TPv3 control channel AVPs.
        /// </summary>
        [TikProperty("l2tpv3-circuit-id")]
        public string L2tpv3CircuitId { get; set; }

        /// <summary>
        /// l2tpv3-cookie-length — session cookie size for L2TPv3 pseudowires.
        /// Default: 0 (no cookie)
        /// <seealso cref="L2tpv3CookieLengthType"/>
        /// </summary>
        [TikProperty("l2tpv3-cookie-length", DefaultValue = "0")]
        public L2tpv3CookieLengthType L2tpv3CookieLength { get; set; }

        /// <summary>
        /// l2tpv3-digest-hash — hash algorithm used for L2TPv3 control channel message authentication.
        /// Default: md5
        /// <seealso cref="L2tpv3DigestHashType"/>
        /// </summary>
        [TikProperty("l2tpv3-digest-hash", DefaultValue = "md5")]
        public L2tpv3DigestHashType L2tpv3DigestHash { get; set; }

        /// <summary>
        /// l2tpv3-ether-interface-list — interface list whose members are bridged via L2TPv3 Ethernet pseudowires.
        /// </summary>
        [TikProperty("l2tpv3-ether-interface-list")]
        public string L2tpv3EtherInterfaceList { get; set; }

        /// <summary>
        /// max-mru — maximum receive unit for L2TP tunnel interfaces in bytes.
        /// Default: 1450; router default 1450, omitted on add when left 0.
        /// </summary>
        // router default 1450; omitted on add when left 0
        [TikProperty("max-mru")]
        public int MaxMru { get; set; }

        /// <summary>
        /// max-mtu — maximum transmit unit for L2TP tunnel interfaces in bytes.
        /// Default: 1450; router default 1450, omitted on add when left 0.
        /// </summary>
        // router default 1450; omitted on add when left 0
        [TikProperty("max-mtu")]
        public int MaxMtu { get; set; }

        /// <summary>
        /// max-sessions — maximum number of concurrent L2TP sessions.
        /// Accepts an integer limit or <c>unlimited</c>.
        /// Default: unlimited
        /// </summary>
        [TikProperty("max-sessions", DefaultValue = "unlimited")]
        public string/*integer or "unlimited"*/ MaxSessions { get; set; }

        /// <summary>
        /// mrru — maximum reconstructed receive unit across multi-link PPP tunnel links, in bytes.
        /// Set to <c>disabled</c> to turn off MRRU negotiation.
        /// Default: disabled
        /// </summary>
        [TikProperty("mrru", DefaultValue = "disabled")]
        public string/*integer or "disabled"*/ Mrru { get; set; }

        /// <summary>
        /// one-session-per-host — when <c>true</c>, each remote host is limited to a single active L2TP session.
        /// Default: no
        /// </summary>
        [TikProperty("one-session-per-host", DefaultValue = "no")]
        public bool OneSessionPerHost { get; set; }

        /// <summary>
        /// use-ipsec — controls IPSec encapsulation for L2TP tunnels.
        /// <c>no</c> — no IPSec; <c>yes</c> — offer IPSec; <c>require</c> — only IPSec connections accepted.
        /// Default: no
        /// <seealso cref="UseIpsecType"/>
        /// </summary>
        [TikProperty("use-ipsec", DefaultValue = "no")]
        public UseIpsecType UseIpsec { get; set; }

        /// <summary>Human-readable summary of the L2TP server configuration.</summary>
        public override string ToString() => string.Format("l2tp-server enabled={0} use-ipsec={1} max-sessions={2}", Enabled, UseIpsec, MaxSessions);
    }
}
