using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// /system/resource (single R/O entity)
    /// </summary>
    [RosRecord("/system/resource")] // Read-only
    public class SystemResource : SingleRecordBase {
        /// <summary>
        /// uptime
        /// </summary>
        [RosProperty("uptime")] // Read-only
        public TimeSpan Uptime { get; private set; }

        /// <summary>
        /// version
        /// </summary>
        [RosProperty("version")] // Read-only
        public string Version { get; private set; }

        /// <summary>
        /// build-time
        /// </summary>
        [RosProperty("build-time")] // Read-only
        public string BuildTime { get; private set; }

        /// <summary>
        /// free-memory
        /// </summary>
        [RosProperty("free-memory")] // Read-only
        public long FreeMemory { get; private set; }

        /// <summary>
        /// total-memory
        /// </summary>
        [RosProperty("total-memory")] // Read-only
        public long TotalMemory { get; private set; }

        /// <summary>
        /// cpu
        /// </summary>
        [RosProperty("cpu")] // Read-only
        public string Cpu { get; private set; }

        /// <summary>
        /// cpu-count
        /// </summary>
        [RosProperty("cpu-count")] // Read-only
        public long CpuCount { get; private set; }

        /// <summary>
        /// cpu-frequency
        /// </summary>
        [RosProperty("cpu-frequency")] // Read-only
        public long CpuFrequency { get; private set; }

        /// <summary>
        /// cpu-load
        /// </summary>
        [RosProperty("cpu-load")] // Read-only
        public long CpuLoad { get; private set; }

        /// <summary>
        /// free-hdd-space
        /// </summary>
        [RosProperty("free-hdd-space")] // Read-only
        public long FreeHddSpace { get; private set; }

        /// <summary>
        /// total-hdd-space
        /// </summary>
        [RosProperty("total-hdd-space")] // Read-only
        public long TotalHddSpace { get; private set; }

        /// <summary>
        /// write-sect-since-reboot
        /// </summary>
        [RosProperty("write-sect-since-reboot")] // Read-only
        public long WriteSectSinceReboot { get; private set; }

        /// <summary>
        /// write-sect-total
        /// </summary>
        [RosProperty("write-sect-total")] // Read-only
        public long WriteSectTotal { get; private set; }

        /// <summary>
        /// architecture-name
        /// </summary>
        [RosProperty("architecture-name")] // Read-only
        public string ArchitectureName { get; private set; }

        /// <summary>
        /// board-name
        /// </summary>
        [RosProperty("board-name")] // Read-only
        public string BoardName { get; private set; }

        /// <summary>
        /// platform
        /// </summary>
        [RosProperty("platform")] // Read-only
        public string Platform { get; private set; }
    }

}
