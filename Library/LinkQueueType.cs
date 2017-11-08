using InvertedTomato.TikLink.RosRecords;
using System.Collections.Generic;

namespace InvertedTomato.TikLink {
    public class LinkQueueType {
        private readonly Link Link;

        internal LinkQueueType(Link link) {
            Link = link;
        }

        public IList<QueueType> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<QueueType>(properties, filter);
        }

        public QueueType Get(string id, string[] properties = null) {
            return Link.Get<QueueType>(id, properties);
        }

        public void Create(QueueType record, string[] properties = null) {
            Link.Create(record, properties);
        }

        public void Update(QueueType record, string[] properties = null) {
            Link.Update(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<QueueType>(id);
        }

        public void Delete(QueueType record) {
            Link.Delete(record);
        }
    }
}
