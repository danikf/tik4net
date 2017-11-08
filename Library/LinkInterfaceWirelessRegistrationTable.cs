using InvertedTomato.TikLink.RosRecords;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink {
    public class LinkInterfaceWirelessRegistrationTable {
        private readonly Link Link;

        internal LinkInterfaceWirelessRegistrationTable(Link link) {
            Link = link;
        }

        public IList<InterfaceWirelessRegistrationTable> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<InterfaceWirelessRegistrationTable>(properties, filter);
        }

        public InterfaceWirelessRegistrationTable Get(string id, string[] properties = null) {
            return Link.Get<InterfaceWirelessRegistrationTable>(id, properties);
        }

        public void Put(InterfaceWirelessRegistrationTable record, string[] properties = null) {
            Link.Put(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<InterfaceWirelessRegistrationTable>(id);
        }

        public void Delete(InterfaceWirelessRegistrationTable record) {
            Link.Delete(record);
        }
    }
}
