using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    /// <summary>
    /// A receive timeout on the binary API has to say which of three things happened, because they want
    /// opposite fixes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A sentence reaches a caller only once it is <b>whole</b>, so "no response received within 30000 ms,
    /// 550 sentence(s) had already arrived" described a router that stopped answering mid-table and a
    /// router still delivering an enormous row identically — the first is a router-side stall to work
    /// around, the second is a deadline set too short. A third case reads the same from the tag's point of
    /// view: the socket busy with another tag while this one starves.
    /// </para>
    /// <para>
    /// The two socket counters separate them. This is the binary-API counterpart of the WinBox M2 channel's
    /// <c>BytesReceived</c>, where the same distinction is what settled the paged-read stall
    /// (<c>Docs/winbox-native-m2-protocol.md</c> §29) — an unattributed stall there cost several
    /// investigations that each had to re-establish which half was happening.
    /// </para>
    /// </remarks>
    [TestClass]
    public class ApiReadStallAttributionTests
    {
        private const string TestUser = "admin";
        private const string TestPassword = "secret";
        private const int ReceiveTimeoutMs = 1500;

        /// <summary>
        /// The router answers part of the table and then goes quiet: not one byte since the last row.
        /// </summary>
        [TestMethod]
        public void ARouterThatStopsAnsweringMidTableIsReportedAsSilent()
        {
            using (var server = new FakeRouterServer())
            {
                var serverTask = Task.Run(() =>
                {
                    server.AcceptClient();
                    server.ReadSentence();                          // login
                    server.WriteSentence("!done");
                    server.ReadSentence();                          // /interface/print
                    for (int i = 0; i < 3; i++)
                        server.WriteSentence("!re", "=name=ether" + i);
                    Thread.Sleep(ReceiveTimeoutMs * 3);             // …and then nothing, without hanging up
                });

                string message = TimeOutAPrint(server);

                Assert.IsTrue(message.Contains("3 sentence(s) had already arrived"),
                    "the rows that did arrive are what makes this a partial answer rather than no answer: "
                    + message);

                int lastByteAge = AgeFromMessage(message, "last byte");
                int lastSentenceAge = AgeFromMessage(message, "last complete sentence");

                Assert.IsTrue(lastByteAge > ReceiveTimeoutMs / 2,
                    $"nothing arrived during the wait, so the last byte must be reported as old — got "
                    + $"{lastByteAge} ms in: {message}");
                Assert.IsTrue(lastSentenceAge > ReceiveTimeoutMs / 2,
                    $"…and so must the last sentence — got {lastSentenceAge} ms in: {message}");

                // The counters above are ours; this one is the OS's, and it is the only thing that can say
                // the router did answer and we failed to collect it. Zero here is what makes "silent router"
                // a conclusion rather than a guess.
                Assert.IsTrue(message.Contains("0 byte(s) unread in the socket buffer"),
                    "a silent router leaves nothing waiting in the socket buffer, and that is what rules out "
                    + "the reader being at fault: " + message);

                Assert.IsTrue(serverTask.Wait(10000));
            }
        }

        /// <summary>
        /// The same timeout, the opposite cause: the reply is still coming in, one byte at a time, and no
        /// sentence has completed. Told apart from the case above by the last-byte age alone.
        /// </summary>
        [TestMethod]
        public void AReplyStillArrivingByteByByteIsNotReportedAsSilent()
        {
            using (var server = new FakeRouterServer())
            {
                var serverTask = Task.Run(() =>
                {
                    server.AcceptClient();
                    server.ReadSentence();                          // login
                    server.WriteSentence("!done");
                    server.ReadSentence();                          // /interface/print
                    for (int i = 0; i < 3; i++)
                        server.WriteSentence("!re", "=name=ether" + i);

                    // A word far longer than what follows: the client blocks in the word-body read, and
                    // every trickled byte is progress that never completes a sentence.
                    server.WriteWordLengthOnly(4096);
                    for (int i = 0; i < ReceiveTimeoutMs * 3 / 50; i++)
                    {
                        server.WriteRawByte((byte)'x');
                        Thread.Sleep(50);
                    }
                });

                string message = TimeOutAPrint(server);

                int lastByteAge = AgeFromMessage(message, "last byte");
                int lastSentenceAge = AgeFromMessage(message, "last complete sentence");

                Assert.IsTrue(lastByteAge < ReceiveTimeoutMs / 3,
                    "bytes were arriving throughout the wait — reporting this as a silent router sends the "
                    + $"reader looking for a router-side stall that is not there. Got {lastByteAge} ms in: "
                    + message);
                Assert.IsTrue(lastSentenceAge > ReceiveTimeoutMs / 2,
                    "no sentence completed during the wait, which is the whole reason the caller timed out "
                    + $"while the socket was busy. Got {lastSentenceAge} ms in: {message}");

                // Not asserted as a number: the point is that the counter is a running total of the
                // connection, so the trickled body shows up on top of the login and the three rows.
                Assert.IsTrue(message.Contains("byte(s) and"), "the byte total is part of the report: " + message);

                Assert.IsTrue(serverTask.Wait(10000));
            }
        }

        /// <summary>
        /// Runs a print that is guaranteed to time out and returns the exception message.
        /// </summary>
        private static string TimeOutAPrint(FakeRouterServer server)
        {
            using (var connection = new ApiConnection(false))
            {
                connection.ReceiveTimeout = ReceiveTimeoutMs;
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                var ex = Assert.ThrowsException<TikConnectionReceiveTimeoutException>(
                    () => connection.CreateCommand("/interface/print").ExecuteList());
                return ex.Message;
            }
        }

        /// <summary>
        /// Pulls "&lt;label&gt; N ms ago" out of the message, failing the test when it is absent — the label
        /// is part of the contract this fixture covers, so a rename must surface here rather than as an
        /// assertion that quietly compares zero to zero.
        /// </summary>
        private static int AgeFromMessage(string message, string label)
        {
            var match = Regex.Match(message, Regex.Escape(label) + @" ([\d,]+) ms ago");
            Assert.IsTrue(match.Success,
                $"the timeout message does not report '{label}': {message}");
            return int.Parse(match.Groups[1].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
        }
    }
}
