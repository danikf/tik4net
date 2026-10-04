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
        /// Row .id property.
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// Row actual-interface property.
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
        /// broadcast: Broadcasting IP address, calculated by default from an IP address and a network mask. Starting from v5RC6 this parameter is removed
        /// </summary>
        [TikProperty("broadcast", DefaultValue = "255.255.255.255")]
        public TikField<string?> Broadcast { get; set; }

        /// <summary>
        /// network: IP address for the network. For point-to-point links it should be the address of the remote end. Starting from v5RC6 this parameter is configurable only for addresses with /32 netmask (point to point links)
        /// </summary>
        [TikProperty("network" , WinboxLabel = "Network")]
        public TikField<string?> Network { get; set; }

        /// <summary>
        /// netmask: Delimits network address part of the IP address from the host part
        /// </summary>
        [TikProperty("netmask")]
        public TikField<string?> Netmask { get; set; }

        /// <summary>
        /// Row comment property.
        /// </summary>
        [TikProperty("comment")]
        public TikField<string?> Comment { get; set; }

        /// <summary>
        /// Row disabled property.
        /// </summary>
        [TikProperty("disabled")]
        public TikField<bool?> Disabled { get; set; }

        /// <summary>
        /// Row dynamic property.
        /// </summary>
        [TikProperty("dynamic", IsReadOnly = true)]
        public TikField<bool?> Dynamic { get; set; }

        /// <summary>
        /// Row invalid property.
        /// </summary>
        [TikProperty("invalid", IsReadOnly = true)]
        public TikField<bool?> Invalid { get; set; }
    }
}
