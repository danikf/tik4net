using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    /// <summary>
    /// The binary API's reader reads the socket through a buffer: one receive fills it, and the length prefixes and
    /// word bodies are taken from it. These pin the two framing cases a buffer can get wrong — a reply that arrives in
    /// the smallest pieces TCP can deliver, and a socket that ends part-way through a length prefix — end to end
    /// through <see cref="ApiConnection"/> against a fake router.
    /// </summary>
    [TestClass]
    [DoNotParallelize]   // a fake peer answers within a receive timeout
    public class ApiBufferedReadFramingTests
    {
        private const string TestUser = "admin";
        private const string TestPassword = "secret";

        // Values whose "=name=…" word sits on each side of the 1- and 2-byte prefix boundaries, plus a 3-byte one.
        private static readonly int[] WordLengths = { 1, 0x7F, 0x80, 0x3FFF, 0x4000 };

        private static string ValueForWordLength(int wordLength)
            => wordLength <= "=name=".Length ? "" : new string('v', wordLength - "=name=".Length);

        /// <summary>
        /// Every byte of the reply arrives in a TCP segment of its own, so every receive the reader makes returns one
        /// byte: each length prefix is split across receives, and so is every word body.
        /// </summary>
        [TestMethod]
        public void AReplyDeliveredOneBytePerSegmentReadsWhole()
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");
                server.ReadSentence();                       // /interface/print

                var reply = new List<byte>();
                for (int i = 0; i < 3; i++)
                    reply.AddRange(server.EncodeSentence("!re", "=.id=*" + i, "=name=ether" + i, "=comment="));
                reply.AddRange(server.EncodeSentence("!done"));
                server.WriteOneBytePerSegment(reply.ToArray());

                // The long words: their prefixes one byte per segment, the bodies (16 KB) in one go.
                server.ReadSentence();                       // /long/print
                foreach (int length in WordLengths)
                {
                    byte[] sentence = server.EncodeSentence("!re", "=name=" + ValueForWordLength(length));
                    int bodyStart = 1 + "!re".Length;
                    int prefixLength = length < 0x80 ? 1 : length < 0x4000 ? 2 : 3;
                    server.WriteOneBytePerSegment(sentence.Take(bodyStart + prefixLength).ToArray());
                    server.WriteRaw(sentence.Skip(bodyStart + prefixLength).ToArray());
                }
                server.WriteOneBytePerSegment(server.EncodeSentence("!done"));

                server.ReadSentence();                       // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.ReceiveTimeout = TimeSpan.FromSeconds(30);
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                var rows = connection.CreateCommand("/interface/print").ExecuteList().ToList();
                Assert.AreEqual(3, rows.Count);
                for (int i = 0; i < 3; i++)
                {
                    Assert.AreEqual("*" + i, rows[i].GetId());
                    Assert.AreEqual("ether" + i, rows[i].GetResponseField("name"));
                    Assert.AreEqual("", rows[i].GetResponseField("comment"));
                }

                var longRows = connection.CreateCommand("/long/print").ExecuteList().ToList();
                CollectionAssert.AreEqual(WordLengths.Select(ValueForWordLength).ToList(),
                    longRows.Select(r => r.GetResponseField("name")).ToList());

                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(30000));
        }

        /// <summary>
        /// The socket ends after the first byte of a two-byte length prefix, and after two bytes of a three-byte one:
        /// the waiting caller is told the connection was lost, as the end of the stream inside a prefix — not as a
        /// truncated word body, and not as a hang.
        /// </summary>
        [DataTestMethod]
        [DataRow(new byte[] { 0x80 }, DisplayName = "1 of 2 prefix bytes")]
        [DataRow(new byte[] { 0xC0, 0x40 }, DisplayName = "2 of 3 prefix bytes")]
        public void ASocketThatEndsInsideALengthPrefixIsALostConnection(byte[] partialPrefix)
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");
                server.ReadSentence();                       // command
                // Whole rows, then the cut, in ONE write: the prefix's first bytes arrive in the same receive as the
                // rows before them, so they sit in the reader's buffer when the stream ends.
                var bytes = new List<byte>();
                bytes.AddRange(server.EncodeSentence("!re", "=name=ether1"));
                bytes.Add(3);                                // the next sentence's "!re"
                bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("!re"));
                bytes.AddRange(partialPrefix);
                server.WriteRaw(bytes.ToArray());
                server.CloseClientConnection();
            });

            using (var connection = new ApiConnection(false))
            {
                connection.ReceiveTimeout = TimeSpan.FromSeconds(10);
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                var ex = Assert.ThrowsException<TikCommandFatalException>(
                    () => connection.CreateCommand("/interface/print").ExecuteList());
                StringAssert.Contains(ex.Message, "connection lost");
                StringAssert.Contains(ex.Message, "Connection closed by remote host",
                    "the stream ended inside a length prefix, which is the end-of-stream case, not a cut word body");
                Assert.IsFalse(connection.IsOpened);
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }
    }
}
