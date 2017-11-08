using InvertedTomato.TikLink.RosRecords;
using System;

namespace InvertedTomato.TikLink {
    public class LinkSystemResource {
        private readonly Link Link;

        internal LinkSystemResource(Link link) {
            Link = link;
        }

        public SystemResource Get(string[] properties = null) {
            return Link.Get<SystemResource>(properties);
        }
    }
}
