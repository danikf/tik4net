using InvertedTomato.TikLink.Records;
using System;

namespace InvertedTomato.TikLink.Vanity {
    public class LinkSystem {
        public readonly LinkSystemCertificate Certificate;
        public readonly LinkSystemResource Resource;

        private readonly Link Link;

        internal LinkSystem(Link link) {
            Link = link;

            Certificate = new LinkSystemCertificate(Link);
            Resource = new LinkSystemResource(Link);
        }
    }
}
