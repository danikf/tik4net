using System;
using System.Diagnostics;
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
        /// The router answers in full, under a tag the caller is not waiting for. Nothing is missing from the
        /// socket — the reply is sitting in the dispatcher, filed where nobody will look for it.
        /// </summary>
        /// <remarks>
        /// This is the third way to time out and the one the socket counters alone get exactly backwards: the
        /// bytes and the sentences all arrived, so "last byte" and "last sentence" are both as old as the
        /// answer rather than as old as the timeout — indistinguishable from a router that fell silent at the
        /// same moment. Only the dispatcher can say the answer is here and unpaired, which is a fault in this
        /// client rather than in the router, and so has an entirely different fix.
        /// </remarks>
        [TestMethod]
        public void AnAnswerFiledUnderTheWrongTagIsReportedAsUnpairedRatherThanMissing()
        {
            using (var server = new FakeRouterServer())
            {
                var serverTask = Task.Run(() =>
                {
                    server.AcceptClient();
                    server.ReadSentence();                          // login
                    server.WriteSentence("!done");

                    server.ReadSentence();                          // /interface/print
                    // Answer completely and correctly — except for the one word that says whose answer it is.
                    server.EchoTags = false;
                    string wrongTag = TikSpecialProperties.Tag + "=999999";
                    server.WriteSentence("!re", "=name=ether0", wrongTag);
                    server.WriteSentence("!done", wrongTag);

                    Thread.Sleep(ReceiveTimeoutMs * 2);
                });

                string message = TimeOutAPrint(server);

                Assert.IsTrue(message.Contains("unclaimed sentences are held for tag(s) 999999×2"),
                    "the answer arrived and is still held — reporting this as an unanswered command sends the "
                    + "reader looking at the router, when the fault is entirely on this side: " + message);

                // The counters are deliberately NOT asserted to be old here: they cannot be, because
                // everything did arrive. That is the whole reason this case needs the dispatcher.
                Assert.IsTrue(serverTask.Wait(10000));
            }
        }

        /// <summary>
        /// A session that has answered nothing twice running is not asked a third time.
        /// </summary>
        /// <remarks>
        /// The stall does not heal: measured against a live router, a session that stops sending never resumes,
        /// so every later command spent a whole <c>ReceiveTimeout</c> rediscovering it and the caller
        /// experienced an unboundedly slow connection instead of a broken one. The command is deliberately not
        /// written — the router goes on <b>executing</b> what it receives on a stalled session, so sending
        /// would risk a write nobody can confirm.
        /// </remarks>
        [TestMethod]
        public void ASessionThatHasStoppedAnsweringIsNotAskedAgain()
        {
            using (var server = new FakeRouterServer())
            {
                var serverTask = Task.Run(() =>
                {
                    server.AcceptClient();
                    server.ReadSentence();                          // login
                    server.WriteSentence("!done");

                    server.ReadSentence();                          // first command — answered with silence
                    server.ReadSentence();                          // second — likewise
                    Thread.Sleep(ReceiveTimeoutMs * 4);
                });

                using (var connection = new ApiConnection(false))
                {
                    connection.ReceiveTimeout = ReceiveTimeoutMs;
                    connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                    for (int i = 0; i < SilentTimeoutsBeforeDead; i++)
                        Assert.ThrowsException<TikConnectionReceiveTimeoutException>(
                            () => connection.CreateCommand("/interface/print").ExecuteList(),
                            "the first unanswered commands still have to wait — one silent command is not "
                            + "proof of anything, a monitor legitimately looks the same");

                    var sw = Stopwatch.StartNew();
                    var dead = Assert.ThrowsException<TikConnectionSessionClosedException>(
                        () => connection.CreateCommand("/interface/print").ExecuteList());
                    sw.Stop();

                    Assert.IsTrue(sw.ElapsedMilliseconds < ReceiveTimeoutMs / 2,
                        "failing fast is the entire point: this took " + sw.ElapsedMilliseconds + " ms of a "
                        + ReceiveTimeoutMs + " ms timeout");
                    Assert.IsTrue(dead.Message.Contains("did not run"),
                        "a caller has to be able to tell whether to retry: " + dead.Message);

                    // Close must still work — it is what the exception tells the caller to do, and it must not
                    // trip over the refusal on its own /quit.
                    connection.Close();
                    Assert.IsFalse(connection.IsOpened);
                }

                serverTask.Wait(10000);
            }
        }

        /// <summary>Mirrors <c>ApiConnection.SilentTimeoutsBeforePresumedDead</c>, which is internal.</summary>
        private const int SilentTimeoutsBeforeDead = 2;

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
