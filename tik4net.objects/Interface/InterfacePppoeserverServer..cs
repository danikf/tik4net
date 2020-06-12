using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface
{
    /// <summary>
    /// There are two types of interface (tunnel) items in PPPoE server configuration - static users and dynamic connections. An interface is created for each tunnel established to the given server. Static interfaces are added administratively if there is a need to reference the particular interface name (in firewall rules or elsewhere) created for the particular user. Dynamic interfaces are added to this list automatically whenever a user is connected and its username does not match any existing static entry (or in case the entry is active already, as there can not be two separate tunnel interfaces referenced by the same name - set one-session-per-host value if this is a problem). Dynamic interfaces appear when a user connects and disappear once the user disconnects, so it is impossible to reference the tunnel created for that use in router configuration (for example, in firewall), so if you need a persistent rules for that user, create a static entry for him/her. Otherwise it is safe to use dynamic configuration. Note that in both cases PPP users must be configured properly - static entries do not replace PPP configuration. 
    /// </summary>
    /// <seealso cref="https://wiki.mikrotik.com/wiki/Manual:Interface/PPPoE#PPPoE_Server"/>
    /// <seealso cref="https://wiki.mikrotik.com/wiki/Pppoe_server_with_profiles"/>
    /// <seealso cref="https://wiki.mikrotik.com/wiki/PPPOE_Server"/>
    [TikEntity("interface/pppoe-server/server", IncludeDetails = true)]
    public class InterfacePppoeserverServer
    {
        /// <summary>
        /// .id
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// service-name  -  The PPPoE service name. Server will accept clients which sends PADI message with service-names that matches this setting or if service-name field in PADI message is not set.
        /// </summary>
        [TikProperty("service-name", IsMandatory = true)]
        public string ServiceName { get; set; }

        /// <summary>
        /// interface -  	Interface that the clients are connected to
        /// </summary>
        [TikProperty("interface", IsMandatory = true)]
        public string Interface { get; set; }

        /// <summary>
        /// max-mtu - Maximum Transmission Unit. The optimal value is the MTU of the interface the tunnel is working over reduced by 20 (so, for 1500-byte Ethernet link, set the MTU to 1480 to avoid fragmentation of packets)
        /// </summary>
        [TikProperty("max-mtu", DefaultValue = "1480")]
        public string MaxMtu { get; set; }

        /// <summary>
        /// max-mtu - Maximum Receive Unit. The optimal value is the MTU of the interface the tunnel is working over reduced by 20 (so, for 1500-byte Ethernet link, set the MTU to 1480 to avoid fragmentation of packets)
        /// </summary>
        [TikProperty("max-mru", DefaultValue = "1480")]
        public string MaxMru { get; set; }

        /// <summary>
        /// mrru -  	Maximum packet size that can be received on the link. If a packet is bigger than tunnel MTU, it will be split into multiple packets, allowing full size IP or Ethernet packets to be sent over the tunnel.
        /// </summary>
        [TikProperty("mrru", DefaultValue = "disabled")]
        public string Mrru { get; set; }

        /// <summary>
        /// authentication - Authentication algorithm  (mschap2 | mschap1 | chap | pap); Default: "mschap2, mschap1, chap, pap)
        /// </summary>
        [TikProperty("authentication", IsMandatory = true, DefaultValue = "pap,chap,mschap1,mschap2")]
        public string Authentication { get; set; }

        /// <summary>
        /// keepalive-timeout - Defines the time period (in seconds) after which the router is starting to send keepalive packets every second. If there is no traffic and no keepalive responses arrive for that period of time (i.e. 2 * keepalive-timeout), the non responding client is proclaimed disconnected.
        /// </summary>
        [TikProperty("keepalive-timeout", DefaultValue = "10")]
        public string KeepaliveTimeout { get; set; }

        /// <summary>
        /// one-session-per-host - Allow only one session per host (determined by MAC address). If a host tries to establish a new session, the old one will be closed.
        /// </summary>
        [TikProperty("one-session-per-host")]
        public bool OneSessionPerHost { get; set; }

        /// <summary>
        /// max-sessions - Maximum number of clients that the AC can serve. '0' = no limitations.
        /// </summary>
        [TikProperty("max-sessions")]
        public string MaxSessions { get; set; }

        /// <summary>
        /// pado-delay
        /// </summary>
        [TikProperty("pado-delay")]
        public string PadoDelay { get; set; }

        /// <summary>
        /// default-profile - Default user profile to use
        /// </summary>
        [TikProperty("default-profile", IsMandatory = true, DefaultValue = "default")]
        public string DefaultProfile { get; set; }

        /// <summary>
        /// running
        /// </summary>
        [TikProperty("invalid", IsReadOnly = true)]
        public bool Running { get; private set; }

        /// <summary>
        /// disabled
        /// </summary>
        [TikProperty("disabled", DefaultValue = "False")]
        public bool Disabled { get; set; }
    }

}
