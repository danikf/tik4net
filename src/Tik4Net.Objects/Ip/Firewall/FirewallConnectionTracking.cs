using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.Firewall
{
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
}
