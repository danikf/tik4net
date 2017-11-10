using InvertedTomato.TikLink.Records;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkInterfaceEthernet : SetRecordHandlerBase<InterfaceEthernet> {
        public LinkInterfaceEthernet(Link link) : base(link) { }
    }
}
