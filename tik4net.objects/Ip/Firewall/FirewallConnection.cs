using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.Firewall
{
    /// <summary>
    /// ip/firewall/connection tracking: 
    /// </summary>
    [TikEntity("ip/firewall/connection", IsReadOnly = true)]
    public class FirewallConnection
    {
        # region Submenu classes
        /// <summary>
        /// Obsolete: use FirewallConnectionTracking class.
        /// </summary>
        [Obsolete("use FirewallConnectionTracking class.", true)]
        public abstract class ConnectionTracking
        {
        }
        #endregion

        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// assured: "assured" flag indicates that this connection is assured and that it will not be erased if maximum possible tracked connection count is reached.
        /// </summary>
        [TikProperty("assured", IsReadOnly = true)]
        public bool Assured { get; private set; }

        /// <summary>
        /// connection-mark: connection mark set by  mangle rule.
        /// </summary>
        [TikProperty("connection-mark", IsReadOnly = true)]
        public string ConnectionMark { get; private set; }

        /// <summary>
        /// connection-type: Type of connection, property is empty if connection tracking is unable to determine predefined connection type.
        /// </summary>
        [TikProperty("connection-type", IsReadOnly = true)]
        public string ConnectionType { get; private set; }

        /// <summary>
        /// dst-address: Destination address and port (if protocol is port based).
        /// </summary>
        [TikProperty("dst-address", IsReadOnly = true)]
        public string DstAddress { get; private set; }

        /// <summary>
        /// gre-key: 
        /// </summary>
        [TikProperty("gre-key", IsReadOnly = true)]
        public int GreKey { get; private set; }

        /// <summary>
        /// gre-version: 
        /// </summary>
        [TikProperty("gre-version", IsReadOnly = true)]
        public string GreVersion { get; private set; }

        /// <summary>
        /// icmp-code: 
        /// </summary>
        [TikProperty("icmp-code", IsReadOnly = true)]
        public string IcmpCode { get; private set; }

        /// <summary>
        /// icmp-id: 
        /// </summary>
        [TikProperty("icmp-id", IsReadOnly = true)]
        public string IcmpId { get; private set; }

        /// <summary>
        /// icmp-type: 
        /// </summary>
        [TikProperty("icmp-type", IsReadOnly = true)]
        public string IcmpType { get; private set; }

        /// <summary>
        /// p2p: Shows if connection is identified as p2p by firewall p2p matcher.
        /// </summary>
        [TikProperty("p2p", IsReadOnly = true)]
        public bool P2p { get; private set; }

        /// <summary>
        /// protocol: IP protocol type
        /// </summary>
        [TikProperty("protocol", IsReadOnly = true)]
        public string Protocol { get; private set; }

        /// <summary>
        /// reply-dst-address: Destination address (and port) expected of return packets. Usually the same as "src-address:port"
        /// </summary>
        [TikProperty("reply-dst-address", IsReadOnly = true)]
        public string ReplyDstAddress { get; private set; }

        /// <summary>
        /// reply-src-address: Source address (and port) expected of return packets. Usually the same as "dst-address:port"
        /// </summary>
        [TikProperty("reply-src-address", IsReadOnly = true)]
        public string ReplySrcAddress { get; private set; }

        /// <summary>
        /// src-address: Source address and port (if protocol is port based).
        /// </summary>
        [TikProperty("src-address", IsReadOnly = true)]
        public string SrcAddress { get; private set; }

        /// <summary>
        /// tcp-state
        /// Current state of TCP connection&#160;:
        ///  "established"
        ///  "time-wait"
        ///  "close"
        ///  "syn-sent" 
        ///  "syn-received"
        /// </summary>
        [TikProperty("tcp-state", IsReadOnly = true)]
        public string TcpState { get; private set; }

        /// <summary>
        /// timeout: Time after connection will be removed from connection list.
        /// </summary>
        [TikProperty("timeout", IsReadOnly = true)]
        public string Timeout { get; private set; }
    }
}
