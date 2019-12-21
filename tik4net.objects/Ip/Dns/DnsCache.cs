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
        public static void Flush(ITikConnection connection)
        {
            AccountingSnapshotConnectionExtensions.FlushDnsCache(connection);
        }
    }

    /// <summary>
    /// Connection extension class for <see cref="DnsCache"/>
    /// </summary>
    public static class AccountingSnapshotConnectionExtensions
    {
        /// <summary>
        /// Takes new accounting snapshot (/ip/accounting/snapshot/take)
        /// </summary>
        public static void FlushDnsCache(this ITikConnection connection)
        {
            var cmd = connection.CreateCommand("ip/dns/cache/flush");
            cmd.ExecuteNonQuery();
        }
    }
}
