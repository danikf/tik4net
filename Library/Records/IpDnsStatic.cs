using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// ip/dns
    /// The MikroTik RouterOS has an embedded DNS server feature in DNS cache. It allows you to link the particular domain names with the respective IP addresses and advertize these links to the DNS clients using the router as their DNS server. This feature can also be used to provide fake DNS information to your Option clients. For example, resolving any DNS request for a certain set of domains (or for the whole Internet) to your own page.
    /// 
    /// The server is capable of resolving DNS requests based on POSIX basic regular expressions, so that multiple requets can be matched with the same entry. In case an entry does not conform with DNS naming standards, it is considered a regular expression and marked with ‘R’ flag. The list is ordered and is checked from top to bottom. Regular expressions are checked first, then the plain records. 
    /// Reverse DNS lookup (Address to Name) of the regular expression entries is not possible. You can, however, add an additional plain record with the same IP address and specify some name for it.
    /// 
    /// Remember that the meaning of a dot (.) in regular expressions is any character, so the expression should be escaped properly. For example, if you need to match anything within example.com domain but not all the domains that just end with example.com, like www.another-example.com, use name=".*\\.example\\.com"
    /// 
    /// Regular expression matching is significantly slower than of the plain entries, so it is advised to minimize the number of regular expression rules and optimize the expressions themselves
    /// </summary>
    [RosRecord("/ip/dns/static")]
    public class IpDnsStatic : OrderedSetRecordBase {
        /// <summary>
        /// IP address to resolve domain name with
        /// </summary>
        [RosProperty("address")]
        public string/*IP address*/ Address { get; set; }

        /// <summary>
        /// DNS name to be resolved to a given IP address. May be a regular expression
        /// </summary>
        [RosProperty("name", IsRequired = true)]
        public string/*text*/ Name { get; set; }

        /// <summary>
        /// time-to-live of the DNS record
        /// </summary>
        [RosProperty("ttl")]
        public string/*time*/ Ttl { get; set; }

        /// <summary>
        /// disabled 
        /// </summary>
        [RosProperty("disabled")]
        public bool Disabled { get; set; }
    }
}
