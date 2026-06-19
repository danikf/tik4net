using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface.Vpn
{
    /// <summary>
    /// /interface/ovpn-client: OpenVPN client interface list.
    /// OpenVPN implements OSI layer 2 or 3 secure network extensions using the SSL/TLS protocol,
    /// supporting IPv4 and IPv6. Each entry represents one OpenVPN client tunnel connecting outbound
    /// to a remote OpenVPN server.
    /// Use <see cref="TikConnectionExtensions.LoadAll{T}"/> / <see cref="TikConnectionExtensions.Save{T}"/>
    /// for CRUD operations.
    /// </summary>
    [TikEntity("/interface/ovpn-client", IncludeDetails = true)]
    public class OvpnClient
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

        /// <summary>Permitted TLS protocol versions for <see cref="TlsVersion"/>.</summary>
        public enum TlsVersionType
        {
            /// <summary>any — allow any supported TLS version.</summary>
            [TikEnum("any")] Any,
            /// <summary>only-1.2 — restrict to TLS 1.2 only.</summary>
            [TikEnum("only-1.2")] Only12,
        }

        // ---- Primary key ----

        /// <summary>.id — primary key of row.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        // ---- Writable properties ----

        /// <summary>
        /// name — unique interface name identifier (mandatory).
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// connect-to — remote IP or IPv6 address of the OpenVPN server to connect to.
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
        /// add-default-route — whether to add the OVPN remote address as a default route.
        /// Default: no
        /// </summary>
        [TikProperty("add-default-route", DefaultValue = "no")]
        public bool AddDefaultRoute { get; set; }

        /// <summary>
        /// auth — permitted HMAC authentication algorithm(s).
        /// Valid values: md5, sha1, sha256, sha512, null.
        /// Default: sha1
        /// </summary>
        [TikProperty("auth", DefaultValue = "sha1")]
        public string Auth { get; set; }

        /// <summary>
        /// certificate — name of the client TLS certificate; <c>none</c> disables certificate-based auth.
        /// Default: none
        /// </summary>
        [TikProperty("certificate", DefaultValue = "none")]
        public string Certificate { get; set; }

        /// <summary>
        /// cipher — data-channel encryption algorithm.
        /// Valid values: null, aes128-cbc, aes128-gcm, aes192-cbc, aes192-gcm, aes256-cbc, aes256-gcm, blowfish128.
        /// Default: blowfish128
        /// </summary>
        [TikProperty("cipher", DefaultValue = "blowfish128")]
        public string Cipher { get; set; }

        /// <summary>
        /// disconnect-notify — send an explicit disconnect notification to the server on tunnel teardown.
        /// Default: no (undocumented; present in RouterOS tab-completion).
        /// </summary>
        [TikProperty("disconnect-notify", DefaultValue = "no")]
        public bool DisconnectNotify { get; set; }

        /// <summary>
        /// mac-address — MAC address assigned to the virtual interface; auto-generated if not specified.
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
        /// </summary>
        /// <seealso cref="TunnelMode"/>
        [TikProperty("mode", DefaultValue = "ip")]
        public TunnelMode Mode { get; set; }

        /// <summary>
        /// password — password used for user authentication. Maximum 1000 characters.
        /// </summary>
        [TikProperty("password")]
        public string Password { get; set; }

        /// <summary>
        /// port — TCP/UDP port of the remote OpenVPN server.
        /// Default: 1194; router default 1194, omitted on add when left 0.
        /// </summary>
        // router default 1194; omitted on add when left 0
        [TikProperty("port")]
        public int Port { get; set; }

        /// <summary>
        /// profile — PPP profile applied when the tunnel is established.
        /// Default: default
        /// </summary>
        [TikProperty("profile", DefaultValue = "default")]
        public string Profile { get; set; }

        /// <summary>
        /// protocol — transport protocol to use when connecting.
        /// Default: tcp
        /// </summary>
        /// <seealso cref="ProtocolType"/>
        [TikProperty("protocol", DefaultValue = "tcp")]
        public ProtocolType Protocol { get; set; }

        /// <summary>
        /// route-nopull — when <c>true</c> the client ignores routes pushed by the server.
        /// Default: no
        /// </summary>
        [TikProperty("route-nopull", DefaultValue = "no")]
        public bool RouteNopull { get; set; }

        /// <summary>
        /// tls-version — permitted TLS protocol version(s).
        /// Default: any
        /// </summary>
        /// <seealso cref="TlsVersionType"/>
        [TikProperty("tls-version", DefaultValue = "any")]
        public TlsVersionType TlsVersion { get; set; }

        /// <summary>
        /// use-peer-dns — whether to add DNS servers advertised by the OVPN server.
        /// Default: no
        /// </summary>
        [TikProperty("use-peer-dns", DefaultValue = "no")]
        public bool UsePeerDns { get; set; }

        /// <summary>
        /// user — username used for authentication.
        /// </summary>
        [TikProperty("user")]
        public string User { get; set; }

        /// <summary>
        /// verify-server-certificate — when <c>true</c> the client validates the server certificate
        /// CN or SAN against the <see cref="ConnectTo"/> address.
        /// Default: no
        /// </summary>
        [TikProperty("verify-server-certificate", DefaultValue = "no")]
        public bool VerifyServerCertificate { get; set; }

        /// <summary>comment — optional description of the client interface entry.</summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        // ---- Read-only properties ----

        /// <summary>
        /// running — <c>true</c> when the tunnel is currently established and passing traffic.
        /// </summary>
        [TikProperty("running", IsReadOnly = true)]
        public bool Running { get; private set; }

        /// <summary>Human-readable identity of the OpenVPN client interface.</summary>
        public override string ToString() => string.Format("{0} -> {1} (port={2} protocol={3} disabled={4})", Name, ConnectTo, Port, Protocol, Disabled);
    }
}
