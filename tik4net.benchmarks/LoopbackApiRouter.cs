#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace tik4net.Benchmarks
{
    /// <summary>
    /// A binary-API peer on loopback that answers <c>/login</c> with <c>!done</c>, <c>/quit</c> with <c>!fatal</c>, and
    /// any other command with <see cref="Rows"/> pre-encoded <c>!re</c> sentences and a <c>!done</c>, each carrying the
    /// request's tag.
    /// <para>
    /// The rows are encoded once, so the server allocates next to nothing while it answers: BenchmarkDotNet's
    /// allocation count is process-wide, and a server building strings per row would be counted as the client's.
    /// </para>
    /// </summary>
    internal sealed class LoopbackApiRouter : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Thread _thread;
        private readonly List<byte[]> _rowBodies;     // every word of a row except .tag and the terminator
        private volatile bool _stop;

        internal int Port { get; }
        internal int Rows => _rowBodies.Count;

        internal LoopbackApiRouter(IEnumerable<Dictionary<string, string>> rows)
        {
            _rowBodies = rows.Select(r => Encode(new[] { "!re" }.Concat(r.Select(kv => "=" + kv.Key + "=" + kv.Value)))).ToList();
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _thread = new Thread(Serve) { IsBackground = true, Name = "loopback-api-router" };
            _thread.Start();
        }

        private void Serve()
        {
            while (!_stop)
            {
                TcpClient client;
                try { client = _listener.AcceptTcpClient(); }
                catch (SocketException) { return; }
                var t = new Thread(() => Session(client)) { IsBackground = true };
                t.Start();
            }
        }

        private void Session(TcpClient client)
        {
            client.NoDelay = true;
            using (client)
            using (var net = client.GetStream())
            using (var output = new BufferedStream(net, 64 * 1024))
            {
                try
                {
                    while (true)
                    {
                        var words = ReadSentence(net);
                        if (words == null) return;
                        string tag = words.FirstOrDefault(w => w.StartsWith(".tag=", StringComparison.Ordinal)) ?? "";
                        byte[] tagWord = tag.Length == 0 ? Array.Empty<byte>() : Word(tag);
                        string command = words.Count > 0 ? words[0] : "";

                        if (command == "/quit")
                        {
                            WriteSentence(output, Encode(new[] { "!fatal", "=message=session terminated on request" }), tagWord);
                            output.Flush();
                            return;
                        }
                        if (command != "/login")
                            foreach (var row in _rowBodies)
                                WriteSentence(output, row, tagWord);
                        WriteSentence(output, Word("!done"), tagWord);
                        output.Flush();
                    }
                }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
            }
        }

        private static void WriteSentence(Stream output, byte[] body, byte[] tagWord)
        {
            output.Write(body, 0, body.Length);
            output.Write(tagWord, 0, tagWord.Length);
            output.WriteByte(0);
        }

        private static List<string>? ReadSentence(Stream s)
        {
            var words = new List<string>();
            while (true)
            {
                int first = s.ReadByte();
                if (first < 0) return null;
                long len = DecodeLength((byte)first, s);
                if (len == 0) return words;
                var buf = new byte[len];
                int read = 0;
                while (read < len)
                {
                    int n = s.Read(buf, read, (int)len - read);
                    if (n == 0) return null;
                    read += n;
                }
                words.Add(Encoding.UTF8.GetString(buf));
            }
        }

        private static long DecodeLength(byte first, Stream s)
        {
            if ((first & 0x80) == 0) return first;
            if ((first & 0xC0) == 0x80) return ((first & 0x3F) << 8) + s.ReadByte();
            if ((first & 0xE0) == 0xC0) return ((first & 0x1F) << 16) + (s.ReadByte() << 8) + s.ReadByte();
            throw new IOException("benchmark peer: unexpected long word from the client");
        }

        internal static byte[] Encode(IEnumerable<string> words)
        {
            using (var ms = new MemoryStream())
            {
                foreach (var w in words)
                {
                    var b = Word(w);
                    ms.Write(b, 0, b.Length);
                }
                return ms.ToArray();
            }
        }

        internal static byte[] Word(string word)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(word);
            int n = bytes.Length;
            byte[] prefix = n < 0x80 ? new[] { (byte)n }
                : n < 0x4000 ? new[] { (byte)((n >> 8) | 0x80), (byte)n }
                : new[] { (byte)((n >> 16) | 0xC0), (byte)(n >> 8), (byte)n };
            return prefix.Concat(bytes).ToArray();
        }

        public void Dispose()
        {
            _stop = true;
            _listener.Stop();
        }
    }
}
