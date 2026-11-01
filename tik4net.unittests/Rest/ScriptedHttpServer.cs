using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.unittests.Rest
{
    // Minimal loopback HTTP peer for the REST tests. It records what arrives, answers the first N requests from a
    // scripted list and then STALLS: the socket stays open, the request is never answered, and
    // nothing is closed. That is the shape a black-holed router has from the client's side, and it is the
    // only way to prove a timeout actually bounds something — a peer that refuses or resets the connection
    // fails fast on its own and would let a missing timeout pass.
    internal sealed class ScriptedHttpServer : IDisposable
    {
        /// <summary>One scripted answer: any status, content type and bytes.</summary>
        internal sealed class Reply
        {
            public int Status { get; }
            public string ContentType { get; }
            public byte[] Body { get; }

            public Reply(int status, string body, string contentType = "application/json")
                : this(status, Encoding.UTF8.GetBytes(body), contentType) { }

            public Reply(int status, byte[] body, string contentType = "application/json")
            {
                Status = status;
                Body = body;
                ContentType = contentType;
            }
        }

        /// <summary>One request as it arrived.</summary>
        internal sealed class Request
        {
            public string Method { get; set; }
            public string Path { get; set; }
            public string ContentType { get; set; }
            public byte[] BodyBytes { get; set; }
            public string Body => Encoding.UTF8.GetString(BodyBytes);
            public override string ToString() => Method + " " + Path + " " + Body;
        }

        private readonly TcpListener _listener;
        private readonly ConcurrentQueue<Reply> _replies = new ConcurrentQueue<Reply>();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        // Held so the stalled connections are not closed by a finalizer, which would end the client's wait
        // early and turn a timeout test into a "connection closed" test.
        private readonly List<TcpClient> _accepted = new List<TcpClient>();

        public int Port { get; }

        /// <summary>What arrived, in order. A request past the end of the script is recorded before it stalls.</summary>
        public ConcurrentQueue<Request> Requests { get; } = new ConcurrentQueue<Request>();

        /// <summary>A peer that accepts and never answers.</summary>
        public ScriptedHttpServer() : this(new Reply[0])
        {
        }

        /// <param name="responseBodies">JSON bodies to answer 200 with, in order. Requests past the end stall forever.</param>
        public ScriptedHttpServer(params string[] responseBodies)
            : this(Array.ConvertAll(responseBodies ?? new string[0], body => new Reply(200, body)))
        {
        }

        /// <param name="replies">The answers, in order. Requests past the end stall forever.</param>
        public ScriptedHttpServer(params Reply[] replies)
        {
            foreach (var reply in replies)
                _replies.Enqueue(reply);
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Task.Run(() => AcceptLoopAsync());
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                catch { return; } // listener stopped by Dispose
                lock (_accepted) _accepted.Add(client);
                _ = Task.Run(() => ServeAsync(client));
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            try
            {
                var stream = client.GetStream();
                var request = await ReadRequestAsync(stream).ConfigureAwait(false);
                if (request == null)
                    return;
                Requests.Enqueue(request);

                if (!_replies.TryDequeue(out Reply reply))
                    return; // stall: hold the socket, answer nothing

                byte[] head = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 " + reply.Status + " Scripted\r\n" +
                    "Content-Type: " + reply.ContentType + "\r\n" +
                    "Content-Length: " + reply.Body.Length + "\r\n" +
                    "Connection: close\r\n\r\n");
                await stream.WriteAsync(head, 0, head.Length).ConfigureAwait(false);
                await stream.WriteAsync(reply.Body, 0, reply.Body.Length).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
                client.Close();
            }
            catch
            {
                // A test that ends while a request is in flight tears the socket down under us; that is the
                // normal way this server stops, not a failure worth surfacing.
            }
        }

        // Reads the request head up to its blank line, then the body its Content-Length announces: closing the
        // socket with it unread makes the client's send fail with a reset (an add is a PUT with a body).
        private static async Task<Request> ReadRequestAsync(NetworkStream stream)
        {
            var buffer = new byte[1];
            var head = new StringBuilder();
            int matched = 0;
            const string terminator = "\r\n\r\n";
            while (matched < terminator.Length)
            {
                int n = await stream.ReadAsync(buffer, 0, 1).ConfigureAwait(false);
                if (n == 0)
                    return null;
                head.Append((char)buffer[0]);
                matched = buffer[0] == terminator[matched] ? matched + 1 : (buffer[0] == '\r' ? 1 : 0);
            }

            string text = head.ToString();
            var length = System.Text.RegularExpressions.Regex.Match(text, @"(?im)^Content-Length:\s*(\d+)");
            var body = new byte[length.Success ? int.Parse(length.Groups[1].Value) : 0];
            for (int read = 0; read < body.Length;)
            {
                int n = await stream.ReadAsync(body, read, body.Length - read).ConfigureAwait(false);
                if (n == 0)
                    return null;
                read += n;
            }

            string[] requestLine = text.Substring(0, text.IndexOf('\r')).Split(' ');
            var contentType = System.Text.RegularExpressions.Regex.Match(text, @"(?im)^Content-Type:\s*(.+?)\s*$");
            return new Request
            {
                Method = requestLine[0],
                Path = requestLine[1],
                ContentType = contentType.Success ? contentType.Groups[1].Value : null,
                BodyBytes = body,
            };
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            lock (_accepted)
            {
                foreach (var c in _accepted)
                {
                    try { c.Close(); } catch { /* already gone */ }
                }
                _accepted.Clear();
            }
            _cts.Dispose();
        }
    }
}
