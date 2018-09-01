using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface.Wireless
{
    /// <summary>
    /// interface/wireless/channels: Advanced Channels feature provides extended opportunities in wireless interface configuration:
    /// * scan-list that covers multiple bands and channel widths;
    /// * non-standard channel center frequencies(specified with KHz granularity) for hardware that allows that;
    /// * non-standard channel widths(specified with KHz granularity) for hardware that allows that.
    /// </summary>
    [TikEntity("interface/wireless/channels", IsSingleton = false)]
    public class WirelessChannels
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true)]
        public string Id { get; private set; }

        /// <summary>
        /// list: name of list this channel is part of. Lists can be used to group channels;
        /// </summary>
        [TikProperty("list")]
        public String List { get; set; }

        /// <summary>
        /// name: name by which this channel can be referred to. If name is not specified when adding channel, it will be automatically generated from channel frequency and width;
        /// </summary>
        [TikProperty("name")]
        public String Name { get; set; }

        /// <summary>
        /// frequency: channel center frequency in MHz, allowing to specify fractional MHz part, e.g. 5181.5;
        /// </summary>
        [TikProperty("frequency")]
        public String Frequency { get; set; }

        /// <summary>
        /// width: channel width in MHz, allowing to specify fractional MHz part, e.g. 14.5;
        /// </summary>
        [TikProperty("width")]
        public String Width { get; set; }

        /// <summary>
        /// band: defines default set of data rates when using this channel;
        /// </summary>
        [TikProperty("band")]
        public String Band { get; set; }

        /// <summary>
        /// extension-channel: specifies placement of 11n extension channel.
        /// </summary>
        [TikProperty("extension-channel", DefaultValue = "disabled")]
        public String ExtensionChannel { get; set; }
    }
}
