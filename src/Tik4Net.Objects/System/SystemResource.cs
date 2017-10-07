using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.System
{
    /// <summary>
    /// /system/resource (single R/O entity)
    /// </summary>
    [TikEntity("/system/resource", IsReadOnly = true, IsSingleton = true)]
    public class SystemResource
    {
        /// <summary>
        /// uptime
        /// </summary>
        [TikProperty("uptime", IsReadOnly = true)]
        public TimeSpan Uptime { get; private set; }

        /// <summary>
        /// version
        /// </summary>
        [TikProperty("version", IsReadOnly = true)]
        public string Version { get; private set; }

        /// <summary>
        /// build-time
        /// </summary>
        [TikProperty("build-time", IsReadOnly = true)]
        public string BuildTime { get; private set; }

        /// <summary>
        /// free-memory
        /// </summary>
        [TikProperty("free-memory", IsReadOnly = true)]
        public long FreeMemory { get; private set; }

        /// <summary>
        /// total-memory
        /// </summary>
        [TikProperty("total-memory", IsReadOnly = true)]
        public long TotalMemory { get; private set; }

        /// <summary>
        /// cpu
        /// </summary>
        [TikProperty("cpu", IsReadOnly = true)]
        public string Cpu { get; private set; }

        /// <summary>
        /// cpu-count
        /// </summary>
        [TikProperty("cpu-count", IsReadOnly = true)]
        public long CpuCount { get; private set; }

        /// <summary>
        /// cpu-frequency
        /// </summary>
        [TikProperty("cpu-frequency", IsReadOnly = true)]
        public long CpuFrequency { get; private set; }

        /// <summary>
        /// cpu-load
        /// </summary>
        [TikProperty("cpu-load", IsReadOnly = true)]
        public long CpuLoad { get; private set; }

        /// <summary>
        /// free-hdd-space
        /// </summary>
        [TikProperty("free-hdd-space", IsReadOnly = true)]
        public long FreeHddSpace { get; private set; }

        /// <summary>
        /// total-hdd-space
        /// </summary>
        [TikProperty("total-hdd-space", IsReadOnly = true)]
        public long TotalHddSpace { get; private set; }

        /// <summary>
        /// write-sect-since-reboot
        /// </summary>
        [TikProperty("write-sect-since-reboot", IsReadOnly = true)]
        public long WriteSectSinceReboot { get; private set; }

        /// <summary>
        /// write-sect-total
        /// </summary>
        [TikProperty("write-sect-total", IsReadOnly = true)]
        public long WriteSectTotal { get; private set; }

        /// <summary>
        /// architecture-name
        /// </summary>
        [TikProperty("architecture-name", IsReadOnly = true)]
        public string ArchitectureName { get; private set; }

        /// <summary>
        /// board-name
        /// </summary>
        [TikProperty("board-name", IsReadOnly = true)]
        public string BoardName { get; private set; }

        /// <summary>
        /// platform
        /// </summary>
        [TikProperty("platform", IsReadOnly = true)]
        public string Platform { get; private set; }
    }

}
