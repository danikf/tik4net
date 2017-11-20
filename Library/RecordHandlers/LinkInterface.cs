using InvertedTomato.TikLink.Records;
using System;
using System.Collections.Generic;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkInterface : SetRecordHandlerBase<Interface> {
        public readonly LinkInterfaceBridge Bridge;
        public readonly LinkInterfaceEthernet Ethernet;
        public readonly LinkInterfaceWireless Wireless;

        public LinkInterface(Link link) : base(link) {
            Bridge = new LinkInterfaceBridge(Link);
            Ethernet = new LinkInterfaceEthernet(Link);
            Wireless = new LinkInterfaceWireless(Link);
        }

        public override void Add(Interface record, bool readBack = false, string[] properties = null) {
            throw new NotSupportedException();
        }
    }
}
