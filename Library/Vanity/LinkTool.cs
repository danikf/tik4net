using InvertedTomato.TikLink.Records;
using System;

namespace InvertedTomato.TikLink.Vanity {
    public class LinkTool {
        public readonly LinkToolPing Ping;
        public readonly LinkToolTorch Torch;

        private readonly Link Link;

        internal LinkTool(Link link) {
            Link = link;

            Ping = new LinkToolPing(Link);
            Torch = new LinkToolTorch(Link);
        }
    }
}
