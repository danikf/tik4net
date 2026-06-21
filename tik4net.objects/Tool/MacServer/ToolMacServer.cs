using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Tool.MacServer
{
    /// <summary>
    /// /tool/mac-server — controls access to the MAC-Telnet server (singleton).
    /// MAC-Telnet allows administration over Layer 2 (MAC-layer) without an IP address
    /// configured. Restricts access to a named interface list.
    /// <para>See also: https://help.mikrotik.com/docs/display/ROS/MAC+server</para>
    /// </summary>
    // IncludeDetails omitted — detail= is rejected by this singleton.
    [TikEntity("/tool/mac-server", IsSingleton = true)]
    public class ToolMacServer
    {
        /// <summary>allowed-interface-list — interface list whose members may reach the MAC-Telnet server. Default: all.</summary>
        [TikProperty("allowed-interface-list", DefaultValue = "all")]
        public string AllowedInterfaceList { get; set; }

        /// <summary>Returns a human-readable summary of the MAC-Telnet server settings.</summary>
        public override string ToString() => string.Format("mac-server allowed: {0}", AllowedInterfaceList);
    }
}
