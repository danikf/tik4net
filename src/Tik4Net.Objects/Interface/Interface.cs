using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface
{
    /// <summary>
    /// /interface
    /// </summary>
    [TikEntity("/interface", IncludeDetails = true)]
    public class Interface
    {
        /// <summary>
        /// .id
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// name
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// default-name
        /// </summary>
        [TikProperty("default-name")]
        public string DefaultName { get; set; }

        /// <summary>
        /// type
        /// </summary>
        [TikProperty("type")]
        public string Type { get; set; }

        /// <summary>
        /// mtu
        /// </summary>
        [TikProperty("mtu")]
        public string Mtu { get; set; }

        /// <summary>
        /// mac-address
        /// </summary>
        [TikProperty("mac-address")]
        public string MacAddress { get; set; }

        /// <summary>
        /// fast-path
        /// </summary>
        [TikProperty("fast-path")]
        public bool FastPath { get; set; }

        /// <summary>
        /// rx-byte
        /// </summary>
        [TikProperty("rx-byte", IsReadOnly = true)]
        public long RxByte { get; private set; }

        /// <summary>
        /// tx-byte
        /// </summary>
        [TikProperty("tx-byte", IsReadOnly = true)]
        public long TxByte { get; private set; }

        /// <summary>
        /// rx-packet
        /// </summary>
        [TikProperty("rx-packet", IsReadOnly = true)]
        public long RxPacket { get; private set; }

        /// <summary>
        /// tx-packet
        /// </summary>
        [TikProperty("tx-packet", IsReadOnly = true)]
        public long TxPacket { get; private set; }

        /// <summary>
        /// rx-drop
        /// </summary>
        [TikProperty("rx-drop", IsReadOnly = true)]
        public long RxDrop { get; private set; }

        /// <summary>
        /// tx-drop
        /// </summary>
        [TikProperty("tx-drop", IsReadOnly = true)]
        public long TxDrop { get; private set; }

        /// <summary>
        /// rx-error
        /// </summary>
        [TikProperty("rx-error", IsReadOnly = true)]
        public long RxError { get; private set; }

        /// <summary>
        /// tx-error
        /// </summary>
        [TikProperty("tx-error", IsReadOnly = true)]
        public long TxError { get; private set; }

        /// <summary>
        /// running
        /// </summary>
        [TikProperty("running", IsReadOnly = true)]
        public bool Running { get; private set; }

        /// <summary>
        /// disabled
        /// </summary>
        [TikProperty("disabled")]
        public bool Disabled { get; set; }

        /// <summary>
        /// comment
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }
    }

}
