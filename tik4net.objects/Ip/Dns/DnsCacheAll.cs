using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.Dns
{
    /// <summary>
    /// ip/dns: This menu provides a complete list with all DNS records stored on the server 
    /// </summary>
    [TikEntity("/ip/dns/cache/all", SupportedOperations = TikEntityOperations.None)]
    public class DnsCacheAll
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// data
        /// DNS data field. IP address for type "A" records. Other record types may have different contents of the data field (like hostname or arbitrary text)
        /// </summary>
        [TikProperty("data", IsReadOnly = true, WinboxLabel = "Data")]
        public TikField<string?> Data { get; private set; }

        /// <summary>
        /// name
        /// DNS name of the host
        /// </summary>
        [TikProperty("name", IsReadOnly = true, WinboxLabel = "Name")]
        public TikField<string?> Name { get; private set; }

        /// <summary>
        /// ttl
        /// remaining time-to-live for the record
        /// </summary>
        [TikProperty("ttl", IsReadOnly = true, WinboxLabel = "TTL")]
        public TikField<string?> Ttl { get; private set; }

        /// <summary>
        /// type
        /// DNS record type
        /// </summary>
        [TikProperty("type", IsReadOnly = true, WinboxLabel = "Type")]
        public TikField<string?> Type { get; private set; }
    }
}
