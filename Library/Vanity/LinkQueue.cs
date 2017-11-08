namespace InvertedTomato.TikLink.Vanity {
    public class LinkQueue {
        public readonly LinkQueueSimple Simple;
        public readonly LinkQueueTree Tree;
        public readonly LinkQueueType Type;

        private readonly Link Link;

        internal LinkQueue(Link link) {
            Link = link;

            Simple = new LinkQueueSimple(Link);
            Tree = new LinkQueueTree(Link);
            Type = new LinkQueueType(Link);
        }
    }
}
