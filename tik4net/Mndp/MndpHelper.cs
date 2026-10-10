using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.Mndp
{
    /// <summary>
    /// Implementation of MNDP (Microtik network discovery protocol).
    /// Tries to find all mikrotik routers accessible via local network broadcast on port 5678.
    /// </summary>
    public static class MndpHelper
    {
        //https://github.com/xmegz/MndpTray/blob/master/MndpTray/MndpTray.Protocol.Shared/MndpListener.cs
        //https://github.com/xmegz/MndpTray/blob/master/MndpTray/MndpTray.Protocol.Shared/MndpMessage.cs
        //https://hadler.me/cc/mikrotik-neighbor-discovery-mndp/
        //https://stackoverflow.com/questions/40616911/c-sharp-udp-broadcast-and-receive-example
        //https://forum.mikrotik.com/viewtopic.php?t=130551

        private const int MNDP_UDP_PORT = 5678;

        /// <summary>
        /// Discovers the MAC address of the router at <paramref name="host"/> (IPv4 string) via MNDP, as
        /// <c>AA:BB:CC:DD:EE:FF</c>, or <c>null</c> if no router announced that address within
        /// <paramref name="timeout"/> (default 5 s).
        /// </summary>
        /// <remarks>
        /// A string, because that is the spelling every other MAC in this library uses — it can be assigned
        /// straight to <see cref="ITikMacLayerConnection.RouterMac"/> or passed to
        /// <see cref="TikRouterAddress.FromMac(string)"/>. It used to return <c>byte[]</c>, which was the
        /// one shape nothing else accepted: a caller had to re-format the result of the discovery helper
        /// before handing it to the thing the helper exists to feed. The bytes were themselves produced by
        /// parsing this string, so nothing is lost by not making the round trip.
        /// </remarks>
        public static string? FindMacByHost(string host, TimeSpan? timeout = null)
        {
            var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5);
            var encoding = Encoding.GetEncoding("iso-8859-1");
            var found = Discover(effectiveTimeout, encoding, stopWhenFirstFound: false)
                .FirstOrDefault(r => r.IPv4?.ToString() == host);
            return string.IsNullOrEmpty(found.Mac) ? null : found.Mac;
        }

        /// <summary>
        /// Renders an MNDP address TLV (16 raw bytes for IPv6, 4 for IPv4) as an address string.
        /// </summary>
        /// <remarks>
        /// TLV 15 carries the ADDRESS BYTES, not text. Reading it with the same <c>encoding.GetString</c> as
        /// the neighbouring identity/version/board fields turns <c>fe80::215:5dff:fe04:1f03</c> into sixteen
        /// latin-1 characters, and the damage reaches <see cref="TikInstanceDescriptor.IpDescription"/>,
        /// which falls back to the v6 address when there is no v4 one — so a v6-only neighbour would
        /// describe itself in mojibake.
        /// </remarks>
        /// <param name="raw">The TLV payload.</param>
        /// <returns>The formatted address, or an empty string when the payload is not an address length.</returns>
        internal static string FormatIpAddress(byte[]? raw)
        {
            if (raw != null && (raw.Length == 16 || raw.Length == 4))
            {
                try { return new IPAddress(raw).ToString(); }
                catch (ArgumentException) { /* fall through — not an address after all */ }
            }

            return string.Empty;
        }

        /// <summary>
        /// Discover with default 60s timeout and encoding.
        /// </summary>
        public static IEnumerable<TikInstanceDescriptor> Discover(bool stopWhenFirstFound = false)
        {
            var encoding = Encoding.GetEncoding("iso-8859-1");
            var timeout = new TimeSpan(0, 0, 60);

            return Discover(timeout, encoding, stopWhenFirstFound);
        }

        /// <summary>
        /// Discover with specified timeout and encoding.
        /// </summary>
        /// <remarks>
        /// <para>Solicits once a second for <paramref name="timeout"/> and collects every router that answers; with
        /// <paramref name="stopWhenFirstFound"/>, returns at the first one.</para>
        /// <para>Answers are received on the calling thread, between solicitations, so a discovery does not depend on
        /// the thread pool — a receiver queued to a busy pool would not start before the window is over. The MNDP port is bound shared, so two discoveries can run at once (in this process or
        /// another): RouterOS broadcasts its answers, and every socket bound to the port receives them. A packet that
        /// is not a whole announcement — no MAC, a field cut short — is skipped rather than failing the discovery.</para>
        /// </remarks>
        public static IEnumerable<TikInstanceDescriptor> Discover(TimeSpan timeout, Encoding encoding, bool stopWhenFirstFound = false)
        {
            var result = new List<TikInstanceDescriptor>();

            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { EnableBroadcast = true })
            {
                SharePort(socket);
                socket.Bind(new IPEndPoint(IPAddress.Any, MNDP_UDP_PORT));

                var buffer = new byte[ushort.MaxValue];
                var clock = System.Diagnostics.Stopwatch.StartNew();
                long window = (long)timeout.TotalMilliseconds;
                long nextSolicitation = 0;
                while (clock.ElapsedMilliseconds < window)
                {
                    if (clock.ElapsedMilliseconds >= nextSolicitation)
                    {
                        SendMndpBroadcast();
                        nextSolicitation += SolicitationIntervalMs;
                    }

                    long waitMs = Math.Min(nextSolicitation, window) - clock.ElapsedMilliseconds;
                    if (waitMs <= 0 || !socket.Poll((int)waitMs * 1000, SelectMode.SelectRead))
                        continue;

                    int length;
                    EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                    try { length = socket.ReceiveFrom(buffer, ref from); }
                    catch (SocketException) { continue; }   // e.g. an ICMP error reported on the socket: not an answer

                    if (!TryParseResponsePacket(buffer, length, encoding, out var routerDescriptor))
                        continue;
                    if (!result.Any(r => r.IpDescription == routerDescriptor.IpDescription))
                        result.Add(routerDescriptor);
                    if (stopWhenFirstFound)
                        break;
                }
            }

            return result;
        }

        private const int SolicitationIntervalMs = 1000;

        // SO_REUSEADDR lets another socket bind the MNDP port beside this one. ExclusiveAddressUse = false only lifts
        // Windows' exclusive mode; on its own a second bind is refused (AddressAlreadyInUse). A platform that refuses
        // the option (Android) still gets a discovery, just not a shared one.
        private static void SharePort(Socket socket)
        {
            try { socket.ExclusiveAddressUse = false; }
            catch (SocketException) { } catch (PlatformNotSupportedException) { }
            try { socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true); }
            catch (SocketException) { } catch (PlatformNotSupportedException) { }
        }

        /// <summary>
        /// Sends the MNDP solicitation out of <b>every</b> eligible local interface, not just the one the
        /// host's routing table happens to pick.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The multi-homed machine that breaks discovery is usually the CLIENT, not the router. Receiving is
        /// not the problem — the listener binds <see cref="IPAddress.Any"/> and hears every interface — but a
        /// solicitation sent on an <b>unbound</b> socket leaves by whichever interface the routing table
        /// chooses for <see cref="IPAddress.Broadcast"/>, so routers on every other segment are heard from
        /// only when they happen to broadcast on their own ~30 s cycle. A short discovery window usually
        /// misses that, and the result is indistinguishable from an empty segment or a blocked firewall.
        /// </para>
        /// <para>
        /// So: one socket per candidate interface, bound to that interface's own address, addressed to that
        /// interface's SUBNET broadcast rather than the limited broadcast — the subnet form is what a bound
        /// socket can route unambiguously. The same pattern, for the same reason, is in
        /// <c>MacLayerTransport</c>, which probes each candidate NIC rather than trusting the broadcast route.
        /// </para>
        /// <para>
        /// Failures are swallowed per interface deliberately: an adapter that cannot be bound or has no route
        /// is simply not a candidate, and one of those must not stop the others from being solicited. If no
        /// interface can be enumerated at all the original unbound send is still attempted, which on a host
        /// where enumeration is unavailable beats sending nothing.
        /// </para>
        /// </remarks>
        private static void SendMndpBroadcast()
        {
            var dataToBroadcast = new byte[] { 0, 0, 0, 0 };
            int sent = 0;

            foreach (var nic in EnumerateBroadcastNics())
            {
                try
                {
                    using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { EnableBroadcast = true, ExclusiveAddressUse = false })
                    {
                        socket.Bind(new IPEndPoint(nic.Key, 0));
                        socket.SendTo(dataToBroadcast, new IPEndPoint(nic.Value, MNDP_UDP_PORT));
                        sent++;
                    }
                }
                catch (SocketException)
                {
                    // Not usable for broadcast; the other interfaces still are.
                }
            }

            if (sent == 0)
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { EnableBroadcast = true, ExclusiveAddressUse = false })
                {
                    socket.SendTo(dataToBroadcast, new IPEndPoint(IPAddress.Broadcast, MNDP_UDP_PORT));
                }
            }

            foreach (int interfaceIndex in EnumerateIPv6InterfaceIndexes())
            {
                try
                {
                    using (var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp) { EnableBroadcast = true, ExclusiveAddressUse = false })
                    {
                        socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);
                        // Which interface an ff02:: datagram leaves by is this socket option, not the route.
                        socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface,
                            IPAddress.HostToNetworkOrder(interfaceIndex));
                        socket.SendTo(dataToBroadcast, new IPEndPoint(IPAddress.Parse("ff02::1"), MNDP_UDP_PORT));
                    }
                }
                catch (SocketException)
                {
                    // As above — one interface refusing must not silence the rest.
                }
            }
        }

        /// <summary>
        /// Every local IPv4 address worth soliciting from, paired with the address to send the solicitation
        /// to: the interface is up, not loopback and not a tunnel.
        /// </summary>
        /// <remarks>
        /// <para>
        /// **Every address, not one per interface.** An interface carrying two IPv4 addresses is on two
        /// segments, and stopping at the first one leaves the second as invisible as the unbound socket left
        /// every interface but one — the same defect one level down.
        /// </para>
        /// <para>
        /// The target is that address's own subnet broadcast where one can be derived, and the limited
        /// broadcast where it cannot. An address whose <see cref="UnicastIPAddressInformation.IPv4Mask"/> is
        /// absent or all-zero is reported by real adapters (some VPN and virtual ones), and skipping it would
        /// silently drop that segment. The socket is still bound to the address either way, which is what
        /// decides the interface the datagram leaves by — the limited broadcast only loses the ability to be
        /// forwarded, which MNDP does not use.
        /// </para>
        /// </remarks>
        private static IEnumerable<KeyValuePair<IPAddress, IPAddress>> EnumerateBroadcastNics()
        {
            foreach (var ni in GetEligibleInterfaces())
            {
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;

                    yield return new KeyValuePair<IPAddress, IPAddress>(
                        ua.Address, SolicitationTarget(ua.Address, ua.IPv4Mask));
                }
            }
        }

        /// <summary>
        /// Where a solicitation sent from <paramref name="address"/> should be addressed: that address's own
        /// subnet broadcast, or the limited broadcast when no subnet can be derived.
        /// </summary>
        /// <remarks>
        /// The fallback is the point. An address whose mask is missing or all-zero is reported by real
        /// adapters, and dropping it would leave that segment as unsolicited as the unbound socket left every
        /// segment but one. Binding the socket to the address is what decides which interface the datagram
        /// leaves by; the limited broadcast merely gives up being forwarded, which MNDP does not use anyway.
        /// </remarks>
        /// <param name="address">The local address the socket will be bound to.</param>
        /// <param name="mask">The subnet mask reported for it, if any.</param>
        internal static IPAddress SolicitationTarget(IPAddress? address, IPAddress? mask)
            => SubnetBroadcast(address, mask) ?? IPAddress.Broadcast;

        /// <summary>
        /// The directed (subnet) broadcast address for an IPv4 address and its mask — the host bits set —
        /// or <c>null</c> when the pair does not describe a subnet a broadcast could be addressed to.
        /// </summary>
        /// <remarks>
        /// A mask of all zeroes is rejected rather than turned into 255.255.255.255: that is the limited
        /// broadcast, which is exactly the address whose routing this method exists to avoid depending on.
        /// </remarks>
        /// <param name="address">The interface's own IPv4 address.</param>
        /// <param name="mask">The subnet mask reported for that address.</param>
        internal static IPAddress? SubnetBroadcast(IPAddress? address, IPAddress? mask)
        {
            if (address == null || mask == null) return null;
            if (address.AddressFamily != AddressFamily.InterNetwork) return null;

            byte[] a = address.GetAddressBytes();
            byte[] m = mask.GetAddressBytes();
            if (a.Length != 4 || m.Length != 4) return null;
            if (m.All(b => b == 0)) return null;   // no usable subnet

            var broadcast = new byte[4];
            for (int i = 0; i < 4; i++)
                broadcast[i] = (byte)(a[i] | ~m[i]);

            return new IPAddress(broadcast);
        }

        /// <summary>The interface index of every eligible interface that has IPv6 enabled.</summary>
        private static IEnumerable<int> EnumerateIPv6InterfaceIndexes()
        {
            foreach (var ni in GetEligibleInterfaces())
            {
                int index;
                try { index = ni.GetIPProperties().GetIPv6Properties().Index; }
                catch (NetworkInformationException) { continue; }        // no IPv6 on this interface
                catch (PlatformNotSupportedException) { continue; }

                yield return index;
            }
        }

        /// <summary>Up, not loopback, not a tunnel — the interfaces a solicitation is worth sending from.</summary>
        private static IEnumerable<NetworkInterface> GetEligibleInterfaces()
        {
            NetworkInterface[] all;
            try { all = NetworkInterface.GetAllNetworkInterfaces(); }
            catch (NetworkInformationException) { yield break; }

            foreach (var ni in all)
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                yield return ni;
            }
        }

        //private static UdpClient CreateUdpClient()
        //{
        //    var result = new UdpClient();
        //    result.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        //    result.Client.ExclusiveAddressUse = false;
        //    result.Client.Bind(new IPEndPoint(IPAddress.Any, MNDP_UDP_PORT));

        //    //var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        //    //socket.ExclusiveAddressUse = false;
        //    ////socket.NoDelay = true;
        //    //socket.EnableBroadcast = true;

        //    //socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        //    //socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.NoDelay, 1);
        //    //socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);
        //    //socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontRoute, true);


        //    //socket.Bind(new IPEndPoint(IPAddress.Any, MNDP_UDP_PORT));
        //    //result.Client = socket;

        //    //Not possible to use on android :-/
        //    //result.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        //    //result.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.NoDelay, 1);
        //    //result.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);
        //    //result.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontRoute, true);

        //    //result.Client.ExclusiveAddressUse = false;
        //    //result.Client.NoDelay = true;
        //    //result.Client.EnableBroadcast = true;
        //    //DontRoute ??

        //    return result;
        //}

        //private static void SendMndpBroadcasts(UdpClient udpClient)
        //{
        //    for (int i = 0; i < 3; i++) //send a few packets
        //    {
        //        //inspiration: https://hadler.me/cc/mikrotik-neighbor-discovery-mndp/
        //        var dataToBroadcast = new byte[] { 0, 0, 0, 0 };

        //        //using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { EnableBroadcast = true, ExclusiveAddressUse = false })
        //        {

        //            //socket.SendTo(dataToBroadcast, new IPEndPoint(IPAddress.Broadcast, MNDP_UDP_PORT));
        //        }
        //        //udpClient.SendAsync(dataToBroadcast, dataToBroadcast.Length, new IPEndPoint(IPAddress.Broadcast, MNDP_UDP_PORT));
        //    }
        //}

        /// <summary>
        /// Reads an MNDP announcement: a 4-byte header, then type-length-value items (big-endian type and length).
        /// </summary>
        /// <remarks>
        /// Only the MAC (item 1) makes a packet an announcement; it is what the result is used for. Every other item
        /// is read when present and is empty otherwise — the solicitation itself (4 zero bytes, received back from the
        /// broadcast) and a packet whose items stop short are not announcements, and a repeated item keeps its last
        /// value. Anything else on the MNDP port is not this library's to fail on.
        /// </remarks>
        private static bool TryParseResponsePacket(byte[] data, int length, Encoding encoding, out TikInstanceDescriptor routerDescriptor)
        {
            routerDescriptor = default(TikInstanceDescriptor);

            var items = new Dictionary<ushort, byte[]>();
            int pos = 4;   // type, ttl, sequence
            while (pos + 4 <= length)
            {
                ushort itemType = (ushort)(data[pos] << 8 | data[pos + 1]);
                int itemSize = data[pos + 2] << 8 | data[pos + 3];
                pos += 4;
                if (pos + itemSize > length)
                    break;   // cut short: keep the items before it
                var itemData = new byte[itemSize];
                Array.Copy(data, pos, itemData, 0, itemSize);
                items[itemType] = itemData;
                pos += itemSize;
            }

            if (!items.TryGetValue(1, out byte[]? macBytes) || macBytes.Length == 0)
                return false;

            string Text(ushort type) => items.TryGetValue(type, out byte[]? v) ? encoding.GetString(v) : string.Empty;

            var mac = string.Join(":", macBytes.Select(b => b.ToString("X2")).ToArray());                                // 1  = MAC
            var uptime = items.TryGetValue(10, out byte[]? up) && up.Length >= 4
                ? TimeSpan.FromSeconds(BitConverter.ToUInt32(up, 0)) : TimeSpan.Zero;                                   // 10 = Uptime
            var ipv6 = items.TryGetValue(15, out byte[]? v6) ? FormatIpAddress(v6) : string.Empty;                      // 15 = IPv6
            var ipv4 = items.TryGetValue(17, out byte[]? v4) && v4.Length == 4 ? new IPAddress(v4) : IPAddress.Any;     // 17 = IPv4

            routerDescriptor = new TikInstanceDescriptor(
                Text(5), Text(7), Text(8), uptime, Text(11), Text(12), Text(14),                                        // identity, version, platform, -, software id, board, unpack
                mac, ipv6, Text(16), ipv4);                                                                              // -, -, interface
            return true;
        }
    }

}
