using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.Dns
{
    /// <summary>
    /// ip/dns: This menu provides a complete list with all DNS records stored on the server 
    /// </summary>
    [TikEntity("ip/dns/cache/all", IsReadOnly = true)]
    public class DnsCacheAll
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
}
