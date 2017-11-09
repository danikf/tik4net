using InvertedTomato.TikLink.Records;
using System;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkBridge {
        public readonly LinkBridgeFilter Filter;
        public readonly LinkBridgeNat Nat;
        public readonly LinkBridgePort Port;
        public readonly LinkBridgeSettings Settings;

        private readonly Link Link;

        internal LinkBridge(Link link) {
            Link = link;

            Filter = new LinkBridgeFilter(Link);
            Nat = new LinkBridgeNat(Link);
            Port = new LinkBridgePort(Link);
            Settings = new LinkBridgeSettings(Link);
        }
    }
}
