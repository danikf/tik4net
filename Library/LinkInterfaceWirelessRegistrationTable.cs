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

        public void Create(InterfaceWirelessRegistrationTable record, string[] properties = null) {
            Link.Create(record, properties);
        }

        public void Update(InterfaceWirelessRegistrationTable record, string[] properties = null) {
            Link.Update(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<InterfaceWirelessRegistrationTable>(id);
        }

        public void Delete(InterfaceWirelessRegistrationTable record) {
            Link.Delete(record);
        }
    }
}
