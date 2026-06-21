using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Tool.Romon
{
    /// <summary>
    /// /tool/romon — RoMON (Router Management Overlay Network) global settings (singleton).
    /// RoMON creates an overlay network on top of Ethernet and Wi-Fi interfaces that lets
    /// WinBox manage routers without IP connectivity by hopping through adjacent RoMON-enabled
    /// devices. Requires the <c>romon</c> package.
    /// <para>See also: https://help.mikrotik.com/docs/display/ROS/RoMON</para>
    /// </summary>
    // IncludeDetails omitted — detail= is rejected by this singleton.
    [TikEntity("/tool/romon", IsSingleton = true)]
    public class ToolRomon
    {
        /// <summary>enabled — activates the RoMON agent on this router. Default: no.</summary>
        [TikProperty("enabled", DefaultValue = "no")]
        public bool Enabled { get; set; }

        /// <summary>id — RoMON identifier (MAC address format). When set to 00:00:00:00:00:00 the router's own MAC is used. Default: 00:00:00:00:00:00.</summary>
        [TikProperty("id", DefaultValue = "00:00:00:00:00:00")]
        public string/*MAC*/ Id { get; set; }

        /// <summary>secrets — comma-separated list of shared secrets used to authenticate RoMON peers. Empty string disables authentication.</summary>
        [TikProperty("secrets", DefaultValue = "")]
        public string Secrets { get; set; }

        /// <summary>Returns a human-readable summary of RoMON settings.</summary>
        public override string ToString() => string.Format("romon: enabled={0}, id={1}", Enabled, Id);
    }
}
