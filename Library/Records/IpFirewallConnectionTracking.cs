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
        [RosProperty("enabled")]
        public string/*yes | no | auto*/ Enabled { get; set; } = "auto"; // TODO: Make enum

        /// <summary>
        /// tcp-syn-sent-timeout: TCP SYN timeout.
        /// </summary>
        [RosProperty("tcp-syn-sent-timeout")] // TODO: make all of these TimeSpans
        public string/*time*/ TcpSynSentTimeout { get; set; } = "5s";

        /// <summary>
        /// tcp-syn-received-timeout: TCP SYN timeout.
        /// </summary>
        [RosProperty("tcp-syn-received-timeout")]
        public string/*time*/ TcpSynReceivedTimeout { get; set; } = "5s";

        /// <summary>
        /// tcp-established-timeout: Time when established TCP connection times out.
        /// </summary>
        [RosProperty("tcp-established-timeout")]
        public string/*time*/ TcpEstablishedTimeout { get; set; } = "1d";

        /// <summary>
        /// tcp-fin-wait-timeout: 
        /// </summary>
        [RosProperty("tcp-fin-wait-timeout")]
        public string/*time*/ TcpFinWaitTimeout { get; set; } = "10s";

        /// <summary>
        /// tcp-close-wait-timeout: 
        /// </summary>
        [RosProperty("tcp-close-wait-timeout")]
        public string/*time*/ TcpCloseWaitTimeout { get; set; } = "10s";

        /// <summary>
        /// tcp-last-ack-timeout: 
        /// </summary>
        [RosProperty("tcp-last-ack-timeout")]
        public string/*time*/ TcpLastAckTimeout { get; set; } = "10s";

        /// <summary>
        /// tcp-time-wait-timeout: 
        /// </summary>
        [RosProperty("tcp-time-wait-timeout")]
        public string/*time*/ TcpTimeWaitTimeout { get; set; } = "10s";

        /// <summary>
        /// tcp-close-timeout: 
        /// </summary>
        [RosProperty("tcp-close-timeout")]
        public string/*time*/ TcpCloseTimeout { get; set; } = "10s";

        /// <summary>
        /// udp-timeout: 
        /// </summary>
        [RosProperty("udp-timeout")]
        public string/*time*/ UdpTimeout { get; set; } = "10s";

        /// <summary>
        /// udp-stream-timeout: 
        /// </summary>
        [RosProperty("udp-stream-timeout")]
        public string/*time*/ UdpStreamTimeout { get; set; } = "3m";

        /// <summary>
        /// icmp-timeout: 
        /// </summary>
        [RosProperty("icmp-timeout")]
        public string/*time*/ IcmpTimeout { get; set; } = "10s";

        /// <summary>
        /// generic-timeout: Timeout for all other connection entries
        /// </summary>
        [RosProperty("generic-timeout")]
        public string/*time*/ GenericTimeout { get; set; } = "10m";

        /// <summary>
        /// max-entries: Max amount of entries that connection tracking table can hold. This value depends on installed amount of RAM. Note that system does not create maximum size connection tracking table when it starts, maximum entry amount can increase if situation demands it and router still has free ram left.
        /// </summary>
        [RosProperty("max-entries", IsReadOnly = true)]
        public int MaxEntries { get; private set; }

        /// <summary>
        /// total-entries: Amount of connections that currently connection table holds.
        /// </summary>
        [RosProperty("total-entries", IsReadOnly = true)]
        public int TotalEntries { get; private set; }
    }
}
