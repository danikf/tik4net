using InvertedTomato.TikLink.Records;
using System.Collections.Generic;

namespace InvertedTomato.TikLink.Vanity {
    public class LinkQueueTree {
        private readonly Link Link;

        internal LinkQueueTree(Link link) {
            Link = link;
        }

        public IList<QueueTree> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<QueueTree>(properties, filter);
        }

        public QueueTree Get(string id, string[] properties = null) {
            return Link.Get<QueueTree>(id, properties);
        }

        public void Create(QueueTree record, string[] properties = null) {
            Link.Create(record, properties);
        }

        public void Update(QueueTree record, string[] properties = null) {
            Link.Update(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<QueueTree>(id);
        }

        public void Delete(QueueTree record) {
            Link.Delete(record);
        }
    }
}
