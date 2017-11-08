using InvertedTomato.TikLink.RosRecords;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink {
    public class LinkInterfaceEthernet {
        private readonly Link Link;

        internal LinkInterfaceEthernet(Link link) {
            Link = link;
        }

        public IList<InterfaceEthernet> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<InterfaceEthernet>(properties, filter);
        }

        public InterfaceEthernet Get(string id, string[] properties = null) {
            return Link.Get<InterfaceEthernet>(id, properties);
        }

        public void Put(InterfaceEthernet record, string[] properties = null) {
            Link.Put(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<InterfaceEthernet>(id);
        }

        public void Delete(InterfaceEthernet record) {
            Link.Delete(record);
        }
    }
}
