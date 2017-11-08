using InvertedTomato.TikLink.Records;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink.Vanity {
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

        public void Create(InterfaceEthernet record, string[] properties = null) {
            Link.Create(record, properties);
        }

        public void Update(InterfaceEthernet record, string[] properties = null) {
            Link.Update(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<InterfaceEthernet>(id);
        }

        public void Delete(InterfaceEthernet record) {
            Link.Delete(record);
        }
    }
}
