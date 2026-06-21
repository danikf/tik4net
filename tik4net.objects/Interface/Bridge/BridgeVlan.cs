using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface.Bridge
{
    /// <summary>
    /// interface/bridge/vlan: Bridge VLAN filtering table.
    /// Each row defines which VLAN IDs are allowed on a given set of bridge ports, and whether
    /// those ports carry the VLAN tag on egress (tagged / trunk) or strip it (untagged / access).
    /// The table is only enforced when <c>vlan-filtering=yes</c> is set on the parent bridge.
    /// </summary>
    [TikEntity("interface/bridge/vlan", IncludeDetails = true)]
    public class BridgeVlan
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// bridge: The bridge interface this VLAN entry belongs to.
        /// WinBox: "Bridge"
        /// </summary>
        [TikProperty("bridge")]
        public string/*name*/ Bridge { get; set; }

        /// <summary>
        /// vlan-ids: VLAN IDs covered by this entry. Accepts a single ID, a comma-separated list,
        /// or a range (e.g. <c>100-115,120,122</c>). Valid range: 1–4094.
        /// WinBox: "VLAN IDs"
        /// </summary>
        [TikProperty("vlan-ids", DefaultValue = "1")]
        public string VlanIds { get; set; }

        /// <summary>
        /// tagged: Interfaces (or interface lists) that will add a VLAN tag on egress for these VLAN IDs
        /// (trunk / tagged ports). Comma-separated interface names.
        /// WinBox: "Tagged"
        /// </summary>
        [TikProperty("tagged")]
        public string Tagged { get; set; }

        /// <summary>
        /// untagged: Interfaces (or interface lists) that will strip the VLAN tag on egress for these
        /// VLAN IDs (access / untagged ports). Comma-separated interface names.
        /// WinBox: "Untagged"
        /// </summary>
        [TikProperty("untagged")]
        public string Untagged { get; set; }

        /// <summary>
        /// mvrp-forbidden: Interfaces on which MVRP registration for these VLAN IDs is forbidden.
        /// Comma-separated interface names.
        /// WinBox: "MVRP Forbidden"
        /// </summary>
        [TikProperty("mvrp-forbidden")]
        public string MvrpForbidden { get; set; }

        /// <summary>
        /// disabled: Whether this VLAN entry is disabled.
        /// WinBox: "Disabled"
        /// </summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>
        /// comment: Short description of the entry.
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        // ---- Read-only / dynamic fields ----

        /// <summary>
        /// current-tagged: Interfaces currently acting as tagged ports for these VLAN IDs,
        /// including ports added dynamically (e.g. via PVID). Read-only.
        /// WinBox: "Current Tagged"
        /// </summary>
        [TikProperty("current-tagged", IsReadOnly = true)]
        public string CurrentTagged { get; private set; }

        /// <summary>
        /// current-untagged: Interfaces currently acting as untagged ports for these VLAN IDs,
        /// including ports added dynamically. Read-only.
        /// WinBox: "Current Untagged"
        /// </summary>
        [TikProperty("current-untagged", IsReadOnly = true)]
        public string CurrentUntagged { get; private set; }

        /// <summary>
        /// dynamic: Whether this entry was created dynamically (e.g. by PVID auto-provisioning).
        /// Read-only.
        /// </summary>
        [TikProperty("dynamic", IsReadOnly = true)]
        public bool Dynamic { get; private set; }

        /// <summary>Human-readable identity: bridge + VLAN IDs.</summary>
        public override string ToString()
        {
            return string.Format("{0} vlan-ids={1}", Bridge, VlanIds);
        }
    }
}
