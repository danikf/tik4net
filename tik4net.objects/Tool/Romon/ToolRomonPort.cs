using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Tool.Romon
{
    /// <summary>
    /// /tool/romon/port — per-interface RoMON port configuration.
    /// Each entry controls whether a specific interface (or "all") participates in the RoMON
    /// overlay network, its link cost, and optional per-interface secrets.
    /// A default catch-all entry for "all" interfaces is created automatically.
    /// <para>See also: https://help.mikrotik.com/docs/display/ROS/RoMON</para>
    /// </summary>
    [TikEntity("/tool/romon/port", IncludeDetails = true)]
    public class ToolRomonPort
    {
        /// <summary>.id — primary key of the row.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>interface — interface name this entry applies to, or "all" for a catch-all entry.</summary>
        [TikProperty("interface", IsMandatory = true, DefaultValue = "all")]
        public string Interface { get; set; }

        /// <summary>forbid — when yes, RoMON traffic is blocked on this interface. Default: no.</summary>
        [TikProperty("forbid", DefaultValue = "no")]
        public bool Forbid { get; set; }

        /// <summary>cost — RoMON link cost for this interface (lower = preferred). Real default: 100; set to 0 to let the router use its default on add.</summary>
        // Router default is 100; DefaultValue="0" sentinel ensures 0 (CLR default) is omitted on add
        // so the router applies its own default rather than rejecting an out-of-range value.
        [TikProperty("cost", DefaultValue = "0")]
        public int Cost { get; set; }

        /// <summary>secrets — per-interface shared secrets (overrides global RoMON secrets when set).</summary>
        [TikProperty("secrets", DefaultValue = "")]
        public string Secrets { get; set; }

        /// <summary>disabled — when true this port entry is disabled. Default: no.</summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>comment — free-form comment.</summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        // ── Read-only status fields ──────────────────────────────────────────────

        /// <summary>default — when true this is the automatically created catch-all entry. Read-only.</summary>
        [TikProperty("default", IsReadOnly = true)]
        public bool Default { get; private set; }

        /// <summary>dynamic — when true the entry was created dynamically. Read-only.</summary>
        [TikProperty("dynamic", IsReadOnly = true)]
        public bool Dynamic { get; private set; }

        /// <summary>Returns a human-readable summary of this RoMON port entry.</summary>
        public override string ToString() => string.Format("romon/port: {0} (forbid={1}, cost={2})", Interface, Forbid, Cost);
    }
}
