using InvertedTomato.TikLink.RosRecords;
using System.Collections.Generic;

namespace InvertedTomato.TikLink {
    public class LinkIpPool {
        private readonly Link Link;

        internal LinkIpPool(Link link) {
            Link = link;
        }

        public IList<IpPool> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<IpPool>(properties, filter);
        }

        public IpPool Get(string id, string[] properties = null) {
            return Link.Get<IpPool>(id, properties);
        }

        public void Create(IpPool record, string[] properties = null) {
            Link.Create(record, properties);
        }

        public void Update(IpPool record, string[] properties = null) {
            Link.Update(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<IpPool>(id);
        }

        public void Delete(IpPool record) {
            Link.Delete(record);
        }
    }
}
