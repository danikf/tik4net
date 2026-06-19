using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.Upnp
{
    /// <summary>
    /// /ip/upnp/interfaces: UPnP interface assignments. Each entry designates an interface as
    /// either <c>external</c> (WAN-facing, public IP) or <c>internal</c> (LAN-facing, client side).
    /// At least one external and one internal interface must be configured for UPnP to operate.
    /// </summary>
    [TikEntity("/ip/upnp/interfaces", IncludeDetails = true)]
    public class IpUpnpInterface
    {
        /// <summary>.id — primary key of the entry.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>interface — name of the interface to assign. Must be an existing interface.</summary>
        [TikProperty("interface", IsMandatory = true)]
        public string Interface { get; set; }

        /// <summary>type — role of this interface in the UPnP topology.
        /// <seealso cref="UpnpInterfaceType"/></summary>
        [TikProperty("type", IsMandatory = true)]
        public UpnpInterfaceType Type { get; set; }

        /// <summary>forced-ip — specific public IP to advertise when the external interface has multiple addresses. Leave empty to use the primary address.</summary>
        [TikProperty("forced-ip", DefaultValue = "")]
        public string/*IP*/ ForcedIp { get; set; }

        /// <summary>disabled — when yes, the entry is inactive.</summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>Human-readable entry summary.</summary>
        public override string ToString() => string.Format("{0} ({1})", Interface, Type);
    }

    /// <summary>UPnP interface role for <see cref="IpUpnpInterface.Type"/>.</summary>
    public enum UpnpInterfaceType
    {
        /// <summary>internal — LAN-facing interface connected to UPnP clients.</summary>
        [TikEnum("internal")] Internal,

        /// <summary>external — WAN-facing interface with the public IP address.</summary>
        [TikEnum("external")] External,
    }
}
