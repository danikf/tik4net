using InvertedTomato.TikLink.Records;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink.Vanity {
    public class LinkInterfaceWirelessSecurityProfile {
        private readonly Link Link;

        internal LinkInterfaceWirelessSecurityProfile(Link link) {
            Link = link;
        }

        public IList<InterfaceWirelessSecurityProfile> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<InterfaceWirelessSecurityProfile>(properties, filter);
        }

        public InterfaceWirelessSecurityProfile Get(string id, string[] properties = null) {
            return Link.Get<InterfaceWirelessSecurityProfile>(id, properties);
        }

        public void Add(InterfaceWirelessSecurityProfile record, string[] properties = null) {
            Link.Add(record, properties);
        }

        public void Update(InterfaceWirelessSecurityProfile record, string[] properties = null) {
            Link.Update(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<InterfaceWirelessSecurityProfile>(id);
        }

        public void Delete(InterfaceWirelessSecurityProfile record) {
            Link.Delete(record);
        }
    }
}
