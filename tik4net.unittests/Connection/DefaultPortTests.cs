#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;
using tik4net.Ssh;
using tik4net.WinboxNativeMac;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// The parameterless <c>Open(host, user, password)</c> dials each transport's default port: a loopback listener on
    /// that port sees the connection arrive. A port this machine cannot listen on (in use, or below 1024 without the
    /// right to bind it) is reported and left unchecked.
    /// </summary>
    [TestClass]
    [DoNotParallelize]   // listens on fixed loopback ports
    public class DefaultPortTests
    {
        // The MAC-layer transports have no port to dial (UDP 20561 is fixed), so only the TCP ones can be watched.
        private static readonly Dictionary<TikConnectionType, int> TcpDefaults = new Dictionary<TikConnectionType, int>
        {
            [TikConnectionType.Api] = 8728,
            [TikConnectionType.ApiSsl] = 8729,
            [TikConnectionType.Rest] = 80,
            [TikConnectionType.RestSsl] = 443,
            [TikConnectionType.Ssh] = 22,
            [TikConnectionType.Telnet] = 23,
            [TikConnectionType.WinboxCli] = 8291,
            [TikConnectionType.WinboxNative] = 8291,
        };

        [ClassInitialize]
        public static void RegisterSatelliteTransports(TestContext context) => Tik4NetSsh.Register();

        [TestMethod]
        public void TheParameterlessOpenDialsTheTransportsDefaultPort()
        {
            var unverifiable = new List<string>();
            foreach (var pair in TcpDefaults)
            {
                var listener = new TcpListener(IPAddress.Loopback, pair.Value);
                try { listener.Start(); }
                catch (SocketException ex)
                {
                    unverifiable.Add(pair.Key + " (" + pair.Value + ": " + ex.SocketErrorCode + ")");
                    continue;
                }
                try
                {
                    var accepted = listener.AcceptTcpClientAsync();
                    using (var connection = TikConnectionRegistry.Create(pair.Key))
                    {
                        connection.ConnectTimeout = TimeSpan.FromSeconds(5);
                        connection.ReceiveTimeout = TimeSpan.FromSeconds(1);
                        // The open fails (nothing answers the login); what matters is where it went.
                        var open = Task.Run(() => { try { connection.Open("127.0.0.1", "admin", ""); } catch { } });
                        Assert.IsTrue(accepted.Wait(TimeSpan.FromSeconds(10)), pair.Key + " did not dial " + pair.Value);
                        accepted.Result.Dispose();
                        open.Wait(TimeSpan.FromSeconds(15));
                    }
                }
                finally
                {
                    listener.Stop();
                }
            }
            if (unverifiable.Count == TcpDefaults.Count)
                Assert.Inconclusive("no default port can be listened on here: " + string.Join(", ", unverifiable));
            if (unverifiable.Count > 0)
                Console.WriteLine("not checked: " + string.Join(", ", unverifiable));
        }

        [TestMethod]
        public void WinboxNativeMacsDefaultPortIsTheMacLayersPort()
            => Assert.AreEqual(20561, WinboxNativeMacConnection.DefaultPort);
    }
}
