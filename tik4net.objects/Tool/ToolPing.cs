using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Tool
{
    /// <summary>
    /// Ping uses Internet Control Message Protocol (ICMP) Echo messages to determine if a remote host is active or inactive and to determine the round-trip delay when communicating with it. Ping tool sends ICMP (type 8) message to the host and waits for the ICMP echo-reply (type 0). The interval between these events is called round trip. If the response (that is called pong) has not come until the end of the interval, we assume it has timed out. The second significant parameter reported is ttl (Time to Live). Is is decremented at each machine in which the packet is processed. The packet will reach its destination only when the ttl is greater than the number of routers between the source and the destination.
    /// Author: seho85
    /// </summary>
    [TikEntity("/ping", IsReadOnly = true, IncludeProplist = false)]
    public class ToolPing
    {
        //    [TikProperty("address", IsReadOnly = true)]
        //    public string Address { get; set; }

        //    [TikProperty("count", IsReadOnly = true)]
        //    public string Count { get; set; }

        //    [TikProperty("size", IsReadOnly = true)]
        //    public string Size { get; set; }

        /// <summary>
        /// Sequence number
        /// </summary>
        [TikProperty("seq", IsReadOnly = true)]
        public long SequenceNo { get; private set; }

        /// <summary>
        /// Pinged host.
        /// </summary>
        [TikProperty("host", IsReadOnly = true)]
        public string Host { get; private set; }

        /// <summary>
        /// Time to live parameter adjustment
        /// </summary>
        [TikProperty("ttl", IsReadOnly = true)]
        public string TimeToLife { get; private set; }

        /// <summary>
        /// The ping time.
        /// </summary>
        [TikProperty("time", IsReadOnly = true)]
        public string Time { get; private set; }

        /// <summary>
        /// sent
        /// </summary>
        [TikProperty("sent", IsReadOnly = true)]
        public string Sent { get; private set; }

        /// <summary>
        /// received
        /// </summary>
        [TikProperty("received", IsReadOnly = true)]
        public string Received { get; private set; }

        /// <summary>
        /// packet-loss
        /// </summary>
        [TikProperty("packet-loss", IsReadOnly = true)]
        public string PacketLoss { get; private set; }

        /// <summary>
        /// min-rtt
        /// </summary>
        [TikProperty("min-rtt", IsReadOnly = true)]
        public string MinRtt { get; private set; }

        /// <summary>
        /// avg-rtt
        /// </summary>
        [TikProperty("avg-rtt", IsReadOnly = true)]
        public string AvgRtt { get; private set; }

        /// <summary>
        /// max-rtt
        /// </summary>
        [TikProperty("max-rtt", IsReadOnly = true)]
        public string MaxRtt { get; private set; }
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

        private static string FormatAddress(string ip, string port)
        {
            return (ip + ":" + port).PadRight(21);
        }

        /// <summary>
        /// ToString override to make life more easy.
        /// </summary>
        public override string ToString()
        {
            return string.Format("{0} ....... {1}", Host, TikTimeHelper.FromTikTimeToSeconds(Time));
        }
    }
}
