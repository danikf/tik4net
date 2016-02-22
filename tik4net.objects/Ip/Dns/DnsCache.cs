using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.Dns
{
    /// <summary>
    /// ip/dns: This menu provides a list with all address (DNS type "A") records stored on the server 
    /// </summary>
    [TikEntity("ip/dns/cache", IsReadOnly = true)]
    public class DnsCache
    {
        #region Submenu classes - Obsolete
        /// <summary>
        /// Obsolete: use Dns.DnsCacheAll class.
        /// </summary>
        [Obsolete("use Dns.DnsCacheAll class.", true)]
        public abstract class CacheAll
        {

        }
        #endregion

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
}
