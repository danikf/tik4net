﻿using InvertedTomato.TikLink.Records;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink.Vanity {
    public class LinkIpFirewallFilter {
        private readonly Link Link;

        internal LinkIpFirewallFilter(Link link) {
            Link = link;
        }

        public IList<IpFirewallFilter> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<IpFirewallFilter>(properties, filter);
        }

        public IpFirewallFilter Get(string id, string[] properties = null) {
            return Link.Get<IpFirewallFilter>(id, properties);
        }

        public void Add(IpFirewallFilter record, string[] properties = null) {
            Link.Add(record, properties);
        }

        public void Update(IpFirewallFilter record, string[] properties = null) {
            Link.Update(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<IpFirewallFilter>(id);
        }

        public void Delete(IpFirewallFilter record) {
            Link.Delete(record);
        }
    }
}
