using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip
{
    /// <summary>
    /// ip/dhcp server
    /// The DHCP (Dynamic Host Configuration Protocol) is used for the easy distribution of IP addresses in a network. The MikroTik RouterOS implementation includes both server and client parts and is compliant with RFC 2131.
    /// 
    /// The router supports an individual server for each Ethernet-like interface. The MikroTik RouterOS DHCP server supports the basic functions of giving each requesting client an IP address/netmask lease, default gateway, domain name, DNS-server(s) and WINS-server(s) (for Windows clients) information (set up in the DHCP networks submenu)
    /// 
    /// In order for the DHCP server to work, IP pools must also be configured (do not include the DHCP server's own IP address into the pool range) and the DHCP networks.
    /// 
    /// It is also possible to hand out leases for DHCP clients using the RADIUS server; the supported parameters for a RADIUS server is as follows:
    /// </summary>
    /// <remarks>
    /// Note: DHCP server requires a real interface to receive raw ethernet packets. If the interface is a Bridge interface, then the Bridge must have a real interface attached as a port to that bridge which will receive the raw ethernet packets. It cannot function correctly on a dummy (empty bridge) interface. 
    /// </remarks>
    [TikEntity("ip/dhcp-server")]
    public class IpDhcpServer
    {
        #region Submenu classes - OBSOLETE
        /// <summary>
        /// Obsolete: use DhcpServer.DhcpServerConfig class.
        /// </summary>
        [Obsolete("use DhcpServer.DhcpServerConfig class.", true)]
        public abstract class DhcpServerConfig
        {
        }

        /// <summary>
        /// Obsolete: use DhcpServer.DhcpServerNetwork class.
        /// </summary>
        [Obsolete("use DhcpServer.DhcpServerNetwork class.", true)]
        public abstract class DhcpServerNetwork
        {

        }

        /// <summary>
        /// Obsolete: use DhcpServer.DhcpServerLease class.
        /// </summary>
        [Obsolete("use DhcpServer.DhcpServerLease class.", true)]
        public abstract class DhcpServerLease
        {

        }

        /// <summary>
        /// Obsolete: use DhcpServer.DhcpServerAlert class.
        /// </summary>
        [Obsolete("use DhcpServer.DhcpServerAlert class.", true)]
        public abstract class DhcpServerAlert
        {

        }

        /// <summary>
        /// Obsolete: use DhcpServer.DhcpServerOption class.
        /// </summary>
        [Obsolete("use DhcpServer.DhcpServerOption class.", true)]
        public abstract class DhcpServerOption
        {

        }

        #endregion

        #region -- Enums --
        /// <summary>
        /// Type of <see cref="Authoritative"/>.
        /// </summary>
        public enum AuthoritativeType
        {
            /// <summary>
            /// after-2sec-delay - requests with "secs &lt; 2" will be processed as in "no" setting case and requests with "secs &gt;= 2" will be processed as in "yes" case.
            /// </summary>
            [TikEnum("after-2sec-delay")]
            After2secDelay,
            /// <summary>
            /// yes - replies to clients request for an address that is not available from this server, dhcp server will send negative acknowledgment (DHCPNAK) 
            /// </summary>
            [TikEnum("yes")]
            Yes,
            /// <summary>
            /// no - dhcp server ignores clients requests for addresses that are not available from this server
            /// </summary>
            [TikEnum("no")]
            No,
            /// <summary>
            /// after-10sec-delay - requests with "secs &lt; 10" will be processed as in "no" setting case and requests with "secs &gt;= 10" will be processed as in "yes" case.
            /// </summary>
            [TikEnum("after-10sec-delay")]
            After10secDelay,
        }

        /// <summary>
        /// Types of <see cref="BootpSupport"/>.
        /// </summary>
        public enum BootpSupportType
        {
            /// <summary>
            /// static - offer only static leases to BOOTP clients 
            /// </summary>
            [TikEnum("static")]
            Static,
            /// <summary>
            /// none - do not respond to BOOTP requests 
            /// </summary>
            [TikEnum("none")]
            None,
            /// <summary>
            /// dynamic - offer static and dynamic leases for BOOTP clients
            /// </summary>
            [TikEnum("dynamic")]
            Dynamic,
        }
        #endregion

        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// add-arp: Whether to add dynamic ARP entry.  If set to no either  ARP mode should be enabled on that interface or static  ARP entries should be administratively defined in /ip arp submenu.
        /// </summary>
        [TikProperty("add-arp", DefaultValue = "no")]
        public bool AddArp { get; set; }

        /// <summary>
        /// address-pool:  IP pool, from which to take IP addresses for the clients. If set to static-only, then only the clients that have a static lease (added in  lease submenu) will be allowed.
        /// </summary>
        [TikProperty("address-pool", DefaultValue = "static-only")]
        public string/*string | static-only*/ AddressPool { get; set; }

        /// <summary>
        /// always-broadcast: Always send replies as broadcasts.
        /// </summary>
        [TikProperty("always-broadcast", DefaultValue = "no")]
        public bool AlwaysBroadcast { get; set; }                                    

        /// <summary>
        /// authoritative
        /// Option changes the way how server responds to DHCP requests:
        ///  yes - replies to clients request for an address that is not available from this server, dhcp server will send negative acknowledgment (DHCPNAK) 
        ///  no - dhcp server ignores clients requests for addresses that are not available from this server 
        ///  after-10sec-delay - requests with "secs &lt; 10" will be processed as in "no" setting case and requests with "secs &gt;= 10" will be processed as in "yes" case.
        ///  after-2sec-delay - requests with "secs &lt; 2" will be processed as in "no" setting case and requests with "secs &gt;= 2" will be processed as in "yes" case.
        /// If all requests with "secs &lt; x" should be ignored, then delay-threshold=x setting should be used.
        /// </summary>
        [TikProperty("authoritative", DefaultValue = "after-2sec-delay")]
        public AuthoritativeType Authoritative { get; set; }

        /// <summary>
        /// bootp-support
        /// Support for BOOTP clients:
        ///  none - do not respond to BOOTP requests 
        ///  static - offer only static leases to BOOTP clients 
        ///  dynamic - offer static and dynamic leases for BOOTP clients
        /// </summary>
        [TikProperty("bootp-support", DefaultValue = "static")]
        public BootpSupportType BootpSupport { get; set; }

        /// <summary>
        /// delay-threshold: If secs field in DHCP packet is smaller than delay-threshold, then this packet is ignored. If set to none - there is no threshold (all DHCP packets are processed)
        /// </summary>
        [TikProperty("delay-threshold", DefaultValue = "none")]
        public string/*time | none*/ DelayThreshold { get; set; }

        /// <summary>
        /// interface: Interface on which server will be running.
        /// </summary>
        [TikProperty("interface")]
        public string Interface { get; set; }

        /// <summary>
        /// lease-script
        /// Script that will be executed after lease is assigned or de-assigned. Internal "global" variables that can be used in the script:
        ///  leaseBound - set to "1" if bound, otherwise set to "0"
        ///  leaseServerName -  dhcp server name
        ///  leaseActMAC -  active mac address
        ///  leaseActIP -  active IP address
        /// </summary>
        [TikProperty("lease-script")]
        public string LeaseScript { get; set; }

        /// <summary>
        /// lease-time: The time that a client may use the assigned address. The client will try to renew this address after a half of this time and will request a new address after time limit expires.
        /// </summary>
        [TikProperty("lease-time", DefaultValue = "72h")]
        public string/*time*/ LeaseTime { get; set; }

        /// <summary>
        /// name: Reference name
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// relay
        /// The IP address of the relay this DHCP server should process requests from:
        ///  0.0.0.0 - the DHCP server will be used only for direct requests from clients (no DHCP really allowed) 
        ///  255.255.255.255 - the DHCP server should be used for any incoming request from a DHCP relay except for those, which are processed by another DHCP server that exists in the /ip dhcp-server submenu.
        /// </summary>
        [TikProperty("relay", DefaultValue = "0.0.0.0")]
        public string/*IP*/ Relay { get; set; }

        /// <summary>
        /// src-address: The address which the DHCP client must send requests to in order to renew an IP address lease. If there is only one static address on the DHCP server interface and the source-address is left as 0.0.0.0, then the static address will be used. If there are multiple addresses on the interface, an address in the same subnet as the range of given addresses should be used.
        /// </summary>
        [TikProperty("src-address", DefaultValue = "0.0.0.0")]
        public string/*IP*/ SrcAddress { get; set; }

        /// <summary>
        /// use-radius: Whether to use RADIUS server for dynamic leases
        /// </summary>
        [TikProperty("use-radius", DefaultValue = "no")]
        public bool UseRadius { get; set; }

        /// <summary>
        /// disabled: Whether DHCP server is disabled or not
        /// </summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>
        /// ctor
        /// </summary>
        public IpDhcpServer()
        {
            AddressPool = "static-only";
            Authoritative = AuthoritativeType.After2secDelay;
            BootpSupport = BootpSupportType.Static;
        }
    }

}
