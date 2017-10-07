using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface.Bridge
{
    /// <summary>
    /// Firewall chain type - <see cref="BridgeFirewallBase.Chain"/>
    /// </summary>
    /// <seealso cref="BridgeFirewallBase.Chain"/>
    public static class BridgeFirewallChainType
    {
        /// <summary>
        /// input - used to process packets entering the router through one of the interfaces with the destination IP address which is one of the router's addresses. Packets passing through the router are not processed against the rules of the input chain
        /// </summary>
        public const string Input = "input";

        /// <summary>
        /// forward - used to process packets passing through the router
        /// </summary>
        public const string Forward = "forward";

        /// <summary>
        ///  output - used to process packets originated from the router and leaving it through one of the interfaces.Packets passing through the router are not processed against the rules of the output chain
        /// </summary>
        public const string Output = "output";
    }
}
