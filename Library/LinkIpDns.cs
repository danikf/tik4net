using InvertedTomato.TikLink.RosRecords;
using System;

namespace InvertedTomato.TikLink {
    public class LinkIpDns {
        public readonly LinkIpDnsCache Cache;
        public readonly LinkIpDnsStatic Static;

        private readonly Link Link;

        internal LinkIpDns(Link link) {
            Link = link;

            Cache = new LinkIpDnsCache(Link);
            Static = new LinkIpDnsStatic(Link);
        }

        public IpDns Get(string[] properties = null) {
            return Link.Get<IpDns>(properties);
        }

        public void Update(IpDns record, string[] properties = null) {
            Link.Update(record, properties);
        }
    }
}
