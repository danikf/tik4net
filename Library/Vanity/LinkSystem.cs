using InvertedTomato.TikLink.Records;
using System;

namespace InvertedTomato.TikLink.Vanity {
    public class LinkSystem {
        public readonly LinkSystemResource Resource;

        private readonly Link Link;

        internal LinkSystem(Link link) {
            Link = link;

            Resource = new LinkSystemResource(Link);
        }
    }
}
