using InvertedTomato.TikLink.RosRecords;
using System.Collections.Generic;

namespace InvertedTomato.TikLink {
    public class LinkQueueSimple {
        private readonly Link Link;

        internal LinkQueueSimple(Link link) {
            Link = link;
        }

        public IList<QueueSimple> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<QueueSimple>(properties, filter);
        }

        public QueueSimple Get(string id, string[] properties = null) {
            return Link.Get<QueueSimple>(id, properties);
        }

        public void Create(QueueSimple record, string[] properties = null) {
            Link.Create(record, properties);
        }

        public void Update(QueueSimple record, string[] properties = null) {
            Link.Update(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<QueueSimple>(id);
        }

        public void Delete(QueueSimple record) {
            Link.Delete(record);
        }
    }
}
