using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface.Wireless
{
    /// <summary>
    /// interface/wireless/sniffer: Wireless sniffer allows to capture frames including Radio header, 802.11 header and other wireless related information. 
    /// </summary>
    [TikEntity("interface/wireless/sniffer", IsSingleton = true)]
    public class WirelessSniffer
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true)]
        public string Id { get; private set; }

        /// <summary>
        /// streaming-enabled: Whether to stream captured data to specified streaming server
        /// </summary>
        [TikProperty("streaming-enabled", DefaultValue = "yes")]
        public bool StreamingEnabled { get; set; }

        /// <summary>
        /// streaming-server: IP address of the streaming server.
        /// </summary>
        [TikProperty("streaming-server")]
        public string StreamingServer { get; set; }

        /// <summary>
        /// multiple-channels
        /// </summary>
        [TikProperty("multiple-channels", DefaultValue = "yes")]
        public bool MultipleChannels { get; set; }

        /// <summary>
        /// channel-time: Default: 200ms
        /// </summary>
        [TikProperty("channel-time", DefaultValue = "200")]
        public string ChannelTime { get; set; }
    }
}
