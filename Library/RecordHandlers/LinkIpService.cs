using InvertedTomato.TikLink.Records;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkIpService {
        private readonly Link Link;

        internal LinkIpService(Link link) {
            Link = link;
        }

        public IList<IpService> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<IpService>(properties, filter);
        }

        public IpService Get(string id, string[] properties = null) {
            return Link.Get<IpService>(id, properties);
        }

        public void Update(IpService record, string[] properties = null) {
            Link.Update(record, properties);
        }
    }
}
