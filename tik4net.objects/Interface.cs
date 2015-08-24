using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net.Objects
{
    [TikEntity("/interface", IncludeDetails = true)]
    public class Interface
    {
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        [TikProperty("default-name")]
        public string DefaultName { get; set; }

        [TikProperty("type")]
        public string Type { get; set; }

        [TikProperty("mtu")]
        public long Mtu { get; set; }

        [TikProperty("mac-address")]
        public string MacAddress { get; set; }

        [TikProperty("fast-path")]
        public bool FastPath { get; set; }

        [TikProperty("rx-byte", IsReadOnly = true)]
        public long RxByte { get; private set; }

        [TikProperty("tx-byte", IsReadOnly = true)]
        public long TxByte { get; private set; }

        [TikProperty("rx-packet", IsReadOnly = true)]
        public long RxPacket { get; private set; }

        [TikProperty("tx-packet", IsReadOnly = true)]
        public long TxPacket { get; private set; }

        [TikProperty("rx-drop", IsReadOnly = true)]
        public long RxDrop { get; private set; }

        [TikProperty("tx-drop", IsReadOnly = true)]
        public long TxDrop { get; private set; }

        [TikProperty("rx-error", IsReadOnly = true)]
        public long RxError { get; private set; }

        [TikProperty("tx-error", IsReadOnly = true)]
        public long TxError { get; private set; }

        [TikProperty("running", IsReadOnly = true)]
        public bool Running { get; private set; }

        [TikProperty("disabled")]
        public bool Disabled { get; set; }

        /// <summary>
        /// Row comment property.
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }
    }

}
