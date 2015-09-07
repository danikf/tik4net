using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net.Objects.Ip
{
    /// <summary>
    /// /ip/address
    /// </summary>
    [TikEntity("/ip/address", IncludeDetails = true)]
    public class IpAddress
    {
        /// <summary>
        /// Row .id property.
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// Row actual-interface property.
        /// </summary>
        [TikProperty("actual-interface", IsReadOnly = true)]
        public string ActualInterface { get; private set; }

        /// <summary>
        /// Row address property.
        /// </summary>
        [TikProperty("address", IsMandatory = true)]
        public string Address { get; set; }

        /// <summary>
        /// Row interface property.
        /// </summary>
        [TikProperty("interface", IsMandatory = true)]
        public string Interface { get; set; }

        /// <summary>
        /// Row broadcast property.
        /// </summary>
        [TikProperty("broadcast")]
        public string Broadcast { get; set; }

        /// <summary>
        /// Row network property.
        /// </summary>
        [TikProperty("network")]
        public string Network { get; set; }

        /// <summary>
        /// Row comment property.
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>
        /// Row disabled property.
        /// </summary>
        [TikProperty("disabled")]
        public bool Disabled { get; set; }

        /// <summary>
        /// Row dynamic property.
        /// </summary>
        [TikProperty("dynamic", IsReadOnly = true)]
        public bool Dynamic { get; set; }

        /// <summary>
        /// Row invalid property.
        /// </summary>
        [TikProperty("invalid", IsReadOnly = true)]
        public bool Invalid { get; set; }
    }
}
