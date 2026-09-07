using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace tik4net.Diagnostics
{
    /// <summary>
    /// Renders a socket endpoint for a message that may be read by someone other than the person running the
    /// program — an exception, a log line, a pasted bug report — keeping what identifies the session and
    /// dropping what identifies the network.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reason an endpoint appears in a message at all is that a stalled session has to be findable in the
    /// router's own <c>/ip/firewall/connection</c> table, which is keyed by <c>address:port</c>. The port is
    /// what does that work: it is unique per session, and whoever reads the row is already on the client
    /// machine and knows its address. The address itself adds nothing to the diagnosis and is the only part
    /// that survives being pasted somewhere public as information about a network.
    /// </para>
    /// <para>
    /// So the leading parts are masked and the last one kept — <c>xx.xx.xx.31:56864</c>. The final octet
    /// stays because on one segment it is what tells two client machines apart, and a message that named no
    /// host at all would be worse at the job than the one being replaced.
    /// </para>
    /// <para>
    /// This is for messages that travel. Wire traces are not routed through it: they are opt-in, read on the
    /// machine that produced them, and their whole purpose on a multi-homed client is to say which local
    /// interface a frame left by — which is the subnet, i.e. exactly the part masked here.
    /// </para>
    /// </remarks>
    internal static class TikEndpointText
    {
        /// <summary>What replaces each masked part of an address.</summary>
        private const string Mask = "xx";

        /// <summary>
        /// The endpoint as <c>xx.xx.xx.31:56864</c>, or a description of why it could not be read.
        /// </summary>
        /// <param name="endPoint">The endpoint, typically a socket's <c>LocalEndPoint</c>.</param>
        /// <returns>Never <c>null</c>: a message is being built and must not fail for want of this.</returns>
        internal static string Describe(EndPoint? endPoint)
        {
            if (endPoint == null)
                return "unknown";

            if (endPoint is IPEndPoint ip)
            {
                return Describe(ip.Address) + ":"
                    + ip.Port.ToString(CultureInfo.InvariantCulture);
            }

            // Some other address family (a unix socket, say). Nothing is known about what its text contains,
            // so it is not passed through — the port-and-host shape above is the only one this can mask.
            return endPoint.AddressFamily.ToString();
        }

        /// <summary>
        /// The address with everything but its last part replaced by <c>xx</c>.
        /// </summary>
        /// <remarks>
        /// IPv6 is masked the same way and by the same rule — every group but the last — rather than by
        /// counting bits: an address written <c>fe80::215:5dff:fe04:1f03</c> is masked to
        /// <c>xx:…:xx:1f03</c>, which keeps one group to tell hosts apart and drops the prefix that names
        /// the network. A scope id (<c>%12</c>) is dropped with it: it names a local interface.
        /// </remarks>
        /// <param name="address">The address to mask.</param>
        internal static string Describe(IPAddress? address)
        {
            if (address == null)
                return "unknown";

            // Loopback is not information about anyone's network, and masking it costs a reader the one
            // thing the line was saying — that the connection never left the machine.
            if (IPAddress.IsLoopback(address))
                return address.ToString();

            string text = address.ToString();

            int scope = text.IndexOf('%');
            if (scope >= 0)
                text = text.Substring(0, scope);

            char separator = address.AddressFamily == AddressFamily.InterNetworkV6 ? ':' : '.';

            int last = text.LastIndexOf(separator);
            if (last < 0)
                return Mask;   // no separator at all — nothing can be kept without keeping all of it

            string tail = text.Substring(last + 1);

            // "::" ends an IPv6 address elided to zeroes; there is no last group to keep.
            if (tail.Length == 0)
                return Mask + separator + separator;

            int parts = 1;
            for (int i = 0; i < last; i++)
                if (text[i] == separator) parts++;

            var masked = new System.Text.StringBuilder();
            for (int i = 0; i < parts; i++)
            {
                masked.Append(Mask);
                masked.Append(separator);
            }
            masked.Append(tail);
            return masked.ToString();
        }
    }
}
