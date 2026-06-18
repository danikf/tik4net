using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface
{
    /// <summary>
    /// /interface/list/member — assigns an interface to an <see cref="InterfaceList"/>. One row binds a
    /// single interface to a single named list (an interface may be a member of several lists).
    /// </summary>
    [TikEntity("/interface/list/member", IncludeDetails = true)]
    public class InterfaceListMember
    {
        /// <summary>
        /// .id — primary key of the row.
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// list — name of the interface list this membership belongs to (see <see cref="InterfaceList.Name"/>).
        /// </summary>
        [TikProperty("list", IsMandatory = true)]
        public string List { get; set; }

        /// <summary>
        /// interface — name of the interface added to the list.
        /// </summary>
        [TikProperty("interface", IsMandatory = true)]
        public string Interface { get; set; }

        /// <summary>
        /// dynamic — whether the membership was added dynamically and cannot be edited/removed (read-only).
        /// </summary>
        [TikProperty("dynamic", IsReadOnly = true)]
        public bool Dynamic { get; private set; }

        /// <summary>
        /// disabled — whether this membership is disabled.
        /// </summary>
        [TikProperty("disabled")]
        public bool Disabled { get; set; }

        /// <summary>
        /// comment.
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Format("{0} in {1}", Interface, List);
        }
    }
}
