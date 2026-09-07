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
        public static IEnumerable<TikInstanceDescriptor> Discover(TimeSpan timeout, Encoding encoding, bool stopWhenFirstFound = false)
        {
            var result = new List<TikInstanceDescriptor>();
            var receiveEndpoint = new IPEndPoint(IPAddress.Any, MNDP_UDP_PORT);

            using (var udpClient = new UdpClient() { EnableBroadcast = true, ExclusiveAddressUse = false, MulticastLoopback = true })            
            {
                udpClient.Client.Bind(receiveEndpoint);
                using (var cancelSource = new CancellationTokenSource())
                {
                    //start async receive
                    var receiveCancellToken = cancelSource.Token;
                    var receivingTask = Task.Run(() =>
                    {
                        try
                        {
                            while (!receiveCancellToken.IsCancellationRequested)
                            {
                                var receiveBufferTask = udpClient.ReceiveAsync();
                                receiveBufferTask.ConfigureAwait(false);
                                receiveBufferTask.Wait(receiveCancellToken);
                                if (!receiveBufferTask.IsCanceled)
                                {
                                    var data = receiveBufferTask.Result.Buffer;
                                    if (TryParseResponsePacket(data, encoding, out var routerDescriptor))
                                    {
                                        if (!result.Any(r => r.IpDescription == routerDescriptor.IpDescription))
                                            result.Add(routerDescriptor);
                                    }
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                        }
                    }, receiveCancellToken);

                    //send broadcast
                    const int BROADCAST_DELAY = 1000;
                    for (int i = 0; i < timeout.TotalMilliseconds / BROADCAST_DELAY; i++)
                    {
                        SendMndpBroadcast();
                        
                        Thread.Sleep(BROADCAST_DELAY);
                        if (stopWhenFirstFound && result.Count > 0)
                            break;
                    }

                    cancelSource.Cancel();
                    receivingTask.Wait();
                }
            }            

            return result;
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
        /// The local IPv4 address and subnet broadcast address of every interface worth soliciting from:
        /// up, not loopback, not a tunnel, carrying an IPv4 address with a mask to derive the broadcast from.
        /// </summary>
        private static IEnumerable<KeyValuePair<IPAddress, IPAddress>> EnumerateBroadcastNics()
        {
            foreach (var ni in GetEligibleInterfaces())
            {
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (ua.IPv4Mask == null) continue;

                    var broadcast = SubnetBroadcast(ua.Address, ua.IPv4Mask);
                    if (broadcast == null) continue;

                    yield return new KeyValuePair<IPAddress, IPAddress>(ua.Address, broadcast);
                    break;   // one address per interface is enough to reach its segment
                }
            }
        }

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

        private static bool TryParseResponsePacket(byte[] data, Encoding encoding, out TikInstanceDescriptor routerDescriptor)
        {
            if (data == null || data.Length < 18)
            {
                routerDescriptor = default(TikInstanceDescriptor);
                return false; //malformed response (or request message itself)
            }

            //parse
            using (var stream = new MemoryStream(data))
            {
                using (var reader = new BinaryReader(stream))
                {
                    //Message header
                    var type = reader.ReadByte();     // 0. byte   = TYPE
                    var ttl = reader.ReadByte();      // 1. byte   = TTL
                    var sequence = reader.ReadWord(); // 2-3. byte = SEQUENCE

                    //Message items 
                    var messageItems = new Dictionary<UInt16, byte[]>();
                    while (reader.BaseStream.Position < data.Length)
                    {
                        var itemType = reader.ReadWord();          //x+0-1           = ITEM_TYPE
                        var itemSize = reader.ReadWord();          //x+2-3           = ITEM_SIZE
                        var itemData = reader.ReadBytes(itemSize); //X+4-x+ITEM_SIZE = ITEM_DATA

                        messageItems.Add(itemType, itemData);
                    }

                    // MessageItems -> Data
                    var mac = string.Join(":", messageItems[1].Select(b => b.ToString("X2")).ToArray());             // 1  = MAC
                    var identity = encoding.GetString(messageItems[5]);                                              // 5  = Identity
                    var version = encoding.GetString(messageItems[7]);                                               // 7  = Version
                    var platform = encoding.GetString(messageItems[8]);                                              // 8  = Platform
                    var uptime = TimeSpan.FromSeconds(BitConverter.ToUInt32(messageItems[10], 0));                   // 10 = Uptime
                    var softwareId = encoding.GetString(messageItems[11]);                                           // 11 = SoftwareId
                    var boardName = encoding.GetString(messageItems[12]);                                            // 12 = BoardName
                    var unpack = encoding.GetString(messageItems[14]);                                               // 14 = Unpack ???
                    var IPV6 = messageItems.ContainsKey(15) ? FormatIpAddress(messageItems[15]) : string.Empty;       // 15 = IPV6 (optional)
                    var interfaceName = encoding.GetString(messageItems[16]);                                        // 16 = InterfaceName
                    var IPV4 = messageItems.ContainsKey(17) ? new IPAddress(messageItems[17]) : IPAddress.Any;       // 17 = IPV4 (optional)    

                    routerDescriptor = new TikInstanceDescriptor(identity, version, platform, uptime, softwareId, boardName, unpack, mac, IPV6, interfaceName, IPV4);
                    return true;
                }
            }
        }
    }

}
