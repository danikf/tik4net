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
    [TikEntity("ppp/secret")]
    public class PppSecret
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// caller-id: For  PPTP and  L2TP it is the IP address a client must connect from. For PPPoE it is the MAC address (written in CAPITAL letters) a client must connect from. For ISDN it is the caller's number (that may or may not be provided by the operator) the client may dial-in from
        /// </summary>
        [TikProperty("caller-id")]
        public string CallerId { get; set; }

        /// <summary>
        /// comment: Short description of the user.
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>
        /// disabled: Whether secret will be used.
        /// </summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>
        /// limit-bytes-in: Maximal amount of bytes for a session that client can upload.
        /// </summary>
        [TikProperty("limit-bytes-in", DefaultValue = "0")]
        public int LimitBytesIn { get; set; }

        /// <summary>
        /// limit-bytes-out: Maximal amount of bytes for a session that client can download.
        /// </summary>
        [TikProperty("limit-bytes-out", DefaultValue = "0")]
        public int LimitBytesOut { get; set; }

        /// <summary>
        /// local-address: IP address that will be set locally on ppp interface.
        /// </summary>
        [TikProperty("local-address")]
        public string/*IP address*/ LocalAddress { get; set; }

        /// <summary>
        /// name: Name used for authentication
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// password: Password used for authentication
        /// </summary>
        [TikProperty("password")]
        public string Password { get; set; }

        /// <summary>
        /// profile: Which  user profile to use.
        /// </summary>
        [TikProperty("profile", DefaultValue = "default")]
        public string Profile { get; set; }

        /// <summary>
        /// remote-address: IP address that will be assigned to remote ppp interface.
        /// </summary>
        [TikProperty("remote-address")]
        public string/*IP*/ RemoteAddress { get; set; }

        /// <summary>
        /// remote-ipv6-prefix: IPv6 prefix assigned to ppp client. Prefix is added to  ND prefix list enabling  stateless address auto-configuration on ppp interface.Available starting from v5.0.
        /// </summary>
        [TikProperty("remote-ipv6-prefix")]
        public string/*IPv6 prefix*/ RemoteIpv6Prefix { get; set; }

        /// <summary>
        /// routes: Routes that appear on the server when the client is connected. The route format is: dst-address gateway metric (for example, 10.1.0.0/ 24 10.0.0.1 1). Other syntax is not acceptable since it can be represented in incorrect way. Several routes may be specified separated with commas. This parameter will be ignored for OpenVPN.
        /// </summary>
        [TikProperty("routes")]
        public string Routes { get; set; }

        /// <summary>
        /// service: Specifies the services that particular user will be able to use.
        /// </summary>
        [TikProperty("service", DefaultValue = "any")]
        public string/*any | async | isdn | l2tp | pppoe | pptp | ovpn | sstp*/ Service { get; set; }

    }

}
