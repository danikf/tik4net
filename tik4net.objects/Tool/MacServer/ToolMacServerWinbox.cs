using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Tool.MacServer
{
    /// <summary>
    /// /tool/mac-server/mac-winbox — controls access to WinBox over MAC (singleton).
    /// Restricts which interfaces can reach the router via WinBox using a Layer-2 MAC
    /// connection (no IP required). Separate from the MAC-Telnet server setting.
    /// <para>See also: https://help.mikrotik.com/docs/display/ROS/MAC+server</para>
    /// </summary>
    // IncludeDetails omitted — detail= is rejected by this singleton.
    [TikEntity("/tool/mac-server/mac-winbox", IsSingleton = true)]
    public class ToolMacServerWinbox
    {
        /// <summary>allowed-interface-list — interface list whose members may reach the router via WinBox-over-MAC. Default: all.</summary>
        [TikProperty("allowed-interface-list", DefaultValue = "all")]
        public string AllowedInterfaceList { get; set; }

        /// <summary>Returns a human-readable summary of the MAC-WinBox settings.</summary>
        public override string ToString() => string.Format("mac-winbox allowed: {0}", AllowedInterfaceList);
    }
}
