﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// ip/dns: A MikroTik router with DNS feature enabled can be set as a DNS server for any DNS-compliant client. Moreover, MikroTik router can be specified as a primary DNS server under its dhcp-server settings. When the remote requests are enabled, the MikroTik router responds to TCP and UDP DNS requests on port 53. 
    /// </summary>
    [RosRecord("/ip/dns")]
    public class IpDns : ISingleRecord {
        /// <summary>
        /// allow-remote-requests
        /// specifies whether to allow network requests
        /// </summary>
        [RosProperty("allow-remote-requests", DefaultValue = "no")]
        public bool AllowRemoteRequests { get; set; }

        /// <summary>
        /// cache-max-ttl
        /// specifies maximum time-to-live for cache records. In other words, cache records will expire unconditionally after cache-max-ttl time. Shorter TTL received from DNS servers are respected
        /// </summary>
        [RosProperty("cache-max-ttl", DefaultValue = "1w")]
        public string/*time*/ CacheMaxTtl { get; set; }

        /// <summary>
        /// cache-size
        /// specifies the size of DNS cache in KiB
        /// </summary>
        [RosProperty("cache-size", DefaultValue = "2M")]
        public string/*integer: 512..10240*/ CacheSize { get; set; }

        /// <summary>
        /// cache-used
        /// displays the current cache size in KiB
        /// </summary>
        [RosProperty("cache-used",IsReadOnly = true)]
        public string/*read-only: integer*/ CacheUsed { get; private set; }

        /// <summary>
        /// servers
        /// comma seperated list of DNS server IP addresses
        /// </summary>
        [RosProperty("servers", DefaultValue = "0.0.0.0")]
        public string/*IPv4/IPv6 address list*/ Servers { get; set; }
    }

}
