using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net.Objects.System
{
    [TikEntity("/system/resource", IsReadOnly = true)]
    public class SystemResource
    {
        [TikProperty("uptime", IsReadOnly = true)]
        public string Uptime { get; private set; }

        [TikProperty("version", IsReadOnly = true)]
        public string Version { get; private set; }

        [TikProperty("build-time", IsReadOnly = true)]
        public string BuildTime { get; private set; }

        [TikProperty("free-memory", IsReadOnly = true)]
        public long FreeMemory { get; private set; }

        [TikProperty("total-memory", IsReadOnly = true)]
        public long TotalMemory { get; private set; }

        [TikProperty("cpu", IsReadOnly = true)]
        public string Cpu { get; private set; }

        [TikProperty("cpu-count", IsReadOnly = true)]
        public long CpuCount { get; private set; }

        [TikProperty("cpu-frequency", IsReadOnly = true)]
        public long CpuFrequency { get; private set; }

        [TikProperty("cpu-load", IsReadOnly = true)]
        public long CpuLoad { get; private set; }

        [TikProperty("free-hdd-space", IsReadOnly = true)]
        public long FreeHddSpace { get; private set; }

        [TikProperty("total-hdd-space", IsReadOnly = true)]
        public long TotalHddSpace { get; private set; }

        [TikProperty("write-sect-since-reboot", IsReadOnly = true)]
        public long WriteSectSinceReboot { get; private set; }

        [TikProperty("write-sect-total", IsReadOnly = true)]
        public long WriteSectTotal { get; private set; }

        [TikProperty("architecture-name", IsReadOnly = true)]
        public string ArchitectureName { get; private set; }

        [TikProperty("board-name", IsReadOnly = true)]
        public string BoardName { get; private set; }

        [TikProperty("platform", IsReadOnly = true)]
        public string Platform { get; private set; }
    }

}
