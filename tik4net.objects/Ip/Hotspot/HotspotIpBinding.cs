using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.Hotspot
{
    /// <summary>
    /// ip/hotspot/ip-binding
    /// 
    /// IP-Binding HotSpot menu allows to setup static One-to-One NAT translations, allows to bypass specific HotSpot clients without any authentication, and also allows to block specific hosts and subnets from HotSpot network 
    /// </summary>
    [TikEntity("ip/hotspot/ip-binding")]
    public class HotspotIpBinding
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// address: The original IP address of the client
        /// </summary>
        [TikProperty("address", DefaultValue = "")]
        public string/*IP Range*/ Address { get; set; }

        /// <summary>
        /// mac-address: MAC address of the client
        /// </summary>
        [TikProperty("mac-address", DefaultValue = "")]
        public string/*MAC*/ MacAddress { get; set; }

        /// <summary>
        /// server
        /// Name of the HotSpot server.
        ///  all - will be applied to all hotspot servers
        /// </summary>
        [TikProperty("server", DefaultValue = "all")]
        public string/*string | all*/ Server { get; set; }

        /// <summary>
        /// to-address: New IP address of the client, translation occurs on the router (client does not know anything about the translation)
        /// </summary>
        [TikProperty("to-address", DefaultValue = "")]
        public string/*IP*/ ToAddress { get; set; }

        /// <summary>
        /// type
        /// Type of the IP-binding action
        ///  regular - performs One-to-One NAT according to the rule, translates address to to-address
        ///  bypassed - performs the translation, but excludes client from login to the HotSpot
        ///  blocked - translation is not performed and packets from host are dropped
        /// </summary>
        [TikProperty("type", DefaultValue = "")]
        public string/*blocked | bypassed | regular*/ Type { get; set; }
    }
}
