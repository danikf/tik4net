using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.RosRecords {
    /// <summary>
    /// ip/firewall/connection tracking: 
    /// </summary>
    [RosRecord("/ip/firewall/connection", IsReadOnly = true)]
    public class FirewallConnection  : IHasId {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [RosProperty(".id", IsRequired = true)]
        public string Id { get; set; }

        /// <summary>
        /// assured: "assured" flag indicates that this connection is assured and that it will not be erased if maximum possible tracked connection count is reached.
        /// </summary>
        [RosProperty("assured",IsReadOnly = true)]
        public bool Assured { get; private set; }

        /// <summary>
        /// connection-mark: connection mark set by  mangle rule.
        /// </summary>
        [RosProperty("connection-mark",IsReadOnly = true)]
        public string ConnectionMark { get; private set; }

        /// <summary>
        /// connection-type: Type of connection, property is empty if connection tracking is unable to determine predefined connection type.
        /// </summary>
        [RosProperty("connection-type",IsReadOnly = true)]
        public string ConnectionType { get; private set; }

        /// <summary>
        /// dst-address: Destination address and port (if protocol is port based).
        /// </summary>
        [RosProperty("dst-address",IsReadOnly = true)]
        public string DstAddress { get; private set; }

        /// <summary>
        /// gre-key: 
        /// </summary>
        [RosProperty("gre-key",IsReadOnly = true)]
        public int GreKey { get; private set; }

        /// <summary>
        /// gre-version: 
        /// </summary>
        [RosProperty("gre-version",IsReadOnly = true)]
        public string GreVersion { get; private set; }

        /// <summary>
        /// icmp-code: 
        /// </summary>
        [RosProperty("icmp-code",IsReadOnly = true)]
        public string IcmpCode { get; private set; }

        /// <summary>
        /// icmp-id: 
        /// </summary>
        [RosProperty("icmp-id",IsReadOnly = true)]
        public string IcmpId { get; private set; }

        /// <summary>
        /// icmp-type: 
        /// </summary>
        [RosProperty("icmp-type",IsReadOnly = true)]
        public string IcmpType { get; private set; }

        /// <summary>
        /// p2p: Shows if connection is identified as p2p by firewall p2p matcher.
        /// </summary>
        [RosProperty("p2p", IsReadOnly = true)]
        public bool P2p { get; private set; }

        /// <summary>
        /// protocol: IP protocol type
        /// </summary>
        [RosProperty("protocol",IsReadOnly = true)]
        public string Protocol { get; private set; }

        /// <summary>
        /// reply-dst-address: Destination address (and port) expected of return packets. Usually the same as "src-address:port"
        /// </summary>
        [RosProperty("reply-dst-address",IsReadOnly = true)]
        public string ReplyDstAddress { get; private set; }

        /// <summary>
        /// reply-src-address: Source address (and port) expected of return packets. Usually the same as "dst-address:port"
        /// </summary>
        [RosProperty("reply-src-address",IsReadOnly = true)]
        public string ReplySrcAddress { get; private set; }

        /// <summary>
        /// src-address: Source address and port (if protocol is port based).
        /// </summary>
        [RosProperty("src-address",IsReadOnly = true)]
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
        [RosProperty("tcp-state",IsReadOnly = true)]
        public string TcpState { get; private set; }

        /// <summary>
        /// timeout: Time after connection will be removed from connection list.
        /// </summary>
        [RosProperty("timeout",IsReadOnly = true)]
        public string Timeout { get; private set; }
    }
}
