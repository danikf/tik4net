using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using tik4net.Cli;
using tik4net.MacTelnet;

namespace tik4net.unittests.MacTelnet
{
    /// <summary>
    /// The router's end of a MAC-Telnet session over loopback UDP, for a <see cref="MacTelnetUdpClient"/> wired to it
    /// without the MAC-layer connect or EC-SRP5: one datagram per screen the router writes, its own DATA counter, ACKs
    /// and <c>PKT_END</c>. What the client sends is read back with <see cref="TryReceive"/>.
    /// </summary>
    /// <remarks>
    /// The session key is 0 on both sides, so every packet passes the client's session check; the MACs are the
    /// locally-administered <c>02:00:00:00:00:01</c> (client) and <c>…:02</c> (router).
    /// </remarks>
    internal sealed class MacTelnetFakeRouter : IDisposable
    {
        internal const byte PktData = 1, PktAck = 2, PktEnd = 255;
        private const int HeaderLength = 22;
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly UdpClient _router;
        private readonly IPEndPoint _client;
        private readonly object _sendGate = new object();
        private uint _outCounter;

        /// <param name="receiveTimeoutMs">The client's receive timeout (its connect timeout is fixed at 4 s).</param>
        internal MacTelnetFakeRouter(int receiveTimeoutMs = 4000)
        {
            _router = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            Client = new MacTelnetUdpClient(Encoding.UTF8, CliClientTimeouts.Fixed(receiveTimeoutMs, 4000), null);

            var peer = (IPEndPoint)_router.Client.LocalEndPoint;
            var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));   // the client disposes it
            Set("_udp", udp);
            Set("_routerEp", peer);
            Set("_routerUnicastEp", peer);
            Set("_localMac", new byte[] { 0x02, 0, 0, 0, 0, 0x01 });
            Set("_routerMac", new byte[] { 0x02, 0, 0, 0, 0, 0x02 });
            _client = (IPEndPoint)udp.Client.LocalEndPoint;
        }

        internal MacTelnetUdpClient Client { get; }

        /// <summary>Starts the client's receive pump, as the end of its login does.</summary>
        internal void StartPump() => typeof(MacTelnetUdpClient).GetMethod("StartPump", Flags).Invoke(Client, null);

        /// <summary>Writes one screen: a DATA datagram at the router's next stream offset.</summary>
        internal void SendData(string text)
        {
            byte[] payload = Encoding.UTF8.GetBytes(text);
            lock (_sendGate)
            {
                Send(PktData, _outCounter, payload);
                _unacked.Add((_outCounter, payload));
                _outCounter += (uint)payload.Length;
            }
        }

        // DATA the client has not acknowledged yet, oldest first.
        private readonly System.Collections.Generic.List<(uint Counter, byte[] Payload)> _unacked
            = new System.Collections.Generic.List<(uint Counter, byte[] Payload)>();

        /// <summary>
        /// The client has acknowledged the stream up to <paramref name="counter"/>; a later
        /// <see cref="RetransmitUnacked"/> resends only what lies past it.
        /// </summary>
        internal void NoteAck(uint counter)
        {
            lock (_sendGate)
                _unacked.RemoveAll(d => d.Counter + (uint)d.Payload.Length <= counter);
        }

        /// <summary>
        /// Resends every DATA datagram the client has not acknowledged, as RouterOS does: the client's pump can lose one
        /// at its own receive timeout (see <see cref="TryReceive"/>), and without the resend the answer would never
        /// arrive. A copy the client already has is re-acknowledged and dropped by it.
        /// </summary>
        internal void RetransmitUnacked()
        {
            lock (_sendGate)
                foreach (var (counter, payload) in _unacked)
                    Send(PktData, counter, payload);
        }

        /// <summary>Ends the session from the router's side.</summary>
        internal void SendEnd() => Send(PktEnd, 0, null);

        /// <summary>Acknowledges the client's stream up to <paramref name="counter"/> (the offset past the data taken).</summary>
        internal void SendAck(uint counter) => Send(PktAck, counter, null);

        /// <summary>Total DATA payload bytes the router has written.</summary>
        internal uint OutCounter { get { lock (_sendGate) return _outCounter; } }

        /// <summary>One packet from the client, or false when none arrives within <paramref name="timeoutMs"/>.</summary>
        /// <remarks>
        /// Waits with <see cref="Socket.Poll"/> and receives only once a datagram is there, never with a receive timeout:
        /// on Windows a UDP receive that times out loses a datagram that arrives as it does (measured on loopback, .NET 8
        /// and .NET Framework: 328 and 402 of 3000 datagrams lost against a 5 ms <c>ReceiveTimeout</c>, none with a poll).
        /// The router's end polls every few milliseconds, so it lost a command now and then, and the read waited for an
        /// answer to a line the router never saw.
        /// </remarks>
        internal bool TryReceive(int timeoutMs, out byte type, out uint counter, out byte[] payload)
        {
            type = 0; counter = 0; payload = new byte[0];
            byte[] pkt;
            try
            {
                if (!_router.Client.Poll(timeoutMs * 1000, SelectMode.SelectRead))
                    return false;
                IPEndPoint ep = null;
                pkt = _router.Receive(ref ep);
            }
            catch (SocketException) { return false; }
            catch (ObjectDisposedException) { return false; }
            if (pkt.Length < HeaderLength) return false;
            type = pkt[1];
            counter = ((uint)pkt[18] << 24) | ((uint)pkt[19] << 16) | ((uint)pkt[20] << 8) | pkt[21];
            payload = new byte[pkt.Length - HeaderLength];
            Buffer.BlockCopy(pkt, HeaderLength, payload, 0, payload.Length);
            return true;
        }

        private void Send(byte type, uint counter, byte[] payload)
        {
            int length = payload?.Length ?? 0;
            byte[] pkt = new byte[HeaderLength + length];
            pkt[0] = 1; pkt[1] = type;
            pkt[2] = 0x02; pkt[7] = 0x02;    // source: the router
            pkt[8] = 0x02; pkt[13] = 0x01;   // destination: the client
            pkt[18] = (byte)(counter >> 24); pkt[19] = (byte)(counter >> 16); pkt[20] = (byte)(counter >> 8); pkt[21] = (byte)counter;
            if (length > 0) Buffer.BlockCopy(payload, 0, pkt, HeaderLength, length);
            try { _router.Send(pkt, pkt.Length, _client); }
            catch (ObjectDisposedException) { /* the test is over */ }
        }

        private void Set(string field, object value) => typeof(MacLayerTransport).GetField(field, Flags).SetValue(Client, value);

        public void Dispose()
        {
            Client.Dispose();
            _router.Dispose();
        }
    }
}
