using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// ip/dns: This menu provides a complete list with all DNS records stored on the server 
    /// </summary>
    [RosRecord("/ip/dns/cache/all", IsReadOnly = true)]
    public class IpDnsCacheAll  : ISetRecord {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [RosProperty(".id", IsRequired = true)]
        public string Id { get; set; }

        /// <summary>
        /// data
        /// DNS data field. IP address for type "A" records. Other record types may have different contents of the data field (like hostname or arbitrary text)
        /// </summary>
        [RosProperty("data",IsReadOnly = true)]
        public string/*read-only: text*/ Data { get; private set; }

        /// <summary>
        /// name
        /// DNS name of the host
        /// </summary>
        [RosProperty("name", IsRequired = true, IsReadOnly = true)]
        public string/*read-only: name*/ Name { get; private set; }

        /// <summary>
        /// ttl
        /// remaining time-to-live for the record
        /// </summary>
        [RosProperty("ttl",IsReadOnly = true)]
        public string/*read-only: time*/ Ttl { get; private set; }

        /// <summary>
        /// type
        /// DNS record type
        /// </summary>
        [RosProperty("type",IsReadOnly = true)]
        public string/*read-only: text*/ Type { get; private set; }
    }
}
