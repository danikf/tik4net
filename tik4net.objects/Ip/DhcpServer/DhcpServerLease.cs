using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.DhcpServer
{
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
        /// lease-time: Time that the client may use the address. If set to TimeSpan.Min lease will never expire.
        /// </summary>
        [TikProperty("lease-time", DefaultValue = "0s")]
        public TimeSpan/*time*/ LeaseTime { get; set; }

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
        public TimeSpan ExpiresAfter { get; private set; }

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
}
