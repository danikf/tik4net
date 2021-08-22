using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

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

        //NOTE: supporting of netstandard 1.3 and other old frameworks made it harder to implement :-/

        private const int MNDP_UDP_PORT = 5678;

        //private class DiscoveryState
        //{
        //    public volatile bool ShouldStop;
        //}

        /// <summary>
        /// Discover with default timeout and encoding.
        /// </summary>
        public static IEnumerable<TikInstanceDescriptor> Discover()
        {
            var encoding = Encoding.GetEncoding("iso-8859-1");
            var timeout = new TimeSpan(0, 0, 2);

            return Discover(timeout, encoding);
        }

        /// <summary>
        /// Discover with specified timeout and encoding.
        /// </summary>
        public static IEnumerable<TikInstanceDescriptor> Discover(TimeSpan timeout, Encoding encoding)
        {
            var result = new List<TikInstanceDescriptor>();

            using (var udpClient = CreateUdpClient())
            {
                //Broadcast datagram (more than once) -> BroadCast:5678
                SendMndpBroadcast(udpClient);

                var shouldStop = false;
                var receivingThread = new Thread(() =>
                {
                    while (!shouldStop)
                    {
#if NET20 || NET35 || NET40
                        var from = new IPEndPoint(0, 0);
                        var data = udpClient.Receive(ref from);
#else
                        var asyncUdp = udpClient.ReceiveAsync();
                        byte[] data = null;
                        if (asyncUdp.Wait((int)timeout.TotalMilliseconds))
                        {
                            data = asyncUdp.Result.Buffer;
                        }
#endif
                        if (TryParseResponsePacket(data, encoding, out var routerDescriptor))
                        {
                            if (!result.Any(r => r.IpDescription == routerDescriptor.IpDescription))
                                result.Add(routerDescriptor);
                        }
                    }
                });
                receivingThread.IsBackground = true;
                receivingThread.Start();
                Thread.Sleep((int)timeout.TotalMilliseconds);
                shouldStop = true;
                receivingThread.Join((int)timeout.TotalMilliseconds);
            }

            return result;
        }

        private static UdpClient CreateUdpClient()
        {
            var result = new UdpClient();
            result.Client.Bind(new IPEndPoint(IPAddress.Any, MNDP_UDP_PORT));

            //Not possible to use on android :-/
            //result.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            //result.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.NoDelay, 1);
            //result.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);
            //result.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontRoute, true);

            result.Client.ExclusiveAddressUse = false;
            result.Client.NoDelay = true;
            result.Client.EnableBroadcast = true;
            //DontRoute ??

            return result;
        }

        private static void SendMndpBroadcast(UdpClient udpClient)
        {
            for (int i = 0; i < 3; i++) //send a few packets
            {
                //inspiration: https://hadler.me/cc/mikrotik-neighbor-discovery-mndp/
                var dataToBroadcast = new byte[] { 0, 0, 0, 0 };

#if NET20 || NET35 || NET40
                udpClient.Send(dataToBroadcast, dataToBroadcast.Length, new IPEndPoint(IPAddress.Broadcast, MNDP_UDP_PORT));
#else
                udpClient.SendAsync(dataToBroadcast, dataToBroadcast.Length, new IPEndPoint(IPAddress.Broadcast, MNDP_UDP_PORT));
#endif
            }
        }

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
                    var IPV6 = messageItems.ContainsKey(15) ? encoding.GetString(messageItems[15]) : string.Empty;    // 15 = IPV6 (optional)
                    var interfaceName = encoding.GetString(messageItems[16]);                                        // 16 = InterfaceName
                    var IPV4 = messageItems.ContainsKey(17) ? new IPAddress(messageItems[17]) : IPAddress.Any;       // 17 = IPV4 (optional)    

                    routerDescriptor = new TikInstanceDescriptor(identity, version, platform, uptime, softwareId, boardName, unpack, mac, IPV6, interfaceName, IPV4);
                    return true;
                }
            }
        }
    }

}
