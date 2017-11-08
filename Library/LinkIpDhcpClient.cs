﻿using InvertedTomato.TikLink.RosRecords;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink {
    public class LinkIpDhcpClient {
        private readonly Link Link;

        internal LinkIpDhcpClient(Link link) {
            Link = link;
        }

        public IList<IpDhcpClient> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<IpDhcpClient>(properties, filter);
        }

        public IpDhcpClient Get(string id, string[] properties = null) {
            return Link.Get<IpDhcpClient>(id, properties);
        }

        public void Create(IpDhcpClient record, string[] properties = null) {
            Link.Create(record, properties);
        }

        public void Update(IpDhcpClient record, string[] properties = null) {
            Link.Update(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<IpDhcpClient>(id);
        }

        public void Delete(IpDhcpClient record) {
            Link.Delete(record);
        }
    }
}
