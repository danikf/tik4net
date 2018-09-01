using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ppp
{
    /// <summary>
    /// ppp/active: This submenu allows to monitor active (connected) users. 
    /// https://wiki.mikrotik.com/wiki/Manual:PPP_AAA
    /// </summary>
    [TikEntity("ppp/active", IsReadOnly = true)]
    public class PppActive
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// address: IP address the client got from the server
        /// </summary>
        [TikProperty("address", IsReadOnly = true)]
        public string Address { get; private set; }

        /// <summary>
        /// bytes: Amount of bytes transfered through tis connection. First figure represents amount of transmitted traffic from the router's point of view, while the second one shows amount of received traffic.
        /// </summary>
        [TikProperty("bytes", IsReadOnly = true)]
        public int Bytes { get; private set; }

        /// <summary>
        /// caller-id: For  PPTP and  L2TP it is the IP address the client connected from. For  PPPoE it is the MAC address the client connected from.
        /// </summary>
        [TikProperty("caller-id", IsReadOnly = true)]
        public string CallerId { get; private set; }

        /// <summary>
        /// encoding: Shows encryption and encoding (separated with '/' if asymmetric) being used in this connection
        /// </summary>
        [TikProperty("encoding", IsReadOnly = true)]
        public string Encoding { get; private set; }

        /// <summary>
        /// limit-bytes-in: Maximal amount of bytes the user is allowed to send to the router.
        /// </summary>
        [TikProperty("limit-bytes-in", IsReadOnly = true)]
        public int LimitBytesIn { get; private set; }

        /// <summary>
        /// limit-bytes-out: Maximal amount of bytes the user is allowed to send to the client.
        /// </summary>
        [TikProperty("limit-bytes-out", IsReadOnly = true)]
        public int LimitBytesOut { get; private set; }

        /// <summary>
        /// name: User name supplied at authentication stage
        /// </summary>
        [TikProperty("name", IsReadOnly = true, IsMandatory = true)]
        public string Name { get; private set; }

        /// <summary>
        /// packets: Amount of packets transfered through tis connection. First figure represents amount of transmitted traffic from the router's point of view, while the second one shows amount of received traffic
        /// </summary>
        [TikProperty("packets", IsReadOnly = true)]
        public string Packets { get; private set; }

        /// <summary>
        /// service: Type of service the user is using.
        /// </summary>
        [TikProperty("service", IsReadOnly = true)]
        public string Service { get; private set; }

        /// <summary>
        /// session-id: Shows unique client identifier.
        /// </summary>
        [TikProperty("session-id", IsReadOnly = true)]
        public string SessionId { get; private set; }

        /// <summary>
        /// uptime: User's uptime
        /// </summary>
        [TikProperty("uptime", IsReadOnly = true)]
        public string Uptime { get; private set; }

    }

}
