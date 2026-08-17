using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface
{
    /// <summary>
    /// /interface
    /// </summary>
    [TikEntity("/interface", IncludeDetails = true, IncludeCliStats = true)]
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
        [TikProperty("default-name", IsReadOnly = true)]
        public string DefaultName { get; private set; }

        /// <summary>
        /// type — what kind of interface this is (<c>ether</c>, <c>bridge</c>, <c>vlan</c>, …). Read-only:
        /// an interface's kind is decided when it is created, and <c>/interface set</c> does not accept it
        /// (verified by tab completion on RouterOS 7.23: <c>comment disabled l2mtu mtu name numbers</c>).
        /// </summary>
        [TikProperty("type", IsReadOnly = true)]
        public string Type { get; private set; }

        /// <summary>
        /// mtu
        /// </summary>
        [TikProperty("mtu")]
        public string Mtu { get; set; }

        /// <summary>
        /// l2mtu — the layer-2 MTU. One of the five arguments <c>/interface set</c> actually accepts
        /// (<c>comment disabled l2mtu mtu name numbers</c>, RouterOS 7.23); most interface kinds report it
        /// read-only and refuse a change, which the router answers with a trap rather than silence.
        /// </summary>
        [TikProperty("l2mtu")]
        public string L2Mtu { get; set; }

        /// <summary>
        /// mac-address. Read-only <b>here</b>: <c>/interface set</c> does not accept it. It is settable on
        /// the concrete menu the interface belongs to — <see cref="InterfaceEthernet"/> has a writable
        /// <c>mac-address</c>, and that is the entity to use for changing one.
        /// </summary>
        [TikProperty("mac-address", IsReadOnly = true)]
        public string MacAddress { get; private set; }

        /// <summary>
        /// fast-path. Read-only <b>here</b>, for the same reason as <see cref="MacAddress"/>: the toggle
        /// lives on the concrete interface menu (<c>/interface/ethernet</c> and friends), not on
        /// <c>/interface</c>, which reports the resulting state.
        /// </summary>
        [TikProperty("fast-path", IsReadOnly = true)]
        public bool? FastPath { get; private set; }

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
        public bool? Disabled { get; set; }

        /// <summary>
        /// comment
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>
        /// Link last down time. (since 6.43 version) Read-only: it is a measurement, and
        /// <c>/interface set</c> does not accept it (verified by tab completion on RouterOS 7.23).
        /// </summary>
        [TikProperty("last-link-down-time", IsReadOnly = true)]
        public string LastLinkDownTime { get; private set; }

        /// <summary>
        /// Link last up time (since 6.43 version) Read-only: it is a measurement, and
        /// <c>/interface set</c> does not accept it (verified by tab completion on RouterOS 7.23).
        /// </summary>
        [TikProperty("last-link-up-time", IsReadOnly = true)]
        public string LastLinkUpTime { get; private set; }
    }

}
