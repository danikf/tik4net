using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.Upnp
{
    /// <summary>
    /// /ip/upnp: Universal Plug and Play (UPnP) global settings (singleton). UPnP allows
    /// network devices to discover each other and establish functional services such as data
    /// sharing and internet access. Interfaces are configured under /ip/upnp/interfaces.
    /// Use <see cref="TikConnectionExtensions.LoadSingle{T}"/> to load.
    /// </summary>
    [TikEntity("/ip/upnp", IsSingleton = true)]
    public class IpUpnp
    {
        /// <summary>enabled — enables or disables the UPnP service.</summary>
        [TikProperty("enabled", DefaultValue = "no")]
        public bool Enabled { get; set; }

        /// <summary>allow-disable-external-interface — permits UPnP clients to disable the external interface without authentication (required by the UPnP standard). Default: yes.</summary>
        [TikProperty("allow-disable-external-interface", DefaultValue = "yes")]
        public bool AllowDisableExternalInterface { get; set; }

        /// <summary>show-dummy-rule — enables a workaround for broken UPnP implementations that mishandle an empty rule set. Default: yes.</summary>
        [TikProperty("show-dummy-rule", DefaultValue = "yes")]
        public bool ShowDummyRule { get; set; }

        /// <summary>Human-readable summary of UPnP settings.</summary>
        public override string ToString() => string.Format("enabled={0}", Enabled);
    }
}
