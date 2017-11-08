using InvertedTomato.TikLink.Records;
using System;

namespace InvertedTomato.TikLink.Vanity {
    public class LinkCapsMan {
        public readonly LinkCapsManRegistrationTable RegistrationTable;
        public readonly LinkBridgeNat Nat;
        public readonly LinkBridgePort Port;

        private readonly Link Link;

        internal LinkCapsMan(Link link) {
            Link = link;

            RegistrationTable = new LinkCapsManRegistrationTable(Link);
        }
    }
}
