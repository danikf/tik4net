using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.MacTelnet;

namespace tik4net.unittests.MacTelnet
{
    /// <summary>
    /// How many wide gaps — a DATA packet arriving more than one datagram past the last byte held — condemn a MAC-Telnet
    /// answer that ends at a prompt with an unbroken byte counter.
    /// </summary>
    /// <remarks>
    /// The measured short answers had 4, 29, 30 and 31 wide gaps; every complete answer condemned since counted reads
    /// were exempted had exactly two (an <c>add</c> the router had executed, and two 3 4xx-character answers whose length
    /// <c>TerminalFrameBoundaryTest</c> checks). Three keeps a margin below the smallest real case (X27, D29).
    /// </remarks>
    [TestClass]
    public class MacTelnetLostDatagramsTests
    {
        private const string Answer = "#e=1234\r\n[admin@CHR] > ";

        [TestMethod]
        public void OneWideGap_DoesNotCondemnTheAnswer()
        {
            MacTelnetUdpClient.ThrowIfResponseLostDatagrams(1, "/ip address add address=192.168.4.250/24", Answer);
        }

        [TestMethod]
        public void TwoWideGaps_DoNotCondemnACompleteAnswer()
        {
            MacTelnetUdpClient.ThrowIfResponseLostDatagrams(2, "/ip address add address=192.168.4.250/24", Answer);
        }

        [TestMethod]
        public void ThreeWideGaps_CondemnTheAnswer()
        {
            var ex = Assert.ThrowsException<TikConnectionResponseIncompleteException>(
                () => MacTelnetUdpClient.ThrowIfResponseLostDatagrams(3, "/ip address add address=192.168.4.250/24 ", Answer));

            Assert.AreEqual(3, ex.LostDatagrams);
            StringAssert.Contains(ex.Message,
                "MAC-Telnet: the router ran a backlog past this client 3 times while answering "
                + "'/ip address add address=192.168.4.250/24'");
            StringAssert.Contains(ex.Message, "The " + Answer.Length + " characters received therefore end at a prompt");
        }
    }
}
