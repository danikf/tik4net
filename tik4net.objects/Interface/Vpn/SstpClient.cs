using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface.Vpn
{
    /// <summary>
    /// /interface/sstp-client: SSTP (Secure Socket Tunneling Protocol) client interface list.
    /// SSTP transports PPP traffic over HTTPS (TCP port 443 by default), making it usable
    /// through most firewalls and NAT devices. Each entry represents one SSTP client tunnel
    /// connecting outbound to a remote SSTP server.
    /// Use <see cref="TikConnectionExtensions.LoadAll{T}"/> / <see cref="TikConnectionExtensions.Save{T}"/>
    /// for CRUD operations.
    /// </summary>
    [TikEntity("/interface/sstp-client", IncludeDetails = true)]
    public class SstpClient
    {
        // ---- Enums ----

        /// <summary>TLS protocol version restriction for <see cref="TlsVersion"/>.</summary>
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
        /// connect-to — remote IP or IPv6 address of the SSTP server to connect to.
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
        /// user — username for PPP authentication.
        /// </summary>
        [TikProperty("user")]
        public string User { get; set; }

        /// <summary>
        /// password — password for PPP authentication.
        /// </summary>
        [TikProperty("password")]
        public string Password { get; set; }

        /// <summary>
        /// port — TCP port of the remote SSTP server.
        /// Default: 443; router default 443, omitted on add when left 0.
        /// </summary>
        // router default 443; omitted on add when left 0
        [TikProperty("port")]
        public int Port { get; set; }

        /// <summary>
        /// profile — PPP profile applied when the tunnel is established.
        /// Default: default
        /// </summary>
        [TikProperty("profile", DefaultValue = "default")]
        public string Profile { get; set; }

        /// <summary>
        /// authentication — comma-separated list of permitted PPP authentication protocols.
        /// Valid values: pap, chap, mschap1, mschap2 (multi-value field, kept as string).
        /// Default: pap,chap,mschap1,mschap2
        /// </summary>
        [TikProperty("authentication", DefaultValue = "pap,chap,mschap1,mschap2")]
        public string Authentication { get; set; }

        /// <summary>
        /// certificate — name of the client TLS certificate; <c>none</c> disables certificate-based auth.
        /// Default: none
        /// </summary>
        [TikProperty("certificate", DefaultValue = "none")]
        public string Certificate { get; set; }

        /// <summary>
        /// verify-server-certificate — when <c>true</c> the client validates the server TLS certificate.
        /// Default: no
        /// </summary>
        [TikProperty("verify-server-certificate", DefaultValue = "no")]
        public bool VerifyServerCertificate { get; set; }

        /// <summary>
        /// verify-server-address-from-certificate — when <c>true</c> the server address is verified
        /// against the CN or SAN in the server certificate.
        /// Default: yes
        /// </summary>
        [TikProperty("verify-server-address-from-certificate", DefaultValue = "yes")]
        public bool VerifyServerAddressFromCertificate { get; set; }

        /// <summary>
        /// tls-version — permitted TLS protocol version(s).
        /// Default: any
        /// </summary>
        /// <seealso cref="TlsVersionType"/>
        [TikProperty("tls-version", DefaultValue = "any")]
        public TlsVersionType TlsVersion { get; set; }

        /// <summary>
        /// ciphers — allowed TLS cipher suites.
        /// Valid values: aes256-sha, aes256-gcm-sha384 (or a combination).
        /// Default: aes256-sha
        /// </summary>
        [TikProperty("ciphers", DefaultValue = "aes256-sha")]
        public string Ciphers { get; set; }

        /// <summary>
        /// pfs — Perfect Forward Secrecy mode.
        /// Valid values: no, yes, required.
        /// Default: no
        /// </summary>
        [TikProperty("pfs", DefaultValue = "no")]
        public string Pfs { get; set; }

        /// <summary>
        /// http-proxy — address of an HTTP proxy to use for the SSTP connection (e.g. <c>192.168.1.1</c>).
        /// Leave empty for a direct connection.
        /// </summary>
        [TikProperty("http-proxy")]
        public string/*IP|IPv6*/ HttpProxy { get; set; }

        /// <summary>
        /// proxy-port — port of the HTTP proxy specified by <see cref="HttpProxy"/>.
        /// Default: 443; router default 443, omitted on add when left 0.
        /// </summary>
        // router default 443; omitted on add when left 0
        [TikProperty("proxy-port")]
        public int ProxyPort { get; set; }

        /// <summary>
        /// keepalive-timeout — seconds between keepalive packets sent to the server; 0 disables.
        /// Default: 60; router default 60, omitted on add when left 0.
        /// </summary>
        // router default 60; omitted on add when left 0
        [TikProperty("keepalive-timeout")]
        public int KeepaliveTimeout { get; set; }

        /// <summary>
        /// add-default-route — whether to add the SSTP remote address as a default route.
        /// Default: no
        /// </summary>
        [TikProperty("add-default-route", DefaultValue = "no")]
        public bool AddDefaultRoute { get; set; }

        /// <summary>
        /// default-route-distance — administrative distance for the auto-created default route
        /// (only relevant when <see cref="AddDefaultRoute"/> is <c>true</c>).
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
        /// max-mtu — maximum transmission unit for the tunnel interface, in bytes.
        /// Default: 1460 (wiki); router returns 1500 on fresh entry, omitted on add when left 0.
        /// </summary>
        // router default 1500; omitted on add when left 0
        [TikProperty("max-mtu")]
        public int MaxMtu { get; set; }

        /// <summary>
        /// max-mru — maximum receive unit for the tunnel interface, in bytes.
        /// Default: 1460 (wiki); router returns 1500 on fresh entry, omitted on add when left 0.
        /// </summary>
        // router default 1500; omitted on add when left 0
        [TikProperty("max-mru")]
        public int MaxMru { get; set; }

        /// <summary>
        /// mrru — maximum reconstructed receive unit when MLPPP is enabled; <c>disabled</c> turns off MLPPP.
        /// Valid range: 512–65535 or the literal string "disabled".
        /// Default: disabled
        /// </summary>
        [TikProperty("mrru", DefaultValue = "disabled")]
        public string Mrru { get; set; }

        /// <summary>
        /// add-sni — when <c>true</c> the client sends the Server Name Indication TLS extension.
        /// Default: no
        /// </summary>
        [TikProperty("add-sni", DefaultValue = "no")]
        public bool AddSni { get; set; }

        /// <summary>comment — optional description of the client interface entry.</summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        // ---- Read-only properties ----

        /// <summary>
        /// running — <c>true</c> when the SSTP tunnel is currently established and passing traffic.
        /// </summary>
        [TikProperty("running", IsReadOnly = true)]
        public bool Running { get; private set; }

        /// <summary>
        /// hw-crypto — <c>true</c> when hardware-accelerated crypto is active on this tunnel.
        /// </summary>
        [TikProperty("hw-crypto", IsReadOnly = true)]
        public bool HwCrypto { get; private set; }

        /// <summary>Human-readable identity of the SSTP client interface.</summary>
        public override string ToString() => string.Format("{0} -> {1} (port={2} disabled={3})", Name, ConnectTo, Port, Disabled);
    }
}
