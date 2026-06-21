using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Radius
{
    /// <summary>
    /// /radius — RADIUS client server table.
    /// RADIUS (Remote Authentication Dial-In User Service) is a remote server that provides
    /// authentication and accounting facilities to various network appliances. MikroTik RouterOS
    /// implements a RADIUS client for PPP, HotSpot, Wireless, DHCP, IPsec, and other services.
    /// https://help.mikrotik.com/docs/display/ROS/RADIUS
    /// </summary>
    [TikEntity("/radius", IncludeDetails = true)]
    public class Radius
    {
        /// <summary>
        /// Protocol used for RADIUS communication.
        /// </summary>
        public enum ProtocolType
        {
            /// <summary>udp — standard RADIUS over UDP (default)</summary>
            [TikEnum("udp")]
            Udp,

            /// <summary>radsec — RADIUS over TLS (RadSec)</summary>
            [TikEnum("radsec")]
            Radsec,
        }

        /// <summary>
        /// Message-Authenticator attribute requirement mode.
        /// </summary>
        public enum RequireMessageAuthType
        {
            /// <summary>no — do not require Message-Authenticator</summary>
            [TikEnum("no")]
            No,

            /// <summary>yes-for-request-resp — require Message-Authenticator on requests and responses</summary>
            [TikEnum("yes-for-request-resp")]
            YesForRequestResp,
        }

        /// <summary>.id — primary key of row</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// service — the router service(s) that will use this RADIUS server.
        /// Comma-separated list of: ppp, login, hotspot, wireless, dhcp, ipsec, dot1x.
        /// Multiple services can be specified (e.g. "ppp,hotspot").
        /// </summary>
        [TikProperty("service")]
        public string Service { get; set; }

        /// <summary>
        /// called-id — the ID of the server depends on the protocol: for HotSpot it is the IP of the
        /// HotSpot interface, for PPP it is the hostname of the router.
        /// </summary>
        [TikProperty("called-id")]
        public string CalledId { get; set; }

        /// <summary>
        /// domain — the Microsoft Windows domain to which the user belongs. For HotSpot and PPP services
        /// only. Used to validate usernames that do not include domain information.
        /// </summary>
        [TikProperty("domain")]
        public string Domain { get; set; }

        /// <summary>
        /// address — IP address of the RADIUS server. Supports VRF notation (address@vrf).
        /// </summary>
        [TikProperty("address")]
        public string Address { get; set; }

        /// <summary>
        /// secret — the shared secret of the RADIUS server used to authenticate the client.
        /// </summary>
        [TikProperty("secret")]
        public string Secret { get; set; }

        /// <summary>
        /// authentication-port — RADIUS server port used for authentication.
        /// Default: 1812.
        /// </summary>
        [TikProperty("authentication-port")] // router default 1812; omitted on add when left 0
        public int AuthenticationPort { get; set; }

        /// <summary>
        /// accounting-port — RADIUS server port used for accounting.
        /// Default: 1813.
        /// </summary>
        [TikProperty("accounting-port")] // router default 1813; omitted on add when left 0
        public int AccountingPort { get; set; }

        /// <summary>
        /// timeout — timeout after which the request should be resent (standard UDP path).
        /// Value is a time string, e.g. "1s100ms". Default: 1s100ms (1100ms).
        /// </summary>
        [TikProperty("timeout", DefaultValue = "1s100ms")]
        public string/*time*/ Timeout { get; set; }

        /// <summary>
        /// radsec-timeout — timeout after which the request should be resent when using RadSec (TLS).
        /// Value is a time string, e.g. "3s300ms". Default: 3s300ms.
        /// Only relevant when <see cref="Protocol"/> is <see cref="ProtocolType.Radsec"/>.
        /// </summary>
        [TikProperty("radsec-timeout", DefaultValue = "3s300ms")]
        public string/*time*/ RadsecTimeout { get; set; }

        /// <summary>
        /// accounting-backup — designates this entry as a backup RADIUS server for accounting.
        /// The backup server is used only when the primary server is not available.
        /// Default: false.
        /// </summary>
        [TikProperty("accounting-backup", DefaultValue = "no")]
        public bool AccountingBackup { get; set; }

        /// <summary>
        /// realm — explicitly stated realm (domain) for the user, so the correct user database is chosen.
        /// </summary>
        [TikProperty("realm")]
        public string Realm { get; set; }

        /// <summary>
        /// src-address — source IP address of the outbound RADIUS packets.
        /// Default: 0.0.0.0 (use the routing-determined source address).
        /// </summary>
        [TikProperty("src-address", DefaultValue = "0.0.0.0")]
        public string/*IP*/ SrcAddress { get; set; }

        /// <summary>
        /// protocol — the protocol to use when communicating with the RADIUS server.
        /// <seealso cref="ProtocolType"/>
        /// Default: udp.
        /// </summary>
        [TikProperty("protocol", DefaultValue = "udp")]
        public ProtocolType Protocol { get; set; }

        /// <summary>
        /// certificate — the certificate file to use for RadSec (TLS) communication.
        /// Only relevant when <see cref="Protocol"/> is <see cref="ProtocolType.Radsec"/>.
        /// Default: none.
        /// </summary>
        [TikProperty("certificate", DefaultValue = "none")]
        public string Certificate { get; set; }

        /// <summary>
        /// require-message-auth — controls whether the Message-Authenticator attribute is required
        /// in RADIUS requests and responses.
        /// <seealso cref="RequireMessageAuthType"/>
        /// Default: yes-for-request-resp.
        /// </summary>
        [TikProperty("require-message-auth", DefaultValue = "yes-for-request-resp")]
        public RequireMessageAuthType RequireMessageAuth { get; set; }

        /// <summary>
        /// disabled — whether this RADIUS server entry is disabled.
        /// Default: false.
        /// </summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>
        /// comment — descriptive comment for this entry.
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        // --- Read-only properties ---

        /// <summary>
        /// invalid — whether the entry is considered invalid/inapplicable by RouterOS (read-only).
        /// </summary>
        [TikProperty("invalid", IsReadOnly = true)]
        public bool Invalid { get; private set; }

        /// <summary>
        /// status — current connection status of the RADIUS server entry (read-only).
        /// </summary>
        [TikProperty("status", IsReadOnly = true)]
        public string Status { get; private set; }

        /// <summary>Returns a human-readable summary of the entry.</summary>
        public override string ToString()
        {
            return string.Format("{0} [{1}]", Address, Service);
        }
    }
}
