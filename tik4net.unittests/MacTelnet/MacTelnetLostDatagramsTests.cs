using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.MacTelnet;

namespace tik4net.unittests.MacTelnet
{
    /// <summary>
    /// Which MAC-Telnet answers that end at a prompt with an unbroken byte counter are condemned for the wide gaps — a
    /// DATA packet arriving more than one datagram past the last byte held — they crossed on the way.
    /// </summary>
    /// <remarks>
    /// Size decides first: RouterOS drops part of its own output only past about 70 KB of an answer (measured on 6.49.13,
    /// 7.21.5 and 7.24.5), and one early lost datagram scores a complete answer as many wide gaps as a short one, so an
    /// answer under <see cref="MacTelnetUdpClient.MinimumCondemnedLength"/> is never condemned (X27, D33). Above it the
    /// measured short answers had 4, 29, 30 and 31 wide gaps, and three keeps a margin below the smallest (D29).
    /// </remarks>
    [TestClass]
    public class MacTelnetLostDatagramsTests
    {
        private const string Prompt = "\r\n[admin@CHR] > ";
        private const string Answer = "#e=1234" + Prompt;
        private const string Command = "/ip address add address=192.168.4.250/24";

        private static string AnswerOf(int length) => new string('x', length - Prompt.Length) + Prompt;

        [TestMethod]
        public void OneWideGap_DoesNotCondemnTheAnswer()
        {
            MacTelnetUdpClient.ThrowIfResponseLostDatagrams(1, Command, AnswerOf(MacTelnetUdpClient.MinimumCondemnedLength));
        }

        [TestMethod]
        public void TwoWideGaps_DoNotCondemnACompleteAnswer()
        {
            MacTelnetUdpClient.ThrowIfResponseLostDatagrams(2, Command, AnswerOf(MacTelnetUdpClient.MinimumCondemnedLength));
        }

        [TestMethod]
        public void ThreeWideGaps_CondemnALargeAnswer()
        {
            string answer = AnswerOf(MacTelnetUdpClient.MinimumCondemnedLength);
            var ex = Assert.ThrowsException<TikConnectionResponseIncompleteException>(
                () => MacTelnetUdpClient.ThrowIfResponseLostDatagrams(3, Command + " ", answer));

            Assert.AreEqual(3, ex.LostDatagrams);
            StringAssert.Contains(ex.Message,
                "MAC-Telnet: the router ran a backlog past this client 3 times while answering '" + Command + "'");
            StringAssert.Contains(ex.Message, "The " + answer.Length + " characters received therefore end at a prompt");
        }

        [TestMethod]
        public void ManyWideGaps_DoNotCondemnAShortAnswer()
        {
            // X27: a complete 3 444-character answer refused at three; the measured complete answers scored up to 47.
            MacTelnetUdpClient.ThrowIfResponseLostDatagrams(47, Command, AnswerOf(3444));
            MacTelnetUdpClient.ThrowIfResponseLostDatagrams(3, Command, Answer);
        }

        [TestMethod]
        public void TheFloorIsTheFirstLengthCondemned()
        {
            MacTelnetUdpClient.ThrowIfResponseLostDatagrams(31, Command, AnswerOf(MacTelnetUdpClient.MinimumCondemnedLength - 1));
            Assert.ThrowsException<TikConnectionResponseIncompleteException>(
                () => MacTelnetUdpClient.ThrowIfResponseLostDatagrams(31, Command, AnswerOf(MacTelnetUdpClient.MinimumCondemnedLength)));
        }
    }
}
