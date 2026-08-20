using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.System
{
    /// <summary>
    /// /system/health — hardware health monitoring status and settings (singleton).
    /// On physical RouterBOARD hardware this menu also lists per-sensor readings
    /// (temperature, voltage, fan speed). The <see cref="StateAfterReboot"/> field
    /// controls whether health monitoring is enabled after the next restart.
    /// <para>Note: <c>=detail=</c> is rejected by this menu; plain print is used.</para>
    /// <para>
    /// Over <c>WinboxNative</c> both states read and write, though no WinBox window names them: the router
    /// sends them at <c>[24,14]</c> keys <c>0x8</c>/<c>0x9</c> and tik4net supplies the names. The
    /// hardware-sensor half of the menu is board-gated and stays empty on a CHR — as it does over the API.
    /// </para>
    /// <para>See also: https://help.mikrotik.com/docs/display/ROS/Health</para>
    /// </summary>
    // IncludeDetails omitted — detail= is rejected by this singleton.
    [TikEntity("/system/health", IsSingleton = true)]
    public class SystemHealth
    {
        /// <summary>state — current health monitoring state (disabled/enabled). Read-only (changed by the system).</summary>
        [TikProperty("state", IsReadOnly = true)]
        public string? State { get; private set; }

        /// <summary>state-after-reboot — health monitoring state applied after the next reboot (disabled/enabled).</summary>
        [TikProperty("state-after-reboot", DefaultValue = "enabled")]
        public string? StateAfterReboot { get; set; }

        /// <summary>Returns a human-readable summary of the health monitoring state.</summary>
        public override string ToString() => string.Format("health: state={0}, after-reboot={1}", State, StateAfterReboot);
    }
}
