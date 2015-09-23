using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        #region Submenu classes
        /// <summary>
        /// This sub-menu allows the configuration of how often the DHCP leases will be stored on disk. If they would be saved on disk on every lease change, a lot of disk writes would happen which is very bad for Compact Flash (especially, if lease times are very short). To minimize writes on disk, all changes are saved on disk every store-leases-disk seconds. Additionally leases are always stored on disk on graceful shutdown and reboot. 
        /// </summary>
        [TikEntity("ip/dhcp-server/config", IsSingleton = true)]
        public class DhcpServerConfig
        {
            /// <summary>
            /// Values for <see cref="StoreLeasesDisk"/> (or use specific time)
            /// </summary>
            public static class StoreLeasesDiskType
            {
                /// <summary>
                /// never
                /// </summary>
                public const string Immediately = "never";
                /// <summary>
                /// never
                /// </summary>
                public const string Never = "never";
            }

            /// <summary>
            /// store-leases-disk - How frequently lease changes should be stored on disk
            /// </summary>
            /// <seealso cref="StoreLeasesDiskType"/>
            [TikProperty("store-leases-disk")]
            public string StoreLeasesDisk { get; set; }
        }

        /// <summary>
        /// ip/dhcp-server/network : 
        /// </summary>
        [TikEntity("ip/dhcp-server/network")]
        public class DhcpServerNetwork
        {
            /// <summary>
            /// .id: primary key of row
            /// </summary>
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string Id { get; private set; }

            /// <summary>
            /// address: the network DHCP server(s) will lease addresses from
            /// </summary>
            [TikProperty("address")]
            public string/*IP/netmask*/ Address { get; set; }

            /// <summary>
            /// boot-file-name: Boot file name
            /// </summary>
            [TikProperty("boot-file-name")]
            public string BootFileName { get; set; }

            /// <summary>
            /// caps-manager: Comma-separated list of IP addresses for one or more CAPsMan system managers.
            /// </summary>
            [TikProperty("caps-manager")]
            public string CapsManager { get; set; }

            /// <summary>
            /// dhcp-option: Add additional DHCP options from  option list.
            /// </summary>
            [TikProperty("dhcp-option")]
            public string DhcpOption { get; set; }

            /// <summary>
            /// dns-server: the DHCP client will use these as the default DNS servers. Two comma-separated DNS servers can be specified to be used by the DHCP client as primary and secondary DNS servers
            /// </summary>
            [TikProperty("dns-server")]
            public string DnsServer { get; set; }

            /// <summary>
            /// domain: The DHCP client will use this as the 'DNS domain' setting for the network adapter.
            /// </summary>
            [TikProperty("domain")]
            public string Domain { get; set; }

            /// <summary>
            /// gateway: The default gateway to be used by DHCP Client.
            /// </summary>
            [TikProperty("gateway", DefaultValue = "0.0.0.0")]
            public string/*IP*/ Gateway { get; set; }

            /// <summary>
            /// netmask: The actual network mask to be used by DHCP client. If set to '0' - netmask from network address will be used.
            /// </summary>
            [TikProperty("netmask", DefaultValue = "0")]
            public string/*integer: 0..32*/ Netmask { get; set; }

            /// <summary>
            /// next-server: IP address of next server to use in bootstrap.
            /// </summary>
            [TikProperty("next-server")]
            public string/*IP*/ NextServer { get; set; }

            /// <summary>
            /// ntp-server: the DHCP client will use these as the default NTP servers. Two comma-separated NTP servers can be specified to be used by the DHCP client as primary and secondary NTP servers
            /// </summary>
            [TikProperty("ntp-server")]
            public string/*IP*/ NtpServer { get; set; }

            /// <summary>
            /// wins-server: The Windows DHCP client will use these as the default WINS servers. Two comma-separated WINS servers can be specified to be used by the DHCP client as primary and secondary WINS servers
            /// </summary>
            [TikProperty("wins-server")]
            public string/*IP*/ WinsServer { get; set; }

            /// <summary>
            /// comment: Short description of the client
            /// </summary>
            [TikProperty("comment")]
            public string Comment { get; set; }
        }

        /// <summary>
        ///  DHCP server lease submenu is used to monitor and manage server's leases. The issued leases are showed here as dynamic entries. You can also add static leases to issue a specific IP address to a particular client (identified by MAC address) .
        /// 
        /// Generally, the DHCP lease it allocated as follows:
        /// 
        ///     an unused lease is in waiting state
        ///     if a client asks for an IP address, the server chooses one
        ///     if the client receives a statically assigned address, the lease becomes offered, and then bound with the respective lease time
        ///     if the client receives a dynamic address (taken from an IP address pool), the router sends a ping packet and waits for answer for 0.5 seconds. During this time, the lease is marked testing
        ///     in the case where the address does not respond, the lease becomes offered and then bound with the respective lease time
        ///     in other case, the lease becomes busy for the lease time (there is a command to retest all busy addresses), and the client's request remains unanswered (the client will try again shortly) 
        /// 
        /// A client may free the leased address. The dynamic lease is removed, and the allocated address is returned to the address pool. But the static lease becomes busy until the client reacquires the address. 
        /// </summary>
        [TikEntity("ip/dhcp-server/lease")]
        public class DhcpServerLease
        {
            /// <summary>
            /// .id: primary key of row
            /// </summary>
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string Id { get; private set; }

            /// <summary>
            /// address: Specify IP address (or ip pool) for static lease. If set to 0.0.0.0 - pool from server will be used
            /// </summary>
            [TikProperty("address")]
            public string/*IP*/ Address { get; set; }

            /// <summary>
            /// address-list: Address list to which address will be added if lease is bound.
            /// </summary>
            [TikProperty("address-list")]
            public string AddressList { get; set; }

            /// <summary>
            /// always-broadcast: Send all replies as broadcasts
            /// </summary>
            [TikProperty("always-broadcast")]
            public bool AlwaysBroadcast { get; set; }

            /// <summary>
            /// block-access: Block access for this client
            /// </summary>
            [TikProperty("block-access", DefaultValue = "no")]
            public bool BlockAccess { get; set; }

            /// <summary>
            /// client-id: If specified, must match DHCP 'client identifier' option of the request
            /// </summary>
            [TikProperty("client-id")]
            public string ClientId { get; set; }

            /// <summary>
            /// lease-time: Time that the client may use the address. If set to 0s lease will never expire.
            /// </summary>
            [TikProperty("lease-time", DefaultValue = "0s")]
            public string/*time*/ LeaseTime { get; set; }

            /// <summary>
            /// mac-address: If specified, must match the MAC address of the client
            /// </summary>
            [TikProperty("mac-address", DefaultValue = "00:00:00:00:00:00")]
            public string/*MAC*/ MacAddress { get; set; }

            /// <summary>
            /// src-mac-address: Source MAC address
            /// </summary>
            [TikProperty("src-mac-address")]
            public string/*MAC*/ SrcMacAddress { get; set; }

            /// <summary>
            /// use-src-mac: Use this source MAC address instead
            /// </summary>
            [TikProperty("use-src-mac")]
            public string/*MAC*/ UseSrcMac { get; set; }

            /// <summary>
            /// active-address: Actual IP address for this lease
            /// </summary>
            [TikProperty("active-address", IsReadOnly = true)]
            public string ActiveAddress { get; private set; }

            /// <summary>
            /// active-client-id: Actual client-id of the client
            /// </summary>
            [TikProperty("active-client-id", IsReadOnly = true)]
            public string ActiveClientId { get; private set; }

            /// <summary>
            /// active-mac-address: Actual MAC address of the client
            /// </summary>
            [TikProperty("active-mac-address", IsReadOnly = true)]
            public string ActiveMacAddress { get; private set; }

            /// <summary>
            /// active-server: Actual dhcp server, which serves this client
            /// </summary>
            [TikProperty("active-server", IsReadOnly = true)]
            public string ActiveServer { get; private set; }

            /// <summary>
            /// agent-circuit-id: Circuit ID of DHCP relay agent. If each character should be valid ASCII text symbol or else this value is displayed as hex dump.
            /// </summary>
            [TikProperty("agent-circuit-id", IsReadOnly = true)]
            public string AgentCircuitId { get; private set; }

            /// <summary>
            /// agent-remote-id: Remote ID, set by DHCP relay agent
            /// </summary>
            [TikProperty("agent-remote-id", IsReadOnly = true)]
            public string AgentRemoteId { get; private set; }

            /// <summary>
            /// blocked: Whether the lease is blocked
            /// </summary>
            [TikProperty("blocked", IsReadOnly = true)]
            public string Blocked { get; private set; }

            /// <summary>
            /// expires-after: Time until lease expires
            /// </summary>
            [TikProperty("expires-after", IsReadOnly = true)]
            public string ExpiresAfter { get; private set; }

            /// <summary>
            /// host-name: Shows host name option from last received DHCP request
            /// </summary>
            [TikProperty("host-name", IsReadOnly = true)]
            public string HostName { get; private set; }

            /// <summary>
            /// radius: Shows if this dynamic lease is authenticated by RADIUS or not
            /// </summary>
            [TikProperty("radius", IsReadOnly = true)]
            public bool Radius { get; private set; }

            /// <summary>
            /// rate-limit: Sets rate limit for active lease. Format is: rx-rate[/tx-rate] [rx-burst-rate[/tx-burst-rate] [rx-burst-threshold[/tx-burst-threshold] [rx-burst-time[/tx-burst-time]]]]. All rates should be numbers with optional 'k' (1,000s) or 'M' (1,000,000s). If tx-rate is not specified, rx-rate is as tx-rate too. Same goes for tx-burst-rate and tx-burst-threshold and tx-burst-time. If both rx-burst-threshold and tx-burst-threshold are not specified (but burst-rate is specified), rx-rate and tx-rate is used as burst thresholds. If both rx-burst-time and tx-burst-time are not specified, 1s is used as default
            /// </summary>
            [TikProperty("rate-limit", IsReadOnly = true)]
            public string RateLimit { get; private set; }

            /// <summary>
            /// server: Server name which serves this client
            /// </summary>
            [TikProperty("server", IsReadOnly = true)]
            public string Server { get; private set; }

            /// <summary>
            /// status
            /// Lease status:
            ///        
            ///               waiting - un-used static lease
            ///               testing - testing whether this address is used or not (only for dynamic leases) by pinging it with timeout of 0.5s 
            ///               authorizing - waiting for response from radius server 
            ///               busy - this address is assigned statically to a client or already exists in the network, so it can not be leased 
            ///               offered - server has offered this lease to a client, but did not receive confirmation from the client 
            ///               bound - server has received client's confirmation that it accepts offered address, it is using it now and will free the address no later than the lease time 
            ///        
            ///     
            /// </summary>
            [TikProperty("status", IsReadOnly = true)]
            public string Status { get; private set; }

            /// <summary>
            /// disabled: 
            /// </summary>
            [TikProperty("disabled")]
            public bool Disabled { get; set; }

            /// <summary>
            /// comment: Short description of the client
            /// </summary>
            [TikProperty("comment")]
            public string Comment { get; set; }

            /// <summary>
            /// Check status of a given busy dynamic lease, and free it in case of no response
            /// </summary>
            public void CheckStatus(ITikConnection connection)
            {
                connection.CreateCommandAndParameters("ip/dhcp-server/lease/check-status",
                    TikSpecialProperties.Id, Id).ExecuteNonQuery();
            }

            /// <summary>
            /// Convert a dynamic lease to a static one
            /// </summary>
            public void MakeStatic(ITikConnection connection)
            {
                connection.CreateCommandAndParameters("ip/dhcp-server/lease/make-static",
                    TikSpecialProperties.Id, Id).ExecuteNonQuery();
            }
        }

        /// <summary>
        ///  To find any rogue DHCP servers as soon as they appear in your network, DHCP Alert tool can be used. It will monitor the ethernet interface for all DHCP replies and check if this reply comes from a valid DHCP server. If a reply from an unknown DHCP server is detected, alert gets triggered:
        /// 
        /// When the system alerts about a rogue DHCP server, it can execute a custom script.
        /// 
        /// As DHCP replies can be unicast, the 'rogue dhcp detector' may not receive any offer to other dhcp clients at all. To deal with this, the rogue dhcp detector acts as a dhcp client as well - it sends out dhcp discover requests once a minute 
        /// </summary>
        [TikEntity("ip/dhcp-server/alert")]
        public class DhcpServerAlert
        {
            /// <summary>
            /// .id: primary key of row
            /// </summary>
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string Id { get; private set; }

            /// <summary>
            /// alert-timeout: Time after which alert will be forgotten. If after that time the same server is detected, new alert will be generated. If set to none timeout will never expire.
            /// </summary>
            [TikProperty("alert-timeout", DefaultValue = "none")]
            public string/*none | time*/ AlertTimeout { get; set; }

            /// <summary>
            /// interface: Interface, on which to run rogue DHCP server finder.
            /// </summary>
            [TikProperty("interface")]
            public string Interface { get; set; }

            /// <summary>
            /// on-alert: Script to run, when an unknown DHCP server is detected.
            /// </summary>
            [TikProperty("on-alert")]
            public string OnAlert { get; set; }

            /// <summary>
            /// valid-server: List of MAC addresses of valid DHCP servers.
            /// </summary>
            [TikProperty("valid-server")]
            public string ValidServer { get; set; }

            /// <summary>
            /// unknown-server: List of MAC addresses of detected unknown DHCP servers. Server is removed from this list after alert-timeout
            /// </summary>
            [TikProperty("unknown-server", IsReadOnly = true)]
            public string UnknownServer { get; private set; }

            /// <summary>
            /// Convert a dynamic lease to a static one
            /// </summary>
            public void ResetAlert(ITikConnection connection)
            {
                connection.CreateCommandAndParameters("ip/dhcp-server/alert/reset-alert",
                    TikSpecialProperties.Id, Id).ExecuteNonQuery();
            }
        }

        /// <summary>
        /// ip/dhcp-server/option :  With help of DHCP Option list, it is possible to define additional custom options for DHCP Server to advertise.
        ///        According to the DHCP protocol, a parameter is returned to the DHCP client only if it requests this parameter, specifying the respective code in DHCP request Parameter-List(code 55) attribute.If the code is not included in Parameter-List attribute, DHCP server will not send it to the DHCP client.
        /// </summary>
        [TikEntity("ip/dhcp-server/option")]
        public class DhcpServerOption
        {
            /// <summary>
            /// .id: primary key of row
            /// </summary>
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string Id { get; private set; }

            /// <summary>
            /// code: dhcp option code. All codes are available at http://www.iana.org/assignments/bootp-dhcp-parameters
            /// </summary>
            [TikProperty("code")]
            public string/*integer:1..254*/ Code { get; set; }

            /// <summary>
            /// name: Descriptive name of the option
            /// </summary>
            [TikProperty("name", IsMandatory = true)]
            public string Name { get; set; }

            /// <summary>
            /// value
            /// Parameter's value.
            /// Starting from v6.8 available data types for options are:
            ///  0xXXXX - hex string (works also in v5)
            ///  'XXXXX' - string (works also in v5 but without ' ' around the text)
            ///  $(XXXXX) - variable (currently there are no variables for server)
            ///  '10.10.10.10' - IP address
            ///  s'10.10.10.10' - IP address converted to string
            ///  '10' - decimal number
            ///  s'10' - decimal number converted to string
            /// Now it is also possible to combine data types into one, for example:
            /// "0x01'vards'$(HOSTNAME)"
            /// For example if HOSTNAME is 'kvm', then raw value will be 0x0176617264736b766d
            /// </summary>
            [TikProperty("value")]
            public string Value { get; set; }

            /// <summary>
            /// raw-value: Read only field which shows raw dhcp option value (the format actually sent out)
            /// </summary>
            [TikProperty("raw-value")]
            public string/*HEX string */ RawValue { get; set; }
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
