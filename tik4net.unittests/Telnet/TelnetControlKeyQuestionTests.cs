using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Telnet;

namespace tik4net.unittests.Telnet
{
    /// <summary>
    /// A control key answered by a question returns at the question, not at the receive deadline. A local listener
    /// stands in for RouterOS 6.49.13 answering the Safe Mode key while another session holds Safe Mode.
    /// </summary>
    [TestClass]
    [DoNotParallelize]   // a local listener answers within a time budget
    public class TelnetControlKeyQuestionTests
    {
        [TestMethod]
        public void TheSafeModeKey_ReturnsAtTheHijackQuestion()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var client = new TelnetClient(Encoding.ASCII, 5000, 5000);
                client.Connect(IPAddress.Loopback.ToString(), ((IPEndPoint)listener.LocalEndpoint).Port, 2000);
                using (Socket router = listener.AcceptSocket())
                {
                    var sw = Stopwatch.StartNew();
                    var reading = client.SendRawAndReadAsync(new byte[] { 0x18 }, CancellationToken.None);

                    var key = new byte[16];
                    int n = router.Receive(key);
                    Assert.AreEqual(0x18, key[n - 1]);
                    router.Send(Encoding.ASCII.GetBytes(
                        "\r\nHijacking Safe Mode from someone - unroll/release/don't take it [u/r/d]: "));

                    Assert.IsTrue(reading.Wait(3000), "the read waited for a prompt the question never gets");
                    StringAssert.Contains(reading.Result, "[u/r/d]");
                    Assert.IsTrue(sw.ElapsedMilliseconds < 3000, sw.ElapsedMilliseconds + " ms");
                }
                client.Close();
            }
            finally { listener.Stop(); }
        }
    }
}
