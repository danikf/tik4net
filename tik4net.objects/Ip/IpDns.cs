using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip
{
    /// <summary>
    /// ip/dns: A MikroTik router with DNS feature enabled can be set as a DNS server for any DNS-compliant client. Moreover, MikroTik router can be specified as a primary DNS server under its dhcp-server settings. When the remote requests are enabled, the MikroTik router responds to TCP and UDP DNS requests on port 53. 
    /// </summary>
    [TikEntity("ip/dns", IsSingleton = true)]
    public class IpDns
    {
        #region Submenu classes
        /// <summary>
        /// ip/dns: This menu provides a list with all address (DNS type "A") records stored on the server 
        /// </summary>
        [TikEntity("ip/dns/cache", IsReadOnly = true)]
        public class DnsCache
        {
            /// <summary>
            /// ip/dns: This menu provides a complete list with all DNS records stored on the server 
            /// </summary>
            [TikEntity("ip/dns/cache/all", IsReadOnly = true)]
            public class CacheAll
            {
                /// <summary>
                /// .id: primary key of row
                /// </summary>
                [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
                public string Id { get; private set; }

                /// <summary>
                /// data
                /// DNS data field. IP address for type "A" records. Other record types may have different contents of the data field (like hostname or arbitrary text)
                /// </summary>
                [TikProperty("data", IsReadOnly = true)]
                public string/*read-only: text*/ Data { get; private set; }

                /// <summary>
                /// name
                /// DNS name of the host
                /// </summary>
                [TikProperty("name", IsMandatory = true, IsReadOnly = true)]
                public string/*read-only: name*/ Name { get; private set; }

                /// <summary>
                /// ttl
                /// remaining time-to-live for the record
                /// </summary>
                [TikProperty("ttl", IsReadOnly = true)]
                public string/*read-only: time*/ Ttl { get; private set; }

                /// <summary>
                /// type
                /// DNS record type
                /// </summary>
                [TikProperty("type", IsReadOnly = true)]
                public string/*read-only: text*/ Type { get; private set; }
            }


            /// <summary>
            /// .id: primary key of row
            /// </summary>
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string Id { get; private set; }

            /// <summary>
            /// address
            /// IP address of the host
            /// </summary>
            [TikProperty("address", IsReadOnly = true)]
            public string/*read-only: IP address*/ Address { get; private set; }

            /// <summary>
            /// name
            /// DNS name of the host
            /// </summary>
            [TikProperty("name", IsMandatory = true, IsReadOnly = true)]
            public string/*read-only: name*/ Name { get; private set; }

            /// <summary>
            /// ttl
            /// remaining time-to-live for the record
            /// </summary>
            [TikProperty("ttl", IsReadOnly = true)]
            public string/*read-only: time*/ Ttl { get; private set; }

            /// <summary>
            /// clears internal DNS cache 
            /// </summary>
            public void Flush(ITikConnection connection)
            {
                connection.CreateCommand("ip/dns/cache/flush").ExecuteNonQuery();
            }
        }

        /// <summary>
        /// ip/dns
        /// The MikroTik RouterOS has an embedded DNS server feature in DNS cache. It allows you to link the particular domain names with the respective IP addresses and advertize these links to the DNS clients using the router as their DNS server. This feature can also be used to provide fake DNS information to your network clients. For example, resolving any DNS request for a certain set of domains (or for the whole Internet) to your own page.
        /// 
        /// The server is capable of resolving DNS requests based on POSIX basic regular expressions, so that multiple requets can be matched with the same entry. In case an entry does not conform with DNS naming standards, it is considered a regular expression and marked with ‘R’ flag. The list is ordered and is checked from top to bottom. Regular expressions are checked first, then the plain records. 
        /// Reverse DNS lookup (Address to Name) of the regular expression entries is not possible. You can, however, add an additional plain record with the same IP address and specify some name for it.
        /// 
        /// Remember that the meaning of a dot (.) in regular expressions is any character, so the expression should be escaped properly. For example, if you need to match anything within example.com domain but not all the domains that just end with example.com, like www.another-example.com, use name=".*\\.example\\.com"
        /// 
        /// Regular expression matching is significantly slower than of the plain entries, so it is advised to minimize the number of regular expression rules and optimize the expressions themselves
        /// </summary>
        [TikEntity("ip/dns/static", IsOrdered = true)]
        public class DnsStatic
        {
            /// <summary>
            /// .id: primary key of row
            /// </summary>
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string Id { get; private set; }

            /// <summary>
            /// address
            /// IP address to resolve domain name with
            /// </summary>
            [TikProperty("address")]
            public string/*IP address*/ Address { get; set; }

            /// <summary>
            /// name
            /// DNS name to be resolved to a given IP address. May be a regular expression
            /// </summary>
            [TikProperty("name", IsMandatory = true)]
            public string/*text*/ Name { get; set; }

            /// <summary>
            /// ttl
            /// time-to-live of the DNS record
            /// </summary>
            [TikProperty("ttl")]
            public string/*time*/ Ttl { get; set; }

            /// <summary>
            /// disabled: 
            /// </summary>
            [TikProperty("disabled")]
            public bool Disabled { get; set; }
        }

        #endregion

        /// <summary>
        /// allow-remote-requests
        /// specifies whether to allow network requests
        /// </summary>
        [TikProperty("allow-remote-requests", DefaultValue = "no")]
        public bool AllowRemoteRequests { get; set; }

        /// <summary>
        /// cache-max-ttl
        /// specifies maximum time-to-live for cache records. In other words, cache records will expire unconditionally after cache-max-ttl time. Shorter TTL received from DNS servers are respected
        /// </summary>
        [TikProperty("cache-max-ttl", DefaultValue = "1w")]
        public string/*time*/ CacheMaxTtl { get; set; }

        /// <summary>
        /// cache-size
        /// specifies the size of DNS cache in KiB
        /// </summary>
        [TikProperty("cache-size", DefaultValue = "2M")]
        public string/*integer: 512..10240*/ CacheSize { get; set; }

        /// <summary>
        /// cache-used
        /// displays the current cache size in KiB
        /// </summary>
        [TikProperty("cache-used", IsReadOnly = true)]
        public string/*read-only: integer*/ CacheUsed { get; private set; }

        /// <summary>
        /// servers
        /// comma seperated list of DNS server IP addresses
        /// </summary>
        [TikProperty("servers", DefaultValue = "0.0.0.0")]
        public string/*IPv4/IPv6 address list*/ Servers { get; set; }
    }

}
