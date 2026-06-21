using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Snmp
{
    /// <summary>
    /// /snmp — Simple Network Management Protocol (SNMP) global configuration.
    /// SNMP is an Internet-standard protocol for managing devices on IP networks.
    /// This is a singleton settings entity; use <see cref="TikConnectionObjectExtensions.LoadSingle{T}"/> to load it.
    /// </summary>
    [TikEntity("/snmp", IsSingleton = true)]
    public class Snmp
    {
        /// <summary>enabled — whether the SNMP service is active.</summary>
        [TikProperty("enabled", DefaultValue = "no")]
        public bool Enabled { get; set; }

        /// <summary>contact — administrative contact information (free-form string).</summary>
        [TikProperty("contact", DefaultValue = "")]
        public string Contact { get; set; }

        /// <summary>location — physical location description (free-form string).</summary>
        [TikProperty("location", DefaultValue = "")]
        public string Location { get; set; }

        /// <summary>
        /// engine-id — the SNMPv3 engine identifier assigned by the router (read-only, auto-generated).
        /// </summary>
        [TikProperty("engine-id", IsReadOnly = true)]
        public string EngineId { get; private set; }

        /// <summary>
        /// engine-id-suffix — optional hex suffix appended to the auto-generated engine-id.
        /// Set to customise the SNMPv3 engine identifier.
        /// </summary>
        [TikProperty("engine-id-suffix", DefaultValue = "")]
        public string EngineIdSuffix { get; set; }

        /// <summary>src-address — source IP address used for SNMP responses and traps. Default <c>::</c> means auto-select.</summary>
        [TikProperty("src-address", DefaultValue = "::")]
        public string SrcAddress { get; set; }

        /// <summary>trap-target — comma-separated list of collector IP addresses that will receive SNMP traps.</summary>
        [TikProperty("trap-target", DefaultValue = "")]
        public string TrapTarget { get; set; }

        /// <summary>trap-community — SNMP community string used in outbound trap messages.</summary>
        [TikProperty("trap-community", DefaultValue = "public")]
        public string TrapCommunity { get; set; }

        /// <summary>
        /// trap-version — SNMP protocol version to use for outbound traps.
        /// </summary>
        /// <seealso cref="SnmpTrapVersion"/>
        [TikProperty("trap-version", DefaultValue = "1")]
        public SnmpTrapVersion TrapVersion { get; set; }

        /// <summary>
        /// trap-generators — comma-separated set of events that trigger SNMP traps.
        /// Stored as a string because multiple values can be active simultaneously
        /// (e.g. <c>interfaces,temp-exception</c>).
        /// Possible tokens: <c>interfaces</c>, <c>start-trap</c>, <c>temp-exception</c>.
        /// </summary>
        [TikProperty("trap-generators", DefaultValue = "")]
        public string TrapGenerators { get; set; }

        /// <summary>
        /// trap-interfaces — comma-separated list of interface names (or <c>all</c>) whose
        /// link-state changes generate traps. Requires <c>interfaces</c> in <see cref="TrapGenerators"/>.
        /// </summary>
        [TikProperty("trap-interfaces", DefaultValue = "")]
        public string TrapInterfaces { get; set; }

        /// <summary>
        /// vrf — Virtual Routing and Forwarding instance used by the SNMP service.
        /// Default is <c>main</c>.
        /// </summary>
        [TikProperty("vrf", DefaultValue = "main")]
        public string Vrf { get; set; }
    }

    /// <summary>SNMP protocol version used for outbound trap messages (<see cref="Snmp.TrapVersion"/>).</summary>
    public enum SnmpTrapVersion
    {
        /// <summary>SNMPv1 traps.</summary>
        [TikEnum("1")] V1,
        /// <summary>SNMPv2c traps.</summary>
        [TikEnum("2")] V2,
        /// <summary>SNMPv3 traps (requires SNMPv3 community configuration).</summary>
        [TikEnum("3")] V3,
    }
}
