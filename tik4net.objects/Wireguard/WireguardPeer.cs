using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Wireguard
{
    /// <summary>
    /// Specific remote entity or device with which the local device establishes a secure communication tunnel
    /// </summary>
    [TikEntity("/interface/wireguard/peers")]
    public class WireguardPeer
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// IP addresses or subnets that are allowed to communicate with that peer
        /// </summary>
        [TikProperty("allowed-address")]
        public string AllowedAddress { get; set; }

        /// <summary>
        /// comment: Short description of the Peer.
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>
        /// disabled: Whether peer will be used.
        /// </summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>
        /// Specifies the local network interface that the peer is associated with
        /// </summary>
        [TikProperty("interface", DefaultValue = "")]
        public string Interface { get; set; }

        /// <summary>
        /// shared secret cryptographic key that is preconfigured between two peers
        /// </summary>
        [TikProperty("preshared-key", DefaultValue = "")]
        public string PresharedKey { get; set; }

        /// <summary>
        /// The IP address and port number of the remote endpoint or server that the peer will connect to.
        /// </summary>
        [TikProperty("endpoint-address")]
        public string EndpointAddress { get; set; }

        /// <summary>
        /// Specifies the specific port number on the remote endpoint or server that the peer will connect to.
        /// </summary>
        [TikProperty("endpoint-port")]
        public int EndpointPort { get; set; }

        /// <summary>
        /// The public key associated with the remote peer
        /// </summary>
        [TikProperty("public-key" )]
        public string PublicKey { get; set; }
    }
}
