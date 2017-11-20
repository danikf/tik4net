using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// ip/dhcp client
    /// The MikroTik RouterOS DHCP client may be enabled on any Ethernet-like interface at a time. The client will accept an address, netmask, default gateway, and two dns server addresses. The received IP address will be added to the interface with the respective netmask. The default gateway will be added to the routing table as a dynamic entry. Should the DHCP client be disabled or not renew an address, the dynamic default route will be removed. If there is already a default route installed prior the DHCP client obtains one, the route obtained by the DHCP client would be shown as invalid.
    /// RouterOS DHCP cilent asks for following options:
    /// </summary>
    [RosRecord("/ip/dhcp-client")]
    public class IpDhcpClient  : SetRecordBase {
        /// <summary>
        /// add-default-route: Whether to install default route in routing table received from dhcp server. By default RouterOS client complies to RFC and ignores option 3 if classless option 121 is received. To force client not to ignore option 3 set special-classless. This parameter is available in v6rc12+
        /// yes - adds classless route if received, if not then add default route(old behavior)
        /// special-classless - adds both classless route if received and default route(MS style)
		/// </summary>
		[RosProperty("add-default-route", DefaultValue = "yes")]
        public AddDefaultRouteType AddDefaultRoute { get; set; }

        /// <summary>
        /// client-id: Corresponds to the settings suggested by the Option administrator or ISP. If not specified, client's MAC address will be sent
        /// </summary>
        [RosProperty("client-id")]
        public string ClientId { get; set; }

        /// <summary>
        /// comment: Short description of the client
        /// </summary>
        [RosProperty("comment")]
        public string Comment { get; set; }

        /// <summary>
        /// default-route-distance: Distance of default route. Applicable if add-default-route is set to yes.
        /// </summary>
        [RosProperty("default-route-distance")]
        public string DefaultRouteDistance { get; set; }

        /// <summary>
        /// disabled: 
        /// </summary>
        [RosProperty("disabled")]
        public bool Disabled { get; set; }

        /// <summary>
        /// host-name: Host name of the client sent to a DHCP server. If not specified, client's system identity will be used.
        /// </summary>
        [RosProperty("host-name")]
        public string HostName { get; set; }

        /// <summary>
        /// interface: Interface on which DHCP client will be running.
        /// </summary>
        [RosProperty("interface")]
        public string Interface { get; set; }

        /// <summary>
        /// use-peer-dns: Whether to accept the  DNS settings advertised by  DHCP Server. (Will override the settings put in the /ip dns submenu.
        /// </summary>
        [RosProperty("use-peer-dns", DefaultValue = "yes")]
        public bool UsePeerDns { get; set; }

        /// <summary>
        /// use-peer-ntp: Whether to accept the  NTP settings advertised by  DHCP Server. (Will override the settings put in the /system ntp client submenu)
        /// </summary>
        [RosProperty("use-peer-ntp", DefaultValue = "yes")]
        public bool UsePeerNtp { get; set; }

        /// <summary>
        /// address: IP address and netmask, which is assigned to DHCP Client from the Server
        /// </summary>
        [RosProperty("address",IsReadOnly = true)]
        public string Address { get; private set; }

        /// <summary>
        /// dhcp-server: IP address of the DHCP server.
        /// </summary>
        [RosProperty("dhcp-server",IsReadOnly = true)]
        public string DhcpServer { get; private set; }

        /// <summary>
        /// expires-after: Time when the lease expires (specified by the DHCP server).
        /// </summary>
        [RosProperty("expires-after",IsReadOnly = true)]
        public string ExpiresAfter { get; private set; }

        /// <summary>
        /// gateway: IP address of the gateway which is assigned by DHCP server
        /// </summary>
        [RosProperty("gateway",IsReadOnly = true)]
        public string Gateway { get; private set; }

        /// <summary>
        /// invalid: Shows whether configuration is invalid.
        /// </summary>
        [RosProperty("invalid",IsReadOnly = true)]
        public bool Invalid { get; private set; }

        /// <summary>
        /// netmask: 
        /// </summary>
        [RosProperty("netmask",IsReadOnly = true)]
        public string Netmask { get; private set; }

        /// <summary>
        /// primary-dns: IP address of the primary DNS server, assigned by the DHCP server
        /// </summary>
        [RosProperty("primary-dns",IsReadOnly = true)]
        public string PrimaryDns { get; private set; }

        /// <summary>
        /// primary-ntp: IP address of the primary NTP server, assigned by the DHCP server
        /// </summary>
        [RosProperty("primary-ntp",IsReadOnly = true)]
        public string PrimaryNtp { get; private set; }

        /// <summary>
        /// secondary-dns: IP address of the secondary DNS server, assigned by the DHCP server
        /// </summary>
        [RosProperty("secondary-dns",IsReadOnly = true)]
        public string SecondaryDns { get; private set; }

        /// <summary>
        /// secondary-ntp: IP address of the secondary NTP server, assigned by the DHCP server
        /// </summary>
        [RosProperty("secondary-ntp",IsReadOnly = true)]
        public string SecondaryNtp { get; private set; }

        /// <summary>
        /// status: Shows the status of DHCP Client
        /// </summary>
        [RosProperty("status",IsReadOnly = true)]
        public string Status { get; private set; }

        /// <summary>
        /// ctor
        /// </summary>
        public IpDhcpClient() {
            AddDefaultRoute = AddDefaultRouteType.Yes;
            UsePeerDns = true;
            UsePeerNtp = true;
        }

        /* TODO
        /// <summary>
        /// Release current binding and restart DHCP client
        /// </summary>
        public void Release(ITikConnection connection) {
            connection.CreateCommandAndParameters("ip/dhcp-client/release",
                TikSpecialProperties.Id, Id).ExecuteNonQuery();
        }

        /// <summary>
        /// Renew current leases. If the renew operation was not successful, client tries to reinitialize lease (i.e. it starts lease request procedure (rebind) as if it had not received an IP address yet)
        /// </summary>
        public void Renew(ITikConnection connection) {
            connection.CreateCommandAndParameters("ip/dhcp-client/renew",
                TikSpecialProperties.Id, Id).ExecuteNonQuery();
        }
        */

        /// Mode of adding default route. See <see cref="IpDhcpClient.AddDefaultRoute"/>.
        /// </summary>
        public enum AddDefaultRouteType {
            /// <summary>
            /// yes - adds classless route if received, if not then add default route (old behavior)
            /// </summary>
            [RosEnum("yes")]
            Yes,

            /// <summary>
            /// no
            /// </summary>
            [RosEnum("no")]
            No,

            /// <summary>
            /// adds both classless route if received and default route (MS style)
            /// </summary>
            [RosEnum("special-classless")]
            SpecialClassless,
        }
    }
}
