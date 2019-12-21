using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.Firewall
{
    /// <summary>
    /// /ip/firewall/address-list
    /// </summary>
    [TikEntity("/ip/firewall/service-port")]
    public class FirewalServicePort
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// name
        /// </summary>
        [TikProperty("name")]
        public string Name { get; private set; }

        /// <summary>
        /// name
        /// </summary>
        [TikProperty("ports")]
        public string Ports { get; set; }

        /// <summary>
        /// disabled
        /// </summary>
        [TikProperty("disabled")]
        public bool Disabled { get; set; }
    }
}
