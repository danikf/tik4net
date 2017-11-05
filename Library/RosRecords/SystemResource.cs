using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.RosRecords {
    /// <summary>
    /// /system/resource (single R/O entity)
    /// </summary>
    [RosRecord("/system/resource", IsReadOnly = true, IsSingleton = true)]
    public class SystemResource {
        /// <summary>
        /// uptime
        /// </summary>
        [RosProperty("uptime", IsReadOnly = true)]
        public TimeSpan Uptime { get; private set; }

        /// <summary>
        /// version
        /// </summary>
        [RosProperty("version", IsReadOnly = true)]
        public string Version { get; private set; }

        /// <summary>
        /// build-time
        /// </summary>
        [RosProperty("build-time", IsReadOnly = true)]
        public string BuildTime { get; private set; }

        /// <summary>
        /// free-memory
        /// </summary>
        [RosProperty("free-memory", IsReadOnly = true)]
        public long FreeMemory { get; private set; }

        /// <summary>
        /// total-memory
        /// </summary>
        [RosProperty("total-memory", IsReadOnly = true)]
        public long TotalMemory { get; private set; }

        /// <summary>
        /// cpu
        /// </summary>
        [RosProperty("cpu", IsReadOnly = true)]
        public string Cpu { get; private set; }

        /// <summary>
        /// cpu-count
        /// </summary>
        [RosProperty("cpu-count", IsReadOnly = true)]
        public long CpuCount { get; private set; }

        /// <summary>
        /// cpu-frequency
        /// </summary>
        [RosProperty("cpu-frequency", IsReadOnly = true)]
        public long CpuFrequency { get; private set; }

        /// <summary>
        /// cpu-load
        /// </summary>
        [RosProperty("cpu-load", IsReadOnly = true)]
        public long CpuLoad { get; private set; }

        /// <summary>
        /// free-hdd-space
        /// </summary>
        [RosProperty("free-hdd-space", IsReadOnly = true)]
        public long FreeHddSpace { get; private set; }

        /// <summary>
        /// total-hdd-space
        /// </summary>
        [RosProperty("total-hdd-space", IsReadOnly = true)]
        public long TotalHddSpace { get; private set; }

        /// <summary>
        /// write-sect-since-reboot
        /// </summary>
        [RosProperty("write-sect-since-reboot", IsReadOnly = true)]
        public long WriteSectSinceReboot { get; private set; }

        /// <summary>
        /// write-sect-total
        /// </summary>
        [RosProperty("write-sect-total", IsReadOnly = true)]
        public long WriteSectTotal { get; private set; }

        /// <summary>
        /// architecture-name
        /// </summary>
        [RosProperty("architecture-name", IsReadOnly = true)]
        public string ArchitectureName { get; private set; }

        /// <summary>
        /// board-name
        /// </summary>
        [RosProperty("board-name", IsReadOnly = true)]
        public string BoardName { get; private set; }

        /// <summary>
        /// platform
        /// </summary>
        [RosProperty("platform", IsReadOnly = true)]
        public string Platform { get; private set; }
    }

}
