using InvertedTomato.TikLink.Records;
using System.Collections.Generic;

namespace InvertedTomato.TikLink.Vanity {
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

        public void Create(InterfaceWireless record, string[] properties = null) {
            Link.Create(record, properties);
        }

        public void Update(InterfaceWireless record, string[] properties = null) {
            Link.Update(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<InterfaceWireless>(id);
        }

        public void Delete(InterfaceWireless record) {
            Link.Delete(record);
        }
    }
}
