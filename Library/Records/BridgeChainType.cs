namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// Firewall chain type
    /// </summary>
    /// <seealso cref="BridgeFirewallBase.Chain"/>
    public static class BridgeChainType {
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
