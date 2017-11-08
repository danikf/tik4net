using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.RosRecords {
    /// <summary>
    /// /ip/accounting/uncounted: In case no more IP pairs can be added to the accounting table (the accounting threshold has been reached), all traffic that does not belong to any of the known IP pairs is summed together and totals are shown in this menu
    /// </summary>
    [RosRecord("/ip/accounting/uncounted", IsReadOnly = true)]
    public class IpAccountingUncounted : ISingleRecord {
        /// <summary>
        /// bytes: byte count
        /// </summary>
        [RosProperty("bytes",IsReadOnly = true)]
        public int Bytes { get; private set; }

        /// <summary>
        /// packets: packet count
        /// </summary>
        [RosProperty("packets",IsReadOnly = true)]
        public int Packets { get; private set; }
    }
}
