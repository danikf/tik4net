using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.System
{
    /// <summary>
    /// /system/resource (single R/O entity)
    /// </summary>
    [TikEntity("/system/resource", SupportedOperations = TikEntityOperations.None, IsSingleton = true)]
    public class SystemResource
    {
        /// <summary>
        /// uptime: time interval elapsed since system boot-up.
        /// </summary>
        [TikProperty("uptime", IsReadOnly = true, WinboxLabel = "Uptime")]
        public TikField<TimeSpan?> Uptime { get; private set; }

        /// <summary>
        /// version: installed RouterOS release number.
        /// </summary>
        [TikProperty("version", IsReadOnly = true, WinboxLabel = "Version")]
        public TikField<string?> Version { get; private set; }

        /// <summary>
        /// build-time: timestamp when the current RouterOS version was compiled.
        /// </summary>
        [TikProperty("build-time", IsReadOnly = true, WinboxLabel = "Build Time")]
        public TikField<string?> BuildTime { get; private set; }

        /// <summary>
        /// free-memory: amount of unused RAM in bytes.
        /// </summary>
        [TikProperty("free-memory", IsReadOnly = true, WinboxLabel = "Free Memory")]
        public TikField<long?> FreeMemory { get; private set; }

        /// <summary>
        /// total-memory: total amount of installed RAM in bytes.
        /// </summary>
        [TikProperty("total-memory", IsReadOnly = true, WinboxLabel = "Total Memory")]
        public TikField<long?> TotalMemory { get; private set; }

        /// <summary>
        /// cpu: CPU model installed on the board.
        /// </summary>
        [TikProperty("cpu", IsReadOnly = true, WinboxLabel = "CPU")]
        public TikField<string?> Cpu { get; private set; }

        /// <summary>
        /// cpu-count: number of CPUs present on the system; each core is a separate CPU.
        /// </summary>
        [TikProperty("cpu-count", IsReadOnly = true, WinboxLabel = "CPU Count")]
        public TikField<long?> CpuCount { get; private set; }

        /// <summary>
        /// cpu-frequency: current processor speed in MHz.
        /// </summary>
        [TikProperty("cpu-frequency", IsReadOnly = true, WinboxLabel = "freq")]
        public TikField<long?> CpuFrequency { get; private set; }

        /// <summary>
        /// cpu-load: percentage of used CPU resources across all CPUs.
        /// </summary>
        [TikProperty("cpu-load", IsReadOnly = true, WinboxLabel = "CPU Load")]
        public TikField<long?> CpuLoad { get; private set; }

        /// <summary>
        /// free-hdd-space: free space on hard drive or NAND storage in bytes.
        /// </summary>
        [TikProperty("free-hdd-space", IsReadOnly = true, WinboxLabel = "Free HDD Space")]
        public TikField<long?> FreeHddSpace { get; private set; }

        /// <summary>
        /// total-hdd-space: total size of hard drive or NAND storage in bytes.
        /// </summary>
        [TikProperty("total-hdd-space", IsReadOnly = true, WinboxLabel = "Total HDD Size")]
        public TikField<long?> TotalHddSpace { get; private set; }

        /// <summary>
        /// write-sect-since-reboot: number of sectors written to disk since last reboot.
        /// </summary>
        [TikProperty("write-sect-since-reboot", IsReadOnly = true, WinboxLabel = "Sector Writes Since Reboot")]
        public TikField<long?> WriteSectSinceReboot { get; private set; }

        /// <summary>
        /// write-sect-total: total number of sectors written to disk over the device's lifetime.
        /// </summary>
        [TikProperty("write-sect-total", IsReadOnly = true, WinboxLabel = "Total Sector Writes")]
        public TikField<long?> WriteSectTotal { get; private set; }

        /// <summary>
        /// architecture-name: CPU architecture type (e.g., x86_64, ARM, MIPS).
        /// </summary>
        [TikProperty("architecture-name", IsReadOnly = true, WinboxLabel = "Architecture Name")]
        public TikField<string?> ArchitectureName { get; private set; }

        /// <summary>
        /// board-name: RouterBOARD model name.
        /// </summary>
        [TikProperty("board-name", IsReadOnly = true, WinboxLabel = "Board Name")]
        public TikField<string?> BoardName { get; private set; }

        /// <summary>
        /// platform: platform name or type.
        /// </summary>
        [TikProperty("platform", IsReadOnly = true)]
        public TikField<string?> Platform { get; private set; }
    }

}
