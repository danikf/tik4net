using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// Allows to disable or enable connection tracking. Disabling connection tracking will cause several firewall features to stop working. 
    /// Features affected by connection tracking:
    /// * NAT
    /// * firewall: connection-bytes, connection-mark, connection-type, connection-state, connection-limit, connection-rate, layer7-protocol, p2p, new-connection-mark, tarpit, p2p matching in simple queues
    /// </summary>
    [RosRecord("/ip/firewall/connection/tracking")]
    public class IpFirewallConnectionTracking : SingleRecordBase {
        /// <summary>
        /// enabled: Allows to disable or enable connection tracking. Disabling connection tracking will cause several firewall features to stop working. See the  list of affected features. Starting from v6.0rc2 default value is auto. Which means that connection tracing is disabled until at least one firewall rule is added.
        /// </summary>
        [RosProperty("enabled", DefaultValue = "auto")]
        public string/*yes | no | auto*/ Enabled { get; set; }

        /// <summary>
        /// tcp-syn-sent-timeout: TCP SYN timeout.
        /// </summary>
        [RosProperty("tcp-syn-sent-timeout", DefaultValue = "5s")]
        public string/*time*/ TcpSynSentTimeout { get; set; }

        /// <summary>
        /// tcp-syn-received-timeout: TCP SYN timeout.
        /// </summary>
        [RosProperty("tcp-syn-received-timeout", DefaultValue = "5s")]
        public string/*time*/ TcpSynReceivedTimeout { get; set; }

        /// <summary>
        /// tcp-established-timeout: Time when established TCP connection times out.
        /// </summary>
        [RosProperty("tcp-established-timeout", DefaultValue = "1d")]
        public string/*time*/ TcpEstablishedTimeout { get; set; }

        /// <summary>
        /// tcp-fin-wait-timeout: 
        /// </summary>
        [RosProperty("tcp-fin-wait-timeout", DefaultValue = "10s")]
        public string/*time*/ TcpFinWaitTimeout { get; set; }

        /// <summary>
        /// tcp-close-wait-timeout: 
        /// </summary>
        [RosProperty("tcp-close-wait-timeout", DefaultValue = "10s")]
        public string/*time*/ TcpCloseWaitTimeout { get; set; }

        /// <summary>
        /// tcp-last-ack-timeout: 
        /// </summary>
        [RosProperty("tcp-last-ack-timeout", DefaultValue = "10s")]
        public string/*time*/ TcpLastAckTimeout { get; set; }

        /// <summary>
        /// tcp-time-wait-timeout: 
        /// </summary>
        [RosProperty("tcp-time-wait-timeout", DefaultValue = "10s")]
        public string/*time*/ TcpTimeWaitTimeout { get; set; }

        /// <summary>
        /// tcp-close-timeout: 
        /// </summary>
        [RosProperty("tcp-close-timeout", DefaultValue = "10s")]
        public string/*time*/ TcpCloseTimeout { get; set; }

        /// <summary>
        /// udp-timeout: 
        /// </summary>
        [RosProperty("udp-timeout", DefaultValue = "10s")]
        public string/*time*/ UdpTimeout { get; set; }

        /// <summary>
        /// udp-stream-timeout: 
        /// </summary>
        [RosProperty("udp-stream-timeout", DefaultValue = "3m")]
        public string/*time*/ UdpStreamTimeout { get; set; }

        /// <summary>
        /// icmp-timeout: 
        /// </summary>
        [RosProperty("icmp-timeout", DefaultValue = "10s")]
        public string/*time*/ IcmpTimeout { get; set; }

        /// <summary>
        /// generic-timeout: Timeout for all other connection entries
        /// </summary>
        [RosProperty("generic-timeout", DefaultValue = "10m")]
        public string/*time*/ GenericTimeout { get; set; }

        /// <summary>
        /// max-entries: Max amount of entries that connection tracking table can hold. This value depends on installed amount of RAM. Note that system does not create maximum size connection tracking table when it starts, maximum entry amount can increase if situation demands it and router still has free ram left.
        /// </summary>
        [RosProperty("max-entries",IsReadOnly = true)]
        public int MaxEntries { get; private set; }

        /// <summary>
        /// total-entries: Amount of connections that currently connection table holds.
        /// </summary>
        [RosProperty("total-entries",IsReadOnly = true)]
        public int TotalEntries { get; private set; }
    }
}
