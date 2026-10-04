using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface
{
    /// <summary>
    /// Wireguard Interface
    /// </summary>
    [TikEntity("/interface/wireguard")]
    public class InterfaceWireguard
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// Name of the wireguard interface
        /// </summary>
        [TikProperty("name", WinboxLabel = "Name")]
        public TikField<string?> Name { get; set; }

        /// <summary>
        /// comment: Short description of the Peer.
        /// </summary>
        [TikProperty("comment")]
        public TikField<string?> Comment { get; set; }

        /// <summary>
        /// disabled: Whether peer will be used.
        /// </summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public TikField<bool?> Disabled { get; set; }

        /// <summary>
        /// mtu: Layer3 Maximum transmission unit
        /// 
        /// integer [0..65536]
        /// </summary>
        [TikProperty("mtu", DefaultValue = "1420", WinboxLabel = "MTU")]
        public TikField<int?> /*integer [0..65536]*/ Mtu { get; set; }

        /// <summary>
        /// The private key associated with the local device
        /// </summary>
        [TikProperty("private-key", IsSensitive = true, WinboxLabel = "Private Key")]
        public TikField<string?> PrivateKey { get; set; }

        /// <summary>
        /// Interface listen port
        /// </summary>
        [TikProperty("listen-port", DefaultValue = "13231", WinboxLabel = "Listen Port")]
        public TikField<int?> ListenPort { get; set; }
    }
}
