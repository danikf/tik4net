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
        /// uptime: time interval elapsed since system boot-up.
        /// </summary>
        [TikProperty("uptime", IsReadOnly = true)]
        public TimeSpan Uptime { get; private set; }

        /// <summary>
        /// version: installed RouterOS release number.
        /// </summary>
        [TikProperty("version", IsReadOnly = true)]
        public string? Version { get; private set; }

        /// <summary>
        /// build-time: timestamp when the current RouterOS version was compiled.
        /// </summary>
        [TikProperty("build-time", IsReadOnly = true)]
        public string? BuildTime { get; private set; }

        /// <summary>
        /// free-memory: amount of unused RAM in bytes.
        /// </summary>
        [TikProperty("free-memory", IsReadOnly = true)]
        public long FreeMemory { get; private set; }

        /// <summary>
        /// total-memory: total amount of installed RAM in bytes.
        /// </summary>
        [TikProperty("total-memory", IsReadOnly = true)]
        public long TotalMemory { get; private set; }

        /// <summary>
        /// cpu: CPU model installed on the board.
        /// </summary>
        [TikProperty("cpu", IsReadOnly = true)]
        public string? Cpu { get; private set; }

        /// <summary>
        /// cpu-count: number of CPUs present on the system; each core is a separate CPU.
        /// </summary>
        [TikProperty("cpu-count", IsReadOnly = true)]
        public long CpuCount { get; private set; }

        /// <summary>
        /// cpu-frequency: current processor speed in MHz.
        /// </summary>
        [TikProperty("cpu-frequency", IsReadOnly = true)]
        public long CpuFrequency { get; private set; }

        /// <summary>
        /// cpu-load: percentage of used CPU resources across all CPUs.
        /// </summary>
        [TikProperty("cpu-load", IsReadOnly = true)]
        public long CpuLoad { get; private set; }

        /// <summary>
        /// free-hdd-space: free space on hard drive or NAND storage in bytes.
        /// </summary>
        [TikProperty("free-hdd-space", IsReadOnly = true)]
        public long FreeHddSpace { get; private set; }

        /// <summary>
        /// total-hdd-space: total size of hard drive or NAND storage in bytes.
        /// </summary>
        [TikProperty("total-hdd-space", IsReadOnly = true)]
        public long TotalHddSpace { get; private set; }

        /// <summary>
        /// write-sect-since-reboot: number of sectors written to disk since last reboot.
        /// </summary>
        [TikProperty("write-sect-since-reboot", IsReadOnly = true)]
        public long WriteSectSinceReboot { get; private set; }

        /// <summary>
        /// write-sect-total: total number of sectors written to disk over the device's lifetime.
        /// </summary>
        [TikProperty("write-sect-total", IsReadOnly = true)]
        public long WriteSectTotal { get; private set; }

        /// <summary>
        /// architecture-name: CPU architecture type (e.g., x86_64, ARM, MIPS).
        /// </summary>
        [TikProperty("architecture-name", IsReadOnly = true)]
        public string? ArchitectureName { get; private set; }

        /// <summary>
        /// board-name: RouterBOARD model name.
        /// </summary>
        [TikProperty("board-name", IsReadOnly = true)]
        public string? BoardName { get; private set; }

        /// <summary>
        /// platform: platform name or type.
        /// </summary>
        [TikProperty("platform", IsReadOnly = true)]
        public string? Platform { get; private set; }
    }

}
