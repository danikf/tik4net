using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// This sub-menu allows the configuration of how often the DHCP leases will be stored on disk. If they would be saved on disk on every lease change, a lot of disk writes would happen which is very bad for Compact Flash (especially, if lease times are very short). To minimize writes on disk, all changes are saved on disk every store-leases-disk seconds. Additionally leases are always stored on disk on graceful shutdown and reboot. 
    /// </summary>
    [RosRecord("/ip/dhcp-server/config")]
    public class IpDhcpServerConfig : SingleRecordBase {
        /// <summary>
        /// Values for <see cref="StoreLeasesDisk"/> (or use specific time)
        /// </summary>
        public static class StoreLeasesDiskType {
            /// <summary>
            /// never
            /// </summary>
            public const string Immediately = "never";
            /// <summary>
            /// never
            /// </summary>
            public const string Never = "never";
        }

        /// <summary>
        /// store-leases-disk - How frequently lease changes should be stored on disk
        /// </summary>
        /// <seealso cref="StoreLeasesDiskType"/>
        [RosProperty("store-leases-disk")]
        public string StoreLeasesDisk { get; set; }
    }
}
