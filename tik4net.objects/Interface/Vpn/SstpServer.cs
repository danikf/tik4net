using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface.Vpn
{
    /// <summary>
    /// /interface/sstp-server/server: SSTP server configuration singleton.
    /// Secure Socket Tunneling Protocol (SSTP) transports a PPP tunnel over a TLS channel.
    /// The use of TLS over TCP port 443 allows SSTP to pass through virtually all firewalls
    /// and proxy servers. The server accepts incoming SSTP connections from clients and
    /// validates them using the configured certificate and authentication methods.
    /// This is a singleton menu — use <see cref="TikConnectionExtensions.LoadSingle{T}"/> to load it.
    /// </summary>
    [TikEntity("/interface/sstp-server/server", IsSingleton = true)] // no =detail= — this server singleton rejects it
    public class SstpServer
    {
        // ---- Enums ----

        /// <summary>Permitted TLS protocol versions for <see cref="TlsVersion"/>.</summary>
        public enum TlsVersionType
        {
            /// <summary>any — allow any supported TLS version.</summary>
            [TikEnum("any")] Any,
            /// <summary>only-1.2 — restrict to TLS 1.2 only.</summary>
            [TikEnum("only-1.2")] Only12,
        }

        /// <summary>Perfect Forward Secrecy mode for <see cref="Pfs"/>.</summary>
        public enum PfsType
        {
            /// <summary>no — PFS is not used.</summary>
            [TikEnum("no")] No,
            /// <summary>yes — PFS is offered but not required.</summary>
            [TikEnum("yes")] Yes,
            /// <summary>required — only connections using PFS are accepted.</summary>
            [TikEnum("required")] Required,
        }

        // ---- Writable properties ----

        /// <summary>
        /// authentication — comma-separated list of permitted PPP authentication methods.
        /// Valid values: pap, chap, mschap1, mschap2.
        /// Default: pap,chap,mschap1,mschap2
        /// </summary>
        [TikProperty("authentication", DefaultValue = "pap,chap,mschap1,mschap2")]
        public string Authentication { get; set; }

        /// <summary>
        /// certificate — name of the TLS server certificate; <c>none</c> disables certificate-based auth.
        /// Default: none
        /// </summary>
        [TikProperty("certificate", DefaultValue = "none")]
        public string Certificate { get; set; }

        /// <summary>
        /// ciphers — comma-separated list of permitted TLS cipher suites.
        /// Valid values: aes256-sha, aes256-gcm-sha384.
        /// Default: aes256-sha,aes256-gcm-sha384
        /// </summary>
        [TikProperty("ciphers", DefaultValue = "aes256-sha,aes256-gcm-sha384")]
        public string Ciphers { get; set; }

        /// <summary>
        /// default-profile — PPP profile applied to new SSTP sessions.
        /// Default: default
        /// </summary>
        [TikProperty("default-profile", DefaultValue = "default")]
        public string DefaultProfile { get; set; }

        /// <summary>
        /// enabled — when <c>true</c> the SSTP server accepts incoming connections.
        /// Default: no
        /// </summary>
        [TikProperty("enabled", DefaultValue = "no")]
        public bool Enabled { get; set; }

        /// <summary>
        /// keepalive-timeout — inactivity timeout in seconds before a client is considered disconnected.
        /// Default: 60; router default 60, omitted on add when left 0.
        /// </summary>
        // router default 60; omitted on add when left 0
        [TikProperty("keepalive-timeout")]
        public int KeepaliveTimeout { get; set; }

        /// <summary>
        /// max-mru — maximum receive unit for SSTP tunnel interfaces, in bytes.
        /// Default: 1500; router default 1500, omitted on add when left 0.
        /// </summary>
        // router default 1500; omitted on add when left 0
        [TikProperty("max-mru")]
        public int MaxMru { get; set; }

        /// <summary>
        /// max-mtu — maximum transmit unit for SSTP tunnel interfaces, in bytes.
        /// Default: 1500; router default 1500, omitted on add when left 0.
        /// </summary>
        // router default 1500; omitted on add when left 0
        [TikProperty("max-mtu")]
        public int MaxMtu { get; set; }

        /// <summary>
        /// mrru — maximum reconstructed receive unit across multi-link PPP tunnel links, in bytes.
        /// Set to <c>disabled</c> to turn off MRRU negotiation. Valid integer range: 512..65535.
        /// Default: disabled
        /// </summary>
        [TikProperty("mrru", DefaultValue = "disabled")]
        public string/*integer or "disabled"*/ Mrru { get; set; }

        /// <summary>
        /// pfs — controls Perfect Forward Secrecy for TLS connections.
        /// <c>no</c> — PFS not used; <c>yes</c> — PFS offered; <c>required</c> — only PFS connections accepted.
        /// Default: no
        /// <seealso cref="PfsType"/>
        /// </summary>
        [TikProperty("pfs", DefaultValue = "no")]
        public PfsType Pfs { get; set; }

        /// <summary>
        /// port — TCP port the server listens on.
        /// Default: 443; router default 443, omitted on add when left 0.
        /// </summary>
        // router default 443; omitted on add when left 0
        [TikProperty("port")]
        public int Port { get; set; }

        /// <summary>
        /// tls-version — permitted TLS protocol version(s).
        /// Default: any
        /// <seealso cref="TlsVersionType"/>
        /// </summary>
        [TikProperty("tls-version", DefaultValue = "any")]
        public TlsVersionType TlsVersion { get; set; }

        /// <summary>
        /// verify-client-certificate — when <c>true</c>, the server validates the client certificate
        /// before allowing the connection.
        /// Default: no
        /// </summary>
        [TikProperty("verify-client-certificate", DefaultValue = "no")]
        public bool VerifyClientCertificate { get; set; }

        /// <summary>Human-readable summary of the SSTP server configuration.</summary>
        public override string ToString() => string.Format("sstp-server enabled={0} port={1} tls-version={2} pfs={3}", Enabled, Port, TlsVersion, Pfs);
    }
}
