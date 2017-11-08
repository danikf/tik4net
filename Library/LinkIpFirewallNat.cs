﻿using InvertedTomato.TikLink.RosRecords;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink {
    public class LinkIpFirewallNat {
        private readonly Link Link;

        internal LinkIpFirewallNat(Link link) {
            Link = link;
        }

        public IList<IpFirewallNat> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<IpFirewallNat>(properties, filter);
        }

        public IpFirewallNat Get(string id, string[] properties = null) {
            return Link.Get<IpFirewallNat>(id, properties);
        }

        public void Create(IpFirewallNat record, string[] properties = null) {
            Link.Create(record, properties);
        }

        public void Update(IpFirewallNat record, string[] properties = null) {
            Link.Update(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<IpFirewallNat>(id);
        }

        public void Delete(IpFirewallNat record) {
            Link.Delete(record);
        }
    }
}