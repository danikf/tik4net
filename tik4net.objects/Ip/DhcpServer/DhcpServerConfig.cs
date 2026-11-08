using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip
{
    /// <summary>
    /// This sub-menu allows the configuration of how often the DHCP leases will be stored on disk. If they would be saved on disk on every lease change, a lot of disk writes would happen which is very bad for Compact Flash (especially, if lease times are very short). To minimize writes on disk, all changes are saved on disk every store-leases-disk seconds. Additionally leases are always stored on disk on graceful shutdown and reboot. 
    /// </summary>
    [TikEntity("/ip/dhcp-server/config", IsSingleton = true)]
    public class DhcpServerConfig
    {
        /// <summary>
        /// The words <see cref="StoreLeasesDisk"/> takes besides a duration, as strings.
        /// </summary>
        [Obsolete("StoreLeasesDisk is a TikDuration: write TikDuration.FromSpecial(TikDurationSpecial.Immediately) or TikDuration.FromSpecial(TikDurationSpecial.Never), or a TimeSpan for an interval.", false)]
        public static class StoreLeasesDiskType
        {
            /// <summary>
            /// immediately — store every lease change on disk as it happens.
            /// </summary>
            public const string Immediately = "immediately";
            /// <summary>
            /// never — do not store lease changes on disk.
            /// </summary>
            public const string Never = "never";
        }

        /// <summary>
        /// store-leases-disk - How frequently lease changes are stored on disk: an interval (<c>5m</c>), or
        /// <see cref="TikDurationSpecial.Immediately"/> / <see cref="TikDurationSpecial.Never"/>
        /// (<c>TikDuration.FromSpecial(...)</c>).
        /// </summary>
        [TikProperty("store-leases-disk", WinboxLabel = "Store Leases On Disk")]
        public TikField<TikDuration?> StoreLeasesDisk { get; set; }
    }
}
