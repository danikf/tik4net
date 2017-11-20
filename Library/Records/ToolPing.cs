using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// Ping uses Internet Control Message Protocol (ICMP) Echo messages to determine if a remote host is active or inactive and to determine the round-trip delay when communicating with it. Ping tool sends ICMP (type 8) message to the host and waits for the ICMP echo-reply (type 0). The interval between these events is called round trip. If the response (that is called pong) has not come until the end of the interval, we assume it has timed out. The second significant parameter reported is ttl (Time to Live). Is is decremented at each machine in which the packet is processed. The packet will reach its destination only when the ttl is greater than the number of routers between the source and the destination.
    /// Author: seho85
    /// </summary>
    [RosRecord("/ping")] // Read-only
    public class ToolPing : RecordBase {
        /// <summary>
        /// Sequence number
        /// </summary>
        [RosProperty("seq")] // Read-only
        public string Sequence { get; set; }

        /// <summary>
        /// Pinged host.
        /// </summary>
        [RosProperty("host")] // Read-only
        public string Host { get; private set; }

        /// <summary>
        /// Time to live parameter adjustment
        /// </summary>
        [RosProperty("ttl")] // Read-only
        public string TimeToLive { get; private set; }

        /// <summary>
        /// The ping time.
        /// </summary>
        [RosProperty("time")] // Read-only
        public TimeSpan Time { get; private set; }

        /// <summary>
        /// sent
        /// </summary>
        [RosProperty("sent")] // Read-only
        public int Sent { get; private set; }

        /// <summary>
        /// received
        /// </summary>
        [RosProperty("received")] // Read-only
        public int Received { get; private set; }

        /// <summary>
        /// packet-loss
        /// </summary>
        [RosProperty("packet-loss")] // Read-only
        public string PacketLoss { get; private set; }

        /// <summary>
        /// min-rtt
        /// </summary>
        [RosProperty("min-rtt")] // Read-only
        public TimeSpan? MinRtt { get; private set; }

        /// <summary>
        /// avg-rtt
        /// </summary>
        [RosProperty("avg-rtt")] // Read-only
        public TimeSpan? AvgRtt { get; private set; }

        /// <summary>
        /// max-rtt
        /// </summary>
        [RosProperty("max-rtt")] // Read-only
        public TimeSpan? MaxRtt { get; private set; }
        //        <=seq=0
        //<=host=172.16.100.1
        //<=size=56
        //<=ttl=64
        //<=time=0ms
        //<=sent=1
        //<=received=1
        //<=packet-loss=0
        //<=min-rtt=0ms
        //<=avg-rtt=0ms
        //<=max-rtt=0ms


        /// <summary>
        /// ToString override to make life more easy.
        /// </summary>
        public override string ToString() {
            return $"{Host} ....... {Time}";
        }
    }
}
