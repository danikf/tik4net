using InvertedTomato.TikLink.Records;
using System;

namespace InvertedTomato.TikLink.RecordHandlers {
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
