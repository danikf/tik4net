using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    // Minimal scripted RouterOS binary-API peer for loopback protocol tests (P1.7 in
    // ARCHITECTUREIMPROVEMENTPLAN.md). Speaks the same length-prefixed word framing as
    // ApiConnection, reusing the production ApiConnectionHelper.EncodeLength encoder
    // (internal, reachable here via InternalsVisibleTo).
    internal sealed class FakeRouterServer : IDisposable
    {
        private readonly TcpListener _listener;
        private TcpClient _client;
        private NetworkStream _stream;

        public int Port { get; }

        /// <summary>
        /// When true (the default), the server echoes the tag of the last sentence it read onto every reply
        /// that does not already carry one — which is what a real RouterOS does, and what makes a scripted
        /// conversation work regardless of the client's tagging policy. Since 5.0 the client tags
        /// synchronous commands by default, so a fake that never echoed would answer a caller waiting on a
        /// tag with a sentence addressed to nobody, and every test would time out rather than fail.
        /// <para>
        /// Set it to <c>false</c> to script exactly that on purpose — an untagged or mis-addressed reply is
        /// a case worth testing, it just has to be asked for.
        /// </para>
        /// </summary>
        public bool EchoTags { get; set; } = true;

        private string _lastRequestTag;

        /// <summary>
        /// The <c>.tag=…</c> word of the last sentence read, or <c>null</c> when it carried none. Needed by
        /// the tests that write their reply word by word (<see cref="WriteWordWithFiveByteLength"/>) and so
        /// bypass <see cref="WriteSentence"/>'s echo.
        /// </summary>
        public string LastRequestTag => _lastRequestTag;

        public FakeRouterServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        }

        public void AcceptClient(int timeoutMs = 5000)
        {
            var acceptTask = _listener.AcceptTcpClientAsync();
            if (!acceptTask.Wait(timeoutMs))
                throw new TimeoutException("Fake router server did not receive a connection in time.");
            _client = acceptTask.Result;
            _stream = _client.GetStream();
        }

        public List<string> ReadSentence()
        {
            var words = new List<string>();
            while (true)
            {
                long length = ReadWordLength();
                if (length == 0)
                    break; // sentence terminator

                byte[] buffer = new byte[(int)length];
                int totalRead = 0;
                while (totalRead < buffer.Length)
                {
                    int n = _stream.Read(buffer, totalRead, buffer.Length - totalRead);
                    if (n == 0)
                        throw new IOException("Client closed connection while sending a word.");
                    totalRead += n;
                }
                words.Add(Encoding.UTF8.GetString(buffer));
            }
            _lastRequestTag = words.Find(w => w.StartsWith(TagPrefix, StringComparison.Ordinal));
            return words;
        }

        private const string TagPrefix = TikSpecialProperties.Tag + "=";   // ".tag="

        private long ReadWordLength()
        {
            int b0 = ReadByteChecked();
            if ((b0 & 0x80) == 0x00)
                return b0;
            if ((b0 & 0xC0) == 0x80)
                return ((b0 & 0x3F) << 8) + ReadByteChecked();
            if ((b0 & 0xE0) == 0xC0)
            {
                long l = ((b0 & 0x1F) << 8) + ReadByteChecked();
                return (l << 8) + ReadByteChecked();
            }
            if ((b0 & 0xF0) == 0xE0)
            {
                long l = ((b0 & 0x0F) << 8) + ReadByteChecked();
                l = (l << 8) + ReadByteChecked();
                return (l << 8) + ReadByteChecked();
            }
            if (b0 == 0xF0)
            {
                long l = ReadByteChecked();
                l = (l << 8) + ReadByteChecked();
                l = (l << 8) + ReadByteChecked();
                l = (l << 8) + ReadByteChecked();
                return l;
            }
            throw new IOException($"Unexpected control byte 0x{b0:X2}.");
        }

        private int ReadByteChecked()
        {
            int b = _stream.ReadByte();
            if (b < 0)
                throw new IOException("Client closed connection.");
            return b;
        }

        public void WriteSentence(params string[] words)
        {
            foreach (var word in words)
                WriteWord(word);
            if (EchoTags && _lastRequestTag != null
                && Array.FindIndex(words, w => w.StartsWith(TagPrefix, StringComparison.Ordinal)) < 0)
                WriteWord(_lastRequestTag);
            EndSentence();
        }

        private void WriteWord(string word)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(word);
            byte[] length = ApiConnectionHelper.EncodeLength(bytes.Length);
            _stream.Write(length, 0, length.Length);
            _stream.Write(bytes, 0, bytes.Length);
        }

        // Writes a word using the protocol's 5-byte control-byte encoding (0xF0 + 4-byte big-endian
        // length). RouterOS only uses this for words >= 0x10000000 bytes; forcing it on a small word
        // here exercises ApiConnection's decode path for that control byte without transferring 256MB.
        public void WriteWordWithFiveByteLength(string word)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(word);
            byte[] lenBytes = BitConverter.GetBytes(bytes.Length);
            _stream.WriteByte(0xF0);
            _stream.WriteByte(lenBytes[3]);
            _stream.WriteByte(lenBytes[2]);
            _stream.WriteByte(lenBytes[1]);
            _stream.WriteByte(lenBytes[0]);
            _stream.Write(bytes, 0, bytes.Length);
        }

        public void EndSentence()
        {
            _stream.WriteByte(0);
            _stream.Flush();
        }

        public void CloseClientConnection()
        {
            _stream?.Dispose();
            _client?.Close();
        }

        public void Dispose()
        {
            CloseClientConnection();
            _listener.Stop();
        }
    }
}
