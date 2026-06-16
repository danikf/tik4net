using System;

namespace tik4net.Winbox
{
    /// <summary>
    /// Transport-agnostic WinBox M2 channel: connect + authenticate, then send/receive whole M2
    /// messages. Abstracts the difference between the TCP transport (<see cref="WinboxM2Session"/>,
    /// chunked frames over port 8291) and the MAC-layer transport (<c>WinboxMacM2Session</c>,
    /// encrypted blobs in UDP 20561 DATA packets).
    /// </summary>
    /// <remarks>
    /// The CLI engine (<c>WinboxCliClient</c>) builds mepty terminal messages on top of this interface,
    /// so the same terminal/VT100 logic drives both the WinBox-CLI (TCP) and WinBox-CLI-MAC transports.
    /// A future native-M2 mode will reuse the same channel.
    /// </remarks>
    internal interface IWinboxM2Channel : IDisposable
    {
        /// <summary>True once an encrypted channel is established (EC-SRP5 keys derived).</summary>
        bool IsEncrypted { get; }

        /// <summary>True when at least one inbound frame/packet is waiting to be read.</summary>
        bool DataAvailable { get; }

        /// <summary>
        /// True when <see cref="DataAvailable"/> + <see cref="Receive"/> can be used to cheaply drain stale
        /// buffered frames before a synchronous request (TCP: a waiting byte means a real buffered M2 frame).
        /// False for the MAC transport, where <c>_udp.Available</c> also reflects ACK/PING/retransmit control
        /// traffic, so a drain loop would thrash on noise rather than discard a single leftover frame.
        /// </summary>
        bool SupportsStaleDrain { get; }

        /// <summary>
        /// Connects to the router and authenticates. <paramref name="port"/> is honoured by TCP
        /// transports and ignored by the MAC transport (which always uses UDP 20561).
        /// </summary>
        void Open(string host, int port, string user, string password, int timeoutMs);

        /// <summary>Builds the next request-id system field (key 0xFF0006), advancing the counter.</summary>
        byte[] NextReqIdField();

        /// <summary>Sends one M2 message (fire-and-forget).</summary>
        void Send(byte[] m2);

        /// <summary>Receives and decodes one inbound M2 message, or <c>null</c> on timeout / non-data frame.</summary>
        byte[] Receive(int timeoutMs);

        /// <summary>Sends one M2 message and reads one response.</summary>
        byte[] SendReceive(byte[] m2, int timeoutMs);
    }
}
