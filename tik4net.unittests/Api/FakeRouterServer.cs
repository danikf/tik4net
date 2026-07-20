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
            return words;
        }

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
