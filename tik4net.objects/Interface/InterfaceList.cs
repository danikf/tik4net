using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface
{
    /// <summary>
    /// /interface/list — named groups of interfaces. Interface lists are referenced from firewall
    /// rules, neighbor discovery, MAC server and other features so a set of interfaces can be managed
    /// as a single named entity. Members are managed via <see cref="InterfaceListMember"/>.
    /// </summary>
    [TikEntity("/interface/list", IncludeDetails = true)]
    public class InterfaceList
    {
        /// <summary>
        /// .id — primary key of the row.
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// name — name of the interface list.
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// include — comma-separated list of other interface lists whose members are included in this list.
        /// </summary>
        [TikProperty("include")]
        public string Include { get; set; }

        /// <summary>
        /// exclude — comma-separated list of other interface lists whose members are excluded from this list.
        /// </summary>
        [TikProperty("exclude")]
        public string Exclude { get; set; }

        /// <summary>
        /// builtin — whether this is a built-in list (all/none/dynamic/static) that cannot be removed (read-only).
        /// </summary>
        [TikProperty("builtin", IsReadOnly = true)]
        public bool Builtin { get; private set; }

        /// <summary>
        /// dynamic — whether the list was added dynamically and cannot be edited/removed (read-only).
        /// </summary>
        [TikProperty("dynamic", IsReadOnly = true)]
        public bool Dynamic { get; private set; }

        /// <summary>
        /// comment.
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return Name;
        }
    }
}
