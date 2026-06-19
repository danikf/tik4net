using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface.Vpn
{
    /// <summary>
    /// /interface/ovpn-server/server: OpenVPN server configuration singleton.
    /// OpenVPN implements OSI layer 2 or 3 secure network extensions using the SSL/TLS protocol,
    /// supporting IPv4 and IPv6. The server listens on a single TCP or UDP port and establishes
    /// SSL/TLS tunnels with authenticated OpenVPN clients.
    /// This is a singleton menu — use <see cref="TikConnectionExtensions.LoadSingle{T}"/> to load it.
    /// </summary>
    [TikEntity("/interface/ovpn-server/server", IsSingleton = true)] // no =detail= — print returns !empty on this RouterOS, see OvpnServerTest
    public class OvpnServer
    {
        // ---- Enums ----

        /// <summary>Tunnel mode for <see cref="Mode"/>.</summary>
        public enum TunnelMode
        {
            /// <summary>ip — Layer 3 (tun) IP tunneling mode.</summary>
            [TikEnum("ip")] Ip,
            /// <summary>ethernet — Layer 2 (tap) Ethernet tunneling mode.</summary>
            [TikEnum("ethernet")] Ethernet,
        }

        /// <summary>Transport protocol for <see cref="Protocol"/>.</summary>
        public enum ProtocolType
        {
            /// <summary>tcp — use TCP transport.</summary>
            [TikEnum("tcp")] Tcp,
            /// <summary>udp — use UDP transport.</summary>
            [TikEnum("udp")] Udp,
        }

        /// <summary>Gateway redirect mode for <see cref="RedirectGateway"/>.</summary>
        public enum RedirectGatewayMode
        {
            /// <summary>disabled — no gateway redirect pushed to clients.</summary>
            [TikEnum("disabled")] Disabled,
            /// <summary>def1 — push default route via OpenVPN tunnel using def1 method.</summary>
            [TikEnum("def1")] Def1,
            /// <summary>ipv6 — push IPv6 default route via OpenVPN tunnel.</summary>
            [TikEnum("ipv6")] Ipv6,
        }

        /// <summary>Permitted TLS protocol versions for <see cref="TlsVersion"/>.</summary>
        public enum TlsVersionType
        {
            /// <summary>any — allow any supported TLS version.</summary>
            [TikEnum("any")] Any,
            /// <summary>only-1.2 — restrict to TLS 1.2 only.</summary>
            [TikEnum("only-1.2")] Only12,
        }

        /// <summary>Challenge authentication method for <see cref="UserAuthMethod"/>.</summary>
        public enum UserAuthMethodType
        {
            /// <summary>pap — Password Authentication Protocol.</summary>
            [TikEnum("pap")] Pap,
            /// <summary>mschap2 — Microsoft Challenge-Handshake Authentication Protocol v2.</summary>
            [TikEnum("mschap2")] Mschap2,
        }

        // ---- Writable properties ----

        /// <summary>name — server interface name identifier.</summary>
        [TikProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// auth — comma-separated list of authentication (HMAC) algorithms the server will accept.
        /// Valid values: md5, sha1, sha256, sha512, null.
        /// Default: sha1,md5,sha256,sha512
        /// </summary>
        [TikProperty("auth", DefaultValue = "sha1,md5,sha256,sha512")]
        public string Auth { get; set; }

        /// <summary>
        /// certificate — name of the TLS certificate the server uses; <c>none</c> disables certificate-based auth.
        /// Default: none
        /// </summary>
        [TikProperty("certificate", DefaultValue = "none")]
        public string Certificate { get; set; }

        /// <summary>
        /// cipher — comma-separated list of permitted data-channel encryption algorithms.
        /// Valid values: null, aes128-cbc, aes128-gcm, aes192-cbc, aes192-gcm, aes256-cbc, aes256-gcm, blowfish128.
        /// Default: aes128-cbc,blowfish128
        /// </summary>
        [TikProperty("cipher", DefaultValue = "aes128-cbc,blowfish128")]
        public string Cipher { get; set; }

        /// <summary>
        /// default-profile — default PPP profile applied when a client connects.
        /// Default: default
        /// </summary>
        [TikProperty("default-profile", DefaultValue = "default")]
        public string DefaultProfile { get; set; }

        /// <summary>
        /// disabled — when <c>true</c> the OpenVPN server does not accept connections.
        /// Default: yes (disabled by default on a fresh router)
        /// </summary>
        [TikProperty("disabled", DefaultValue = "yes")]
        public bool Disabled { get; set; }

        /// <summary>
        /// enable-tun-ipv6 — permits IPv6 IP tunneling over the server interface.
        /// Default: no
        /// </summary>
        [TikProperty("enable-tun-ipv6", DefaultValue = "no")]
        public bool EnableTunIpv6 { get; set; }

        /// <summary>
        /// ipv6-prefix-len — prefix length for IPv6 addresses generated on server-side tun interfaces.
        /// Default: 64; router default 64, omitted on add when left 0.
        /// </summary>
        // router default 64; omitted on add when left 0
        [TikProperty("ipv6-prefix-len")]
        public int Ipv6PrefixLen { get; set; }

        /// <summary>
        /// keepalive-timeout — seconds of inactivity before the server starts sending keepalive probes.
        /// Disconnection occurs after 2× this value with no response.
        /// Set to <c>disabled</c> to turn off keepalives (stored as string to accept "disabled").
        /// Default: 60
        /// </summary>
        [TikProperty("keepalive-timeout", DefaultValue = "60")]
        public string/*integer or "disabled"*/ KeepaliveTimeout { get; set; }

        /// <summary>
        /// mac-address — MAC address assigned to the server virtual interface (auto-generated when not set).
        /// </summary>
        [TikProperty("mac-address")]
        public string/*MAC*/ MacAddress { get; set; }

        /// <summary>
        /// max-mtu — maximum transmission unit for the tunnel interface, in bytes.
        /// Default: 1500; router default 1500, omitted on add when left 0.
        /// </summary>
        // router default 1500; omitted on add when left 0
        [TikProperty("max-mtu")]
        public int MaxMtu { get; set; }

        /// <summary>
        /// mode — tunneling layer: <c>ip</c> for Layer 3 (tun), <c>ethernet</c> for Layer 2 (tap).
        /// Default: ip
        /// <seealso cref="TunnelMode"/>
        /// </summary>
        [TikProperty("mode", DefaultValue = "ip")]
        public TunnelMode Mode { get; set; }

        /// <summary>
        /// netmask — subnet prefix length applied to client IP addresses.
        /// Default: 24; router default 24, omitted on add when left 0.
        /// </summary>
        // router default 24; omitted on add when left 0
        [TikProperty("netmask")]
        public int Netmask { get; set; }

        /// <summary>
        /// port — TCP/UDP port the server listens on.
        /// Default: 1194; router default 1194, omitted on add when left 0.
        /// </summary>
        // router default 1194; omitted on add when left 0
        [TikProperty("port")]
        public int Port { get; set; }

        /// <summary>
        /// protocol — transport protocol.
        /// Default: tcp
        /// <seealso cref="ProtocolType"/>
        /// </summary>
        [TikProperty("protocol", DefaultValue = "tcp")]
        public ProtocolType Protocol { get; set; }

        /// <summary>
        /// push-routes — comma-separated list of IPv4 routes pushed to connecting clients.
        /// Maximum 1400 characters (approximately 37 routes).
        /// </summary>
        [TikProperty("push-routes")]
        public string PushRoutes { get; set; }

        /// <summary>
        /// push-routes-ipv6 — comma-separated list of IPv6 routes pushed to connecting clients.
        /// </summary>
        [TikProperty("push-routes-ipv6")]
        public string PushRoutesIpv6 { get; set; }

        /// <summary>
        /// redirect-gateway — controls default-route redirection pushed to clients.
        /// Default: disabled
        /// <seealso cref="RedirectGatewayMode"/>
        /// </summary>
        [TikProperty("redirect-gateway", DefaultValue = "disabled")]
        public RedirectGatewayMode RedirectGateway { get; set; }

        /// <summary>
        /// reneg-sec — data-channel key renegotiation interval in seconds.
        /// Default: 3600; router default 3600, omitted on add when left 0.
        /// </summary>
        // router default 3600; omitted on add when left 0
        [TikProperty("reneg-sec")]
        public int RenegSec { get; set; }

        /// <summary>
        /// require-client-certificate — when <c>true</c>, the server validates the client certificate
        /// chain membership before allowing the connection.
        /// Default: no
        /// </summary>
        [TikProperty("require-client-certificate", DefaultValue = "no")]
        public bool RequireClientCertificate { get; set; }

        /// <summary>
        /// tls-version — permitted TLS protocol version(s).
        /// Default: any
        /// <seealso cref="TlsVersionType"/>
        /// </summary>
        [TikProperty("tls-version", DefaultValue = "any")]
        public TlsVersionType TlsVersion { get; set; }

        /// <summary>
        /// tun-server-ipv6 — IPv6 address prefix assigned to the server-side tun interface.
        /// Default: :: (not set)
        /// </summary>
        [TikProperty("tun-server-ipv6", DefaultValue = "::")]
        public string/*IPv6 prefix*/ TunServerIpv6 { get; set; }

        /// <summary>
        /// user-auth-method — challenge-response authentication protocol used for user authentication.
        /// Default: pap
        /// <seealso cref="UserAuthMethodType"/>
        /// </summary>
        [TikProperty("user-auth-method", DefaultValue = "pap")]
        public UserAuthMethodType UserAuthMethod { get; set; }

        /// <summary>
        /// vrf — Virtual Routing and Forwarding instance the server connections are bound to.
        /// Leave empty to use the main routing table.
        /// </summary>
        [TikProperty("vrf")]
        public string Vrf { get; set; }

        /// <summary>comment — optional description of the server configuration.</summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>Human-readable summary of the OpenVPN server configuration.</summary>
        public override string ToString() => string.Format("ovpn-server port={0} protocol={1} mode={2} disabled={3}", Port, Protocol, Mode, Disabled);
    }
}
