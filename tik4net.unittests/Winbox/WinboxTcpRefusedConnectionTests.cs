using System.IO;
using System.Net;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    /// <summary>
    /// A WinBox connection the router closes before sending a single byte names the case RouterOS uses it for.
    /// </summary>
    /// <remarks>
    /// RouterOS refuses a WinBox connection over its service's <c>max-sessions</c> by accepting it and resetting it at
    /// once, and logs <c>too many winbox/dude sessions, drop connect from &lt;client&gt;</c>. The client saw only
    /// "Connection closed unexpectedly", 20–100 times in a burst (7.24, RoMON relay churn). A local listener stands in.
    /// </remarks>
    [TestClass]
    [DoNotParallelize]   // a local listener answers within a receive timeout
    public class WinboxTcpRefusedConnectionTests
    {
        private static IOException ReadAfter(System.Action<Socket> router)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                using (var transport = new WinboxTcpTransport())
                {
                    transport.Connect(IPAddress.Loopback.ToString(), ((IPEndPoint)listener.LocalEndpoint).Port, 2000, 3000);
                    using (Socket accepted = listener.AcceptSocket())
                        router(accepted);
                    try { transport.ReadExact(64); }
                    catch (IOException ex) { return ex; }
                    Assert.Fail("the read did not fail");
                    return null;
                }
            }
            finally { listener.Stop(); }
        }

        [TestMethod]
        public void AConnectionClosedBeforeAnyByte_NamesTheSessionLimit()
        {
            var ex = ReadAfter(s => s.Shutdown(SocketShutdown.Both));
            StringAssert.Contains(ex.Message, "before sending anything");
            StringAssert.Contains(ex.Message, "max-sessions");
        }

        [TestMethod]
        public void AConnectionResetBeforeAnyByte_NamesTheSessionLimit()
        {
            var ex = ReadAfter(s => s.LingerState = new LingerOption(true, 0));   // the close that follows is a reset
            StringAssert.Contains(ex.Message, "before sending anything");
            StringAssert.Contains(ex.Message, "max-sessions");
        }

        [TestMethod]
        public void AConnectionClosedMidFrame_KeepsTheGenericMessage()
        {
            var ex = ReadAfter(s => { s.Send(new byte[] { 1, 2, 3 }); s.Shutdown(SocketShutdown.Both); });
            Assert.IsFalse(ex.Message.Contains("max-sessions"), ex.Message);
        }
    }
}
