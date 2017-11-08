﻿using InvertedTomato.TikLink.Records;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink.Vanity {
    public class LinkInterfaceBridge {
        private readonly Link Link;

        internal LinkInterfaceBridge(Link link) {
            Link = link;
        }

        public IList<InterfaceBridge> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<InterfaceBridge>(properties, filter);
        }

        public InterfaceBridge Get(string id, string[] properties = null) {
            return Link.Get<InterfaceBridge>(id, properties);
        }

        public void Add(InterfaceBridge record, string[] properties = null) {
            Link.Add(record, properties);
        }

        public void Update(InterfaceBridge record, string[] properties = null) {
            Link.Update(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<InterfaceBridge>(id);
        }

        public void Delete(InterfaceBridge record) {
            Link.Delete(record);
        }
    }
}
