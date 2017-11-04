using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// ip/dns: This menu provides a list with all address (DNS type "A") records stored on the server 
    /// </summary>
    [TikRecord("/ip/dns/cache", IsReadOnly = true)]
    public class DnsCache {
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

        /* TODO
        /// <summary>
        /// clears internal DNS cache 
        /// </summary>
        public void Flush(ITikConnection connection) {
            connection.CreateCommand("ip/dns/cache/flush").ExecuteNonQuery();
        }
        */
    }
}
