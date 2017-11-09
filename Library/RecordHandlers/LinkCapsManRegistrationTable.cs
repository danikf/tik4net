using InvertedTomato.TikLink.Records;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkCapsManRegistrationTable {
        private readonly Link Link;

        internal LinkCapsManRegistrationTable(Link link) {
            Link = link;
        }

        public IList<CapsManRegistrationTable> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<CapsManRegistrationTable>(properties, filter);
        }

        public CapsManRegistrationTable Get(string id, string[] properties = null) {
            return Link.Get<CapsManRegistrationTable>(id, properties);
        }
    }
}
