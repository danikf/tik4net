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

        /// <summary>
        /// True when a <see cref="Receive"/> can be expected to return a frame promptly — i.e. inbound
        /// data for one is actually waiting.
        /// </summary>
        /// <remarks>
        /// Callers treat this as permission to <b>block</b>: the terminal loops gate a generous per-frame
        /// receive on it, because a receive that times out mid-frame leaves the stream unrecoverable. An
        /// implementation that answers true for traffic which cannot produce a frame therefore does not
        /// merely cost a wasted poll — it costs the caller a whole frame timeout. The MAC channel used to
        /// answer on any datagram, most of which are ACK/PING/retransmit control packets, and that alone
        /// accounted for 5 s per command and per open (P2.43).
        /// </remarks>
        bool DataAvailable { get; }

        /// <summary>
        /// True when <see cref="DataAvailable"/> + <see cref="Receive"/> can be used to cheaply drain stale
        /// buffered <em>frames</em> before a synchronous request (TCP: a waiting byte means a real buffered
        /// M2 frame). False for the MAC transport, where a stale frame is not the problem — leftover partial
        /// reassembly state underneath it is, and discarding whole frames cannot clear that.
        /// </summary>
        bool SupportsStaleDrain { get; }

        /// <summary>
        /// True once the channel knows the router did <b>not</b> take the bytes we last sent — it has been
        /// retransmitting them to exhaustion and has never been acknowledged. Always false on a channel that
        /// cannot tell (see the implementations).
        /// </summary>
        /// <remarks>
        /// This is the one signal that separates "the router has dropped this session" from "the router took
        /// the command and is being slow", and only the former can be reported without lying about whether
        /// the command ran. Without it a dead session costs the caller a whole
        /// <see cref="ITikConnection.ReceiveTimeout"/> and then reports "nothing was received", which names
        /// the symptom at the wrong layer (P2.54; MAC-Telnet learned the same lesson in P2.39).
        /// </remarks>
        bool SendAbandoned { get; }

        /// <summary>
        /// Connects to the router and authenticates. <paramref name="port"/> is honoured by TCP
        /// transports and ignored by the MAC transport (which always uses UDP 20561).
        /// </summary>
        /// <param name="host">Router host (IP address).</param>
        /// <param name="port">TCP port; ignored by the MAC transport.</param>
        /// <param name="user">User name.</param>
        /// <param name="password">Password.</param>
        /// <param name="connectTimeoutMs">
        /// Bounds connect + authentication only. Kept separate from <paramref name="ioTimeoutMs"/> so a
        /// black-holed host fails fast without also shortening per-command reads.
        /// </param>
        /// <param name="ioTimeoutMs">Default socket receive/send timeout for the established session.</param>
        void Open(string host, int port, string user, string password, int connectTimeoutMs, int ioTimeoutMs);

        /// <summary>Builds the next request-id system field (key 0xFF0006), advancing the counter.</summary>
        byte[] NextReqIdField();

        /// <summary>Sends one M2 message (fire-and-forget).</summary>
        void Send(byte[] m2);

        /// <summary>Receives and decodes one inbound M2 message, or <c>null</c> on timeout / non-data frame.</summary>
        byte[] Receive(int timeoutMs);

        /// <summary>Sends one M2 message and reads one response.</summary>
        byte[] SendReceive(byte[] m2, int timeoutMs);

        /// <summary>
        /// True when the channel can hand its read side to a <see cref="WinboxM2Multiplexer"/> reader loop
        /// (i.e. <see cref="ReceiveNextFrame"/> is implemented). Both the TCP and the MAC channel do; the MAC
        /// one only since P2.42, once <c>MacLayerTransport</c> could survive sends from two threads — see
        /// <c>Docs/winbox-m2-multiplexing-design.md</c> §4.5. The property stays on the interface because a
        /// channel that streams unsolicited id-less frames must never be given a multiplexer.
        /// </summary>
        bool SupportsReaderLoop { get; }

        /// <summary>
        /// Blocks until the next inbound M2 message is decoded, for exclusive use by a reader loop that
        /// owns the read side of the channel. Unlike <see cref="Receive"/> this carries <b>no timeout</b>:
        /// a per-read deadline can fire mid-frame and desynchronize the stream, so deadlines belong to the
        /// per-request registrations instead (design §4.2a). Returns <c>null</c> when the channel is closed.
        /// </summary>
        /// <exception cref="NotSupportedException">
        /// <see cref="SupportsReaderLoop"/> is <c>false</c>.
        /// </exception>
        byte[] ReceiveNextFrame();
    }
}
