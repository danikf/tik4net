using InvertedTomato.TikLink.RosRecords;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink {
    public class LinkInterfaceWireless {
        public readonly LinkInterfaceWirelessAccessList AccessList;
        public readonly LinkInterfaceWirelessRegistrationTable RegistrationTable;
        public readonly LinkInterfaceWirelessSecurityProfile SecurityProfile;

        private readonly Link Link;

        internal LinkInterfaceWireless(Link link) {
            Link = link;

            AccessList = new LinkInterfaceWirelessAccessList(Link);
            RegistrationTable = new LinkInterfaceWirelessRegistrationTable(Link);
            SecurityProfile = new LinkInterfaceWirelessSecurityProfile(Link);
        }

        public IList<InterfaceWireless> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<InterfaceWireless>(properties, filter);
        }

        public InterfaceWireless Get(string id, string[] properties = null) {
            return Link.Get<InterfaceWireless>(id, properties);
        }

        public void Put(InterfaceWireless record, string[] properties = null) {
            Link.Put(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<InterfaceWireless>(id);
        }

        public void Delete(InterfaceWireless record) {
            Link.Delete(record);
        }
    }
}
