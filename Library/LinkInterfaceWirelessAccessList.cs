using InvertedTomato.TikLink.RosRecords;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink {
    public class LinkInterfaceWirelessAccessList {
        private readonly Link Link;

        internal LinkInterfaceWirelessAccessList(Link link) {
            Link = link;
        }

        public IList<InterfaceWirelessAccessList> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<InterfaceWirelessAccessList>(properties, filter);
        }

        public InterfaceWirelessAccessList Get(string id, string[] properties = null) {
            return Link.Get<InterfaceWirelessAccessList>(id, properties);
        }

        public void Put(InterfaceWirelessAccessList record, string[] properties = null) {
            Link.Put(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<InterfaceWirelessAccessList>(id);
        }

        public void Delete(InterfaceWirelessAccessList record) {
            Link.Delete(record);
        }
    }
}
