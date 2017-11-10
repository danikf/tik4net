using InvertedTomato.TikLink.Records;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkInterfaceWirelessAccessList : SetRecordHandlerBase<InterfaceWirelessAccessList> {
        public LinkInterfaceWirelessAccessList(Link link) : base(link) { }
    }
}
