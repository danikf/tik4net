using InvertedTomato.TikLink.Records;
using System;
using System.Collections.Generic;

namespace InvertedTomato.TikLink.Vanity {
    public class LinkIpDhcpServerLease {
        private readonly Link Link;

        internal LinkIpDhcpServerLease(Link link) {
            Link = link;
        }

        public IList<IpDhcpServerLease> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<IpDhcpServerLease>(properties, filter);
        }

        public IpDhcpServerLease Get(string id, string[] properties = null) {
            return Link.Get<IpDhcpServerLease>(id, properties);
        }

        public void Add(IpDhcpServerLease record, string[] properties = null) {
            Link.Add(record, properties);
        }

        public void Update(IpDhcpServerLease record, string[] properties = null) {
            Link.Update(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<IpDhcpServerLease>(id);
        }

        public void Delete(IpDhcpServerLease record) {
            Link.Delete(record);
        }


        /// <summary>
        /// Check status of a given busy dynamic lease, and free it in case of no response
        /// </summary>
        public void CheckStatus(string id) {
            if (null == id) {
                throw new ArgumentNullException(nameof(id));
            }

            var resut = Link.Call("/ip/dhcp-server/lease/check-status", new Dictionary<string, string>() { { ".id", id } }).Wait();
            if (resut.IsError) {
                resut.TryGetTrapAttribute("message", out var message);
                throw new CallException(message);
            }
        }

        /// <summary>
        /// Convert a dynamic lease to a static one
        /// </summary>
        public void MakeStatic(string id) {
            if (null == id) {
                throw new ArgumentNullException(nameof(id));
            }

            var resut = Link.Call("/ip/dhcp-server/lease/make-static", new Dictionary<string, string>() { { ".id", id } }).Wait();
            if (resut.IsError) {
                resut.TryGetTrapAttribute("message", out var message);
                throw new CallException(message);
            }
        }
    }
}
