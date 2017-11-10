using InvertedTomato.TikLink.Records;
using System;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkIpFirewallConnection : SetRecordHandlerBase<IpFirewallConnection> {
        public readonly LinkIpFirewallConnectionTracking Tracking;

        internal LinkIpFirewallConnection(Link link) : base(link) {
            Tracking = new LinkIpFirewallConnectionTracking(Link);
        }

        public override void Update(IpFirewallConnection record, string[] properties = null) {
            throw new NotImplementedException();
        }
    }
}
