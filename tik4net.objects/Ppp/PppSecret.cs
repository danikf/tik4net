using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ppp
{
    /// <summary>
    /// ppp/secret: PPP User Database stores PPP user access records with PPP user profile assigned to each user. 
    /// https://wiki.mikrotik.com/wiki/Manual:PPP_AAA
    /// </summary>
    [TikEntity("/ppp/secret", IncludeDetails = true)]
    public class PppSecret
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// caller-id: For  PPTP and  L2TP it is the IP address a client must connect from. For PPPoE it is the MAC address (written in CAPITAL letters) a client must connect from. For ISDN it is the caller's number (that may or may not be provided by the operator) the client may dial-in from
        /// </summary>
        [TikProperty("caller-id", WinboxLabel = "Caller ID")]
        public TikField<string?> CallerId { get; set; }

        /// <summary>
        /// comment: Short description of the user.
        /// </summary>
        [TikProperty("comment")]
        public TikField<string?> Comment { get; set; }

        /// <summary>
        /// disabled: Whether secret will be used.
        /// </summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public TikField<bool?> Disabled { get; set; }

        /// <summary>
        /// limit-bytes-in: Maximal amount of bytes for a session that client can upload.
        /// </summary>
        [TikProperty("limit-bytes-in", DefaultValue = "0", WinboxLabel = "Limit Bytes In")]
        public TikField<int?> LimitBytesIn { get; set; }

        /// <summary>
        /// limit-bytes-out: Maximal amount of bytes for a session that client can download.
        /// </summary>
        [TikProperty("limit-bytes-out", DefaultValue = "0", WinboxLabel = "Limit Bytes Out")]
        public TikField<int?> LimitBytesOut { get; set; }

        /// <summary>
        /// local-address: IP address that will be set locally on ppp interface.
        /// </summary>
        [TikProperty("local-address", WinboxLabel = "Local Address")]
        public TikField<string?> LocalAddress { get; set; }

        /// <summary>
        /// name: Name used for authentication
        /// </summary>
        [TikProperty("name", WinboxLabel = "Name")]
        public TikField<string?> Name { get; set; }

        /// <summary>
        /// password: Password used for authentication
        /// </summary>
        [TikProperty("password", IsSensitive = true, WinboxLabel = "Password")]
        public TikField<string?> Password { get; set; }

        /// <summary>
        /// profile: Which  user profile to use.
        /// </summary>
        [TikProperty("profile", DefaultValue = "default", WinboxLabel = "Profile")]
        public TikField<string?> Profile { get; set; }

        /// <summary>
        /// remote-address: IP address that will be assigned to remote ppp interface.
        /// </summary>
        [TikProperty("remote-address", WinboxLabel = "Remote Address")]
        public TikField<string?> RemoteAddress { get; set; }

        /// <summary>
        /// remote-ipv6-prefix: IPv6 prefix assigned to ppp client. Prefix is added to  ND prefix list enabling  stateless address auto-configuration on ppp interface.Available starting from v5.0.
        /// </summary>
        [TikProperty("remote-ipv6-prefix", WinboxLabel = "Remote IPv6 Prefix")]
        public TikField<string?> RemoteIpv6Prefix { get; set; }

        /// <summary>
        /// routes: Routes that appear on the server when the client is connected. The route format is: dst-address gateway metric (for example, 10.1.0.0/ 24 10.0.0.1 1). Other syntax is not acceptable since it can be represented in incorrect way. Several routes may be specified separated with commas. This parameter will be ignored for OpenVPN.
        /// </summary>
        [TikProperty("routes", WinboxLabel = "Routes")]
        public TikField<string?> Routes { get; set; }

        /// <summary>
        /// service: Specifies the services that particular user will be able to use.
        /// </summary>
        [TikProperty("service", DefaultValue = "any", WinboxLabel = "Service")]
        public TikField<string?> Service { get; set; }

    }

}
