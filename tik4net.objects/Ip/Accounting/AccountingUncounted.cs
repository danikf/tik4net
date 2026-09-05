using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip
{
    /// <summary>
    /// /ip/accounting/uncounted: In case no more IP pairs can be added to the accounting table (the accounting threshold has been reached), all traffic that does not belong to any of the known IP pairs is summed together and totals are shown in this menu
    /// <para>
    /// Declared <see cref="TikEntityOperations.None"/> but not measured - see
    /// <see cref="AccountingSnapshot"/>: <c>/ip/accounting</c> does not exist on RouterOS 7.
    /// </para>
    /// </summary>
    [TikEntity("/ip/accounting/uncounted", SupportedOperations = TikEntityOperations.None, IsSingleton = true)]
    public class AccountingUncounted
    {
        /// <summary>
        /// bytes: byte count
        /// </summary>
        [TikProperty("bytes", IsReadOnly = true)]
        public int Bytes { get; private set; }

        /// <summary>
        /// packets: packet count
        /// </summary>
        [TikProperty("packets", IsReadOnly = true)]
        public int Packets { get; private set; }
    }
}
