using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip
{
    /// <summary>
    /// /ip/address: IP addresses serve for a general host identification purposes in IP networks. Typical (IPv4) address consists of four octets. For proper addressing the router also needs the network mask value, id est which bits of the complete IP address refer to the address of the host, and which - to the address of the network. The network address value is calculated by binary AND operation from network mask and IP address values. It's also possible to specify IP address followed by slash "/" and the amount of bits that form the network address. 
    /// </summary>
    [TikEntity("/ip/address", IncludeDetails = true)]
    public class IpAddress
    {
        /// <summary>
        /// .id: the router's identifier of the row.
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// actual-interface: the interface the address is actually bound to, e.g. the bridge when <c>interface</c> is one of its ports.
        /// </summary>
        [TikProperty("actual-interface", IsReadOnly = true)]
        public TikField<string?> ActualInterface { get; private set; }

        /// <summary>
        /// address: IP address
        /// </summary>
        [TikProperty("address", WinboxLabel = "Address")]
        public TikField<string?> Address { get; set; }

        /// <summary>
        /// interface: Interface name the IP address is assigned to
        /// </summary>
        [TikProperty("interface", WinboxLabel = "Interface")]
        public TikField<string?> Interface { get; set; }

        /// <summary>
        /// broadcast: the broadcast address of the network, calculated from the address and the network mask unless set. <c>set</c> accepts it; a read does not return it.
        /// </summary>
        [TikProperty("broadcast", DefaultValue = "255.255.255.255")]
        public TikField<string?> Broadcast { get; set; }

        /// <summary>
        /// network: the network address, calculated from the address and the network mask; for a point-to-point (/32) address it is set to the address of the remote end.
        /// </summary>
        [TikProperty("network" , WinboxLabel = "Network")]
        public TikField<string?> Network { get; set; }

        /// <summary>
        /// netmask: the mask that separates the network part of the address from the host part. <c>set</c> accepts it; a read does not return it (the prefix length is part of <c>address</c>).
        /// </summary>
        [TikProperty("netmask")]
        public TikField<string?> Netmask { get; set; }

        /// <summary>
        /// comment: free-text note on the row.
        /// </summary>
        [TikProperty("comment")]
        public TikField<string?> Comment { get; set; }

        /// <summary>
        /// disabled: whether the address is disabled (configured but not in use).
        /// </summary>
        [TikProperty("disabled")]
        public TikField<bool?> Disabled { get; set; }

        /// <summary>
        /// dynamic: whether the address was added by a service (DHCP client, PPP, ...) rather than configured, and is removed with it.
        /// </summary>
        [TikProperty("dynamic", IsReadOnly = true)]
        public TikField<bool?> Dynamic { get; private set; }

        /// <summary>
        /// invalid: whether the address cannot be used, e.g. because its interface does not exist.
        /// </summary>
        [TikProperty("invalid", IsReadOnly = true)]
        public TikField<bool?> Invalid { get; private set; }
    }
}
