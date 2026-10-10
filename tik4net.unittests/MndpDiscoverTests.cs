using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Mndp;

namespace tik4net.unittests
{
    /// <summary>
    /// <see cref="MndpHelper.Discover(TimeSpan, Encoding, bool)"/> against announcements a local thread sends to the
    /// loopback address: two discoveries at once, a starved thread pool, and packets that are not whole announcements.
    /// </summary>
    [TestClass]
    [DoNotParallelize]   // binds the MNDP port, changes the thread pool's limits, and runs within a time window
    public class MndpDiscoverTests
    {
        private static readonly Encoding Latin1 = Encoding.GetEncoding("iso-8859-1");
        private static readonly IPAddress AnnouncedIPv4 = IPAddress.Parse("192.0.2.7");
        private static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

        [TestMethod]
        public void TwoDiscoveriesAtOnce_BothRun()
        {
            using (Announce(Announcement()))
            {
                var errors = new List<Exception>();
                var threads = Enumerable.Range(0, 2).Select(_ => new Thread(() =>
                {
                    try { MndpHelper.Discover(Window, Latin1).ToList(); }
                    catch (Exception ex) { lock (errors) errors.Add(ex); }
                })).ToList();
                threads.ForEach(t => t.Start());
                threads.ForEach(t => Assert.IsTrue(t.Join(10000), "a discovery did not return"));
                Assert.AreEqual(0, errors.Count, string.Join(Environment.NewLine, errors));
            }
        }

        [TestMethod]
        public void AStarvedThreadPool_DoesNotStopTheDiscovery()
        {
            ThreadPool.GetMaxThreads(out int maxWorkers, out int maxIo);
            ThreadPool.GetMinThreads(out int minWorkers, out _);
            var release = new ManualResetEventSlim(false);
            try
            {
                Assert.IsTrue(ThreadPool.SetMaxThreads(minWorkers, maxIo));
                ThreadPool.GetAvailableThreads(out int free, out _);
                using (var started = new CountdownEvent(free))
                {
                    for (int i = 0; i < free; i++)
                        ThreadPool.QueueUserWorkItem(_ => { started.Signal(); release.Wait(); });
                    Assert.IsTrue(started.Wait(10000), "the pool was not filled");
                }

                using (Announce(Announcement()))
                {
                    // A thread of its own, so a discovery waiting for the pool fails the test rather than hanging it.
                    List<TikInstanceDescriptor> found = null;
                    Exception error = null;
                    var discovery = new Thread(() =>
                    {
                        try { found = MndpHelper.Discover(Window, Latin1).ToList(); }
                        catch (Exception ex) { error = ex; }
                    }) { IsBackground = true };
                    discovery.Start();
                    Assert.IsTrue(discovery.Join(Window + TimeSpan.FromSeconds(5)), "the discovery waited for the thread pool");
                    Assert.IsNull(error, error?.ToString());
                    Assert.IsTrue(found.Any(r => AnnouncedIPv4.Equals(r.IPv4)), "the announcement was not received");
                }
            }
            finally
            {
                release.Set();
                ThreadPool.SetMaxThreads(maxWorkers, maxIo);
            }
        }

        [TestMethod]
        public void PacketsThatAreNotWholeAnnouncements_AreSkipped()
        {
            byte[] noMac = Packet(Tlv(5, Latin1.GetBytes("no-mac-here")), Tlv(16, Latin1.GetBytes("ether1")));
            byte[] duplicate = Packet(Tlv(5, Latin1.GetBytes("twice")), Tlv(5, Latin1.GetBytes("twice")), Tlv(7, new byte[8]));
            byte[] cutInATlvHeader = Packet(Tlv(5, Latin1.GetBytes("cut-short-at-the-end")), new byte[] { 0x00 });

            using (Announce(noMac, duplicate, cutInATlvHeader, Announcement()))
            {
                var found = MndpHelper.Discover(Window, Latin1).ToList();
                Assert.IsTrue(found.Any(r => AnnouncedIPv4.Equals(r.IPv4)), "the whole announcement was not received");
            }
        }

        [TestMethod]
        public void AnAnnouncementWithoutItsOptionalFields_IsStillARouter()
        {
            // Only the MAC and the IPv4 address: every text field reads as empty.
            byte[] sparse = Packet(Tlv(1, new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF }), Tlv(17, AnnouncedIPv4.GetAddressBytes()),
                                   Tlv(5, Latin1.GetBytes("sparse")));
            using (Announce(sparse))
            {
                var found = MndpHelper.Discover(Window, Latin1).ToList();
                var router = found.SingleOrDefault(r => AnnouncedIPv4.Equals(r.IPv4));
                Assert.AreEqual("sparse", router.Identity);
                Assert.AreEqual("AA:BB:CC:DD:EE:FF", router.Mac);
                Assert.AreEqual(string.Empty, router.Version);
            }
        }

        // A thread of its own (not the pool) sends the packets to the MNDP port on loopback every 100 ms until disposed.
        private static IDisposable Announce(params byte[][] packets)
        {
            var stop = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    var target = new IPEndPoint(IPAddress.Loopback, 5678);
                    while (!stop.Wait(100))
                        foreach (byte[] p in packets)
                            socket.SendTo(p, target);
                }
            }) { IsBackground = true };
            thread.Start();
            return new Stopper(() => { stop.Set(); thread.Join(2000); });
        }

        private sealed class Stopper : IDisposable
        {
            private readonly Action _stop;
            public Stopper(Action stop) => _stop = stop;
            public void Dispose() => _stop();
        }

        private static byte[] Announcement() => Packet(
            Tlv(1, new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF }),
            Tlv(5, Latin1.GetBytes("fake-router")),
            Tlv(7, Latin1.GetBytes("7.24")),
            Tlv(8, Latin1.GetBytes("MikroTik")),
            Tlv(10, BitConverter.GetBytes((uint)3600)),
            Tlv(11, Latin1.GetBytes("ABCD-1234")),
            Tlv(12, Latin1.GetBytes("CHR")),
            Tlv(14, new byte[] { 0x01 }),
            Tlv(16, Latin1.GetBytes("ether1")),
            Tlv(17, AnnouncedIPv4.GetAddressBytes()));

        private static byte[] Packet(params byte[][] tlvs)
        {
            using (var ms = new MemoryStream())
            {
                ms.Write(new byte[] { 0, 0, 0, 1 }, 0, 4);   // type, ttl, sequence
                foreach (byte[] tlv in tlvs)
                    ms.Write(tlv, 0, tlv.Length);
                return ms.ToArray();
            }
        }

        private static byte[] Tlv(ushort type, byte[] data)
        {
            var tlv = new byte[4 + data.Length];
            tlv[0] = (byte)(type >> 8); tlv[1] = (byte)type;
            tlv[2] = (byte)(data.Length >> 8); tlv[3] = (byte)data.Length;
            Array.Copy(data, 0, tlv, 4, data.Length);
            return tlv;
        }
    }
}
