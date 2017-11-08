using InvertedTomato.TikLink.RosRecords;
using System.Collections.Generic;

namespace InvertedTomato.TikLink {
    public class LinkInterface {
        private readonly Link Link;

        public readonly LinkInterfaceBridge Bridge;
        public readonly LinkInterfaceEthernet Ethernet;
        public readonly LinkInterfaceWireless Wireless;

        internal LinkInterface(Link link) {
            Link = link;

            Bridge = new LinkInterfaceBridge(Link);
            Ethernet = new LinkInterfaceEthernet(Link);
            Wireless = new LinkInterfaceWireless(Link);
        }

        public IList<Interface> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<Interface>(properties, filter);
        }

        public Interface Get(string id, string[] properties = null) {
            return Link.Get<Interface>(id, properties);
        }

        public void Create(Interface record, string[] properties = null) {
            Link.Create(record, properties);
        }

        public void Update(Interface record, string[] properties = null) {
            Link.Update(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<Interface>(id);
        }

        public void Delete(Interface record) {
            Link.Delete(record);
        }
    }
}
