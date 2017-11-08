using InvertedTomato.TikLink.RosRecords;
using System;

namespace InvertedTomato.TikLink {
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
