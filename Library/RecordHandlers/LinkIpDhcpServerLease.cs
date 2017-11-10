using InvertedTomato.TikLink.Records;
using System;
using System.Collections.Generic;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkIpDhcpServerLease : SetRecordHandlerBase<IpDhcpServerLease> {
        internal LinkIpDhcpServerLease(Link link) : base(link) { }

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
