﻿using InvertedTomato.TikLink.RosRecords;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink {
    public class LinkIpAddress {
        private readonly Link Link;

        internal LinkIpAddress(Link link) {
            Link = link;
        }

        public IList<IpAddress> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<IpAddress>(properties, filter);
        }

        public IpAddress Get(string id, string[] properties = null) {
            return Link.Get<IpAddress>(id, properties);
        }

        public void Create(IpAddress record, string[] properties = null) {
            Link.Create(record, properties);
        }

        public void Update(IpAddress record, string[] properties = null) {
            Link.Update(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<IpAddress>(id);
        }

        public void Delete(IpAddress record) {
            Link.Delete(record);
        }
    }
}
