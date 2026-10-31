using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.MacTelnet;

namespace tik4net.unittests.MacTelnet
{
    /// <summary>
    /// The screen a MAC-Telnet login lands on after EC-SRP5 (review D2): the change-password nag is answered with
    /// Ctrl-C up to three times, as on the WinBox CLI. It used to be answered once; a nag that came back was waited on
    /// until the login timed out.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class MacTelnetLoginScreenTests
    {
        private const string Nag = "\r\nChange your password\r\nnew password> ";
        private const string Prompt = "\r\n[admin@MikroTik] > ";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

        [TestMethod]
        public async Task ANagThatComesBackTwice_IsAnsweredThreeTimes_ThenThePrompt()
        {
            Assert.AreEqual(3, await CtrlCsBeforeThePrompt(Nag, Nag, Nag, Prompt));
        }

        [TestMethod]
        public async Task ANagThatPersists_EndsTheLogin_AfterThreeCtrlCs()
        {
            var ex = await Assert.ThrowsExceptionAsync<TikConnectionLoginException>(
                () => CtrlCsBeforeThePrompt(Nag, Nag, Nag, Nag, Nag));
            StringAssert.Contains(ex.Message, "new password");
        }

        // Runs the client's prompt wait against a scripted router: the first screen, then the next one after each
        // Ctrl-C. Answers how many Ctrl-Cs the client sent.
        private static async Task<int> CtrlCsBeforeThePrompt(string first, params string[] afterEachCtrlC)
        {
            using (var router = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            using (var client = new MacTelnetUdpClient(Encoding.UTF8, CliClientTimeouts.Fixed(4000, 4000), null))
            {
                var peer = (IPEndPoint)router.Client.LocalEndPoint;
                var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
                Set(client, "_udp", udp);
                Set(client, "_routerEp", peer);
                Set(client, "_routerUnicastEp", peer);
                Set(client, "_localMac", new byte[] { 0x02, 0, 0, 0, 0, 0x01 });
                Set(client, "_routerMac", new byte[] { 0x02, 0, 0, 0, 0, 0x02 });
                var to = (IPEndPoint)udp.Client.LocalEndPoint;

                var screens = new Queue<string>(afterEachCtrlC);
                uint counter = 0;
                int ctrlCs = 0;
                var seen = new HashSet<uint>();
                void Screen(string text)
                {
                    byte[] payload = Encoding.UTF8.GetBytes(text);
                    byte[] pkt = new byte[22 + payload.Length];
                    pkt[0] = 1; pkt[1] = 1;
                    pkt[2] = 0x02; pkt[7] = 0x02; pkt[8] = 0x02; pkt[13] = 0x01;
                    pkt[18] = (byte)(counter >> 24); pkt[19] = (byte)(counter >> 16); pkt[20] = (byte)(counter >> 8); pkt[21] = (byte)counter;
                    Buffer.BlockCopy(payload, 0, pkt, 22, payload.Length);
                    counter += (uint)payload.Length;
                    router.Send(pkt, pkt.Length, to);
                }

                var wait = Task.Run(() =>
                {
                    try { typeof(MacTelnetUdpClient).GetMethod("WaitForPromptSync", Flags).Invoke(client, null); }
                    catch (TargetInvocationException ex) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); }
                });
                Screen(first);

                router.Client.ReceiveTimeout = 200;
                while (!wait.IsCompleted)
                {
                    IPEndPoint ep = null;
                    byte[] pkt;
                    try { pkt = router.Receive(ref ep); }
                    catch (SocketException) { continue; }
                    uint at = ((uint)pkt[18] << 24) | ((uint)pkt[19] << 16) | ((uint)pkt[20] << 8) | pkt[21];
                    if (pkt.Length == 23 && pkt[1] == 1 && pkt[22] == 0x03 && seen.Add(at))   // a retransmit is not another Ctrl-C
                    {
                        ctrlCs++;
                        if (screens.Count > 0) Screen(screens.Dequeue());
                    }
                }
                await wait;
                return ctrlCs;
            }
        }

        private static void Set(object o, string field, object value)
            => typeof(MacLayerTransport).GetField(field, Flags).SetValue(o, value);
    }
}
