using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Tool
{
    /// <summary>
    /// Traceroute displays the list of the routers that packet travels through to get to a remote host. The traceroute or tracepath tool is available on practically all Unix-like operating systems and tracert on Microsoft Windows operating systems.
    /// Traceroute operation is based on TTL value and ICMP “Time Exceeded” message.Remember that TTL value in IP header is used to avoid routing loops.Each hop decrements TTL value by 1. If the TTL reaches zero, the packet is discarded and ICMP Time Exceeded message is sent back to the sender when this occurs.
    /// Initially by traceroute, the TTL value is set to 1 when next router finds a packet with TTL = 1 it sets TTL value to zero, and responds with an ICMP "time exceeded" message to the source. This message lets the source know that the packet traverses that particular router as a hop. Next time TTL value is incremented by 1 and so on.Typically, each router in the path towards the destination decrements the TTL field by one unit TTL reaches zero.
    /// Using this command you can see how packets travel through the network and where it may fail or slow down. Using this information you can determine the computer, router, switch or other network device that possibly causing network issues or failures.  
    /// </summary>
    [TikEntity("/tool/traceroute", LoadCommand = "", LoadDefaultParameneterFormat = TikCommandParameterFormat.NameValue, IsReadOnly = true, IncludeProplist = false)]
    public class ToolTraceroute
    {
        /// <summary>
        /// address
        /// </summary>
        [TikProperty("address", IsReadOnly = true)]
        public string Address { get; private set; }

        /// <summary>
        /// loss
        /// </summary>
        [TikProperty("loss", IsReadOnly = true)]
        public int Loss { get; private set; }

        /// <summary>
        /// sent
        /// </summary>
        [TikProperty("sent", IsReadOnly = true)]
        public int Sent { get; private set; }

        /// <summary>
        /// last
        /// </summary>
        [TikProperty("last", IsReadOnly = true)]
        public string Last { get; private set; }

        /// <summary>
        /// status
        /// </summary>
        [TikProperty("status", IsReadOnly = true)]
        public string Status { get; private set; }

        /// <summary>
        /// avg
        /// </summary>
        [TikProperty("avg", IsReadOnly = true, IsMandatory = false)]
        public string Avg { get; private set; }

        /// <summary>
        /// best
        /// </summary>
        [TikProperty("best", IsReadOnly = true, IsMandatory = false)]
        public string Best { get; private set; }

        /// <summary>
        /// worst
        /// </summary>
        [TikProperty("worst", IsReadOnly = true, IsMandatory = false)]
        public string Worst { get; private set; }

        /// <summary>
        /// Traceroutes given <see paramref="address"/>.
        /// </summary>
        public static IEnumerable<ToolTraceroute> Execute(ITikConnection connection, string address)
        {
            return ToolTracerouteConnectionExtensions.Traceroute(connection, address);
        }
    }

    /// <summary>
    /// Connection extension class for <see cref="ToolPing"/>
    /// </summary>
    public static class ToolTracerouteConnectionExtensions
    {
        /// <summary>
        /// Traceroutes given <see paramref="address"/>.
        /// </summary>
        public static IEnumerable<ToolTraceroute> Traceroute(this ITikConnection connection, string address)
        {
            var result = connection.LoadList<ToolTraceroute>(
                connection.CreateParameter("address", address, TikCommandParameterFormat.NameValue),
                connection.CreateParameter("count", "1", TikCommandParameterFormat.NameValue));

            return result;
        }
    }
}
