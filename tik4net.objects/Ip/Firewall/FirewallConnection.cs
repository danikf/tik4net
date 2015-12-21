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
        #region Submenu classes
        /// <summary>
        /// Allows to disable or enable connection tracking. Disabling connection tracking will cause several firewall features to stop working. 
        /// Features affected by connection tracking:
        /// * NAT
        /// * firewall: connection-bytes, connection-mark, connection-type, connection-state, connection-limit, connection-rate, layer7-protocol, p2p, new-connection-mark, tarpit, p2p matching in simple queues
        /// </summary>
        [TikEntity("ip/firewall/connection/tracking", IsSingleton = true)]
        public class ConnectionTracking
        {
            /// <summary>
            /// enabled: Allows to disable or enable connection tracking. Disabling connection tracking will cause several firewall features to stop working. See the  list of affected features. Starting from v6.0rc2 default value is auto. Which means that connection tracing is disabled until at least one firewall rule is added.
            /// </summary>
            [TikProperty("enabled", DefaultValue = "auto")]
            public string/*yes | no | auto*/ Enabled { get; set; }

            /// <summary>
            /// tcp-syn-sent-timeout: TCP SYN timeout.
            /// </summary>
            [TikProperty("tcp-syn-sent-timeout", DefaultValue = "5s")]
            public string/*time*/ TcpSynSentTimeout { get; set; }

            /// <summary>
            /// tcp-syn-received-timeout: TCP SYN timeout.
            /// </summary>
            [TikProperty("tcp-syn-received-timeout", DefaultValue = "5s")]
            public string/*time*/ TcpSynReceivedTimeout { get; set; }

            /// <summary>
            /// tcp-established-timeout: Time when established TCP connection times out.
            /// </summary>
            [TikProperty("tcp-established-timeout", DefaultValue = "1d")]
            public string/*time*/ TcpEstablishedTimeout { get; set; }

            /// <summary>
            /// tcp-fin-wait-timeout: 
            /// </summary>
            [TikProperty("tcp-fin-wait-timeout", DefaultValue = "10s")]
            public string/*time*/ TcpFinWaitTimeout { get; set; }

            /// <summary>
            /// tcp-close-wait-timeout: 
            /// </summary>
            [TikProperty("tcp-close-wait-timeout", DefaultValue = "10s")]
            public string/*time*/ TcpCloseWaitTimeout { get; set; }

            /// <summary>
            /// tcp-last-ack-timeout: 
            /// </summary>
            [TikProperty("tcp-last-ack-timeout", DefaultValue = "10s")]
            public string/*time*/ TcpLastAckTimeout { get; set; }

            /// <summary>
            /// tcp-time-wait-timeout: 
            /// </summary>
            [TikProperty("tcp-time-wait-timeout", DefaultValue = "10s")]
            public string/*time*/ TcpTimeWaitTimeout { get; set; }

            /// <summary>
            /// tcp-close-timeout: 
            /// </summary>
            [TikProperty("tcp-close-timeout", DefaultValue = "10s")]
            public string/*time*/ TcpCloseTimeout { get; set; }

            /// <summary>
            /// udp-timeout: 
            /// </summary>
            [TikProperty("udp-timeout", DefaultValue = "10s")]
            public string/*time*/ UdpTimeout { get; set; }

            /// <summary>
            /// udp-stream-timeout: 
            /// </summary>
            [TikProperty("udp-stream-timeout", DefaultValue = "3m")]
            public string/*time*/ UdpStreamTimeout { get; set; }

            /// <summary>
            /// icmp-timeout: 
            /// </summary>
            [TikProperty("icmp-timeout", DefaultValue = "10s")]
            public string/*time*/ IcmpTimeout { get; set; }

            /// <summary>
            /// generic-timeout: Timeout for all other connection entries
            /// </summary>
            [TikProperty("generic-timeout", DefaultValue = "10m")]
            public string/*time*/ GenericTimeout { get; set; }

            /// <summary>
            /// max-entries: Max amount of entries that connection tracking table can hold. This value depends on installed amount of RAM. Note that system does not create maximum size connection tracking table when it starts, maximum entry amount can increase if situation demands it and router still has free ram left.
            /// </summary>
            [TikProperty("max-entries", IsReadOnly = true)]
            public int MaxEntries { get; private set; }

            /// <summary>
            /// total-entries: Amount of connections that currently connection table holds.
            /// </summary>
            [TikProperty("total-entries", IsReadOnly = true)]
            public int TotalEntries { get; private set; }
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
