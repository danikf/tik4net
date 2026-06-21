using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip
{
    /// <summary>
    /// /ip/service: Lists the protocols and ports used by various MikroTik RouterOS services
    /// (ftp, ssh, telnet, www, www-ssl, api, api-ssl, winbox, …). The row set is fixed —
    /// services cannot be added or removed, only modified (port, address restriction,
    /// certificate, TLS version, max-sessions, VRF, and enabled/disabled state).
    /// </summary>
    [TikEntity("/ip/service", IncludeDetails = true)]
    public class IpService
    {
        /// <summary>.id — primary key of the row.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// name — service identifier (e.g. ftp, ssh, telnet, www, www-ssl, api, api-ssl, winbox).
        /// Read-only; the set of services is defined by the router.
        /// </summary>
        [TikProperty("name", IsReadOnly = true, IsMandatory = true)]
        public string Name { get; private set; }

        /// <summary>
        /// port — the TCP/UDP port the service listens on (1..65535).
        /// </summary>
        [TikProperty("port")]
        public int Port { get; set; }

        /// <summary>
        /// address — list of IP/IPv6 prefixes from which the service is accessible.
        /// An empty value means the service is accessible from any address.
        /// WinBox: "Available From"
        /// </summary>
        [TikProperty("address", DefaultValue = "")]
        public string Address { get; set; }

        /// <summary>
        /// certificate — name of the certificate used by this service (relevant for www-ssl and api-ssl).
        /// </summary>
        [TikProperty("certificate", DefaultValue = "")]
        public string Certificate { get; set; }

        /// <summary>
        /// tls-version — specifies which TLS versions to allow for this service.
        /// </summary>
        /// <seealso cref="TlsVersionType"/>
        [TikProperty("tls-version", DefaultValue = "any")]
        public TlsVersionType TlsVersion { get; set; }

        /// <summary>
        /// max-sessions — maximum number of simultaneous sessions for this service (1..1000).
        /// </summary>
        [TikProperty("max-sessions", DefaultValue = "20")]
        public int MaxSessions { get; set; }

        /// <summary>
        /// vrf — specifies which VRF instance is used by this service.
        /// </summary>
        [TikProperty("vrf", DefaultValue = "main")]
        public string Vrf { get; set; }

        /// <summary>
        /// disabled — whether the service is disabled.
        /// </summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>
        /// proto — transport protocol used by the service (tcp or udp). Read-only.
        /// </summary>
        [TikProperty("proto", IsReadOnly = true)]
        public string Proto { get; private set; }

        /// <summary>
        /// dynamic — whether this is a dynamically created connection entry (not a base service row). Read-only.
        /// </summary>
        [TikProperty("dynamic", IsReadOnly = true)]
        public bool Dynamic { get; private set; }

        /// <summary>
        /// invalid — whether the service entry is in an invalid state. Read-only.
        /// </summary>
        [TikProperty("invalid", IsReadOnly = true)]
        public bool Invalid { get; private set; }

        /// <summary>
        /// local — router local address for an active connection (present on dynamic connection rows only). Read-only.
        /// </summary>
        [TikProperty("local", IsReadOnly = true)]
        public string Local { get; private set; }

        /// <summary>
        /// remote — remote address of the active connection (present on dynamic connection rows only). Read-only.
        /// </summary>
        [TikProperty("remote", IsReadOnly = true)]
        public string Remote { get; private set; }

        /// <summary>
        /// connection — true when the row represents an active connection rather than a service definition. Read-only.
        /// </summary>
        [TikProperty("connection", IsReadOnly = true)]
        public bool Connection { get; private set; }

        /// <summary>Human-readable identity — service name and port.</summary>
        public override string ToString() => string.Format("{0}:{1}", Name, Port);

        /// <summary>Allowed TLS versions for SSL services (www-ssl, api-ssl).</summary>
        public enum TlsVersionType
        {
            /// <summary>any — accept all supported TLS versions.</summary>
            [TikEnum("any")] Any,
            /// <summary>only-1.2 — restrict to TLS 1.2 only.</summary>
            [TikEnum("only-1.2")] Only12,
        }
    }
}
