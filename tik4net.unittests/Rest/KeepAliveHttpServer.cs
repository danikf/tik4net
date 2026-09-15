using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.unittests.Rest
{
    // Loopback HTTP peer that behaves like the RouterOS web server towards pipelining: it keeps the connection
    // alive, answers the request at the head of what it read, and discards anything that arrived behind it in
    // the same read. RouterOS does exactly that (measured on 7.24.3, Docs/findings-rest-api.md §1), so a client
    // that queues a second request on a connection still waiting for its first gets no answer to it — and
    // nothing on the wire says so. Every answer echoes the request target, so a test can tell whose reply it is.
    internal sealed class KeepAliveHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly List<TcpClient> _accepted = new List<TcpClient>();
        private readonly int _responseDelayMs;
        private int _requestsDiscarded;

        public int Port { get; }

        /// <summary>Requests that arrived queued behind another on the same connection, and were never answered.</summary>
        public int RequestsDiscarded => Volatile.Read(ref _requestsDiscarded);

        /// <param name="responseDelayMs">How long each answer takes — long enough that concurrent requests overlap.</param>
        public KeepAliveHttpServer(int responseDelayMs)
        {
            _responseDelayMs = responseDelayMs;
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
                var buffer = new byte[65536];
                var pending = new StringBuilder();
                while (!_cts.IsCancellationRequested)
                {
                    int headEnd;
                    while ((headEnd = pending.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal)) < 0)
                    {
                        int n = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                        if (n == 0)
                            return;
                        pending.Append(Encoding.ASCII.GetString(buffer, 0, n));
                    }

                    string text = pending.ToString();
                    string requestLine = text.Substring(0, text.IndexOf("\r\n", StringComparison.Ordinal));
                    string target = requestLine.Split(' ')[1];
                    // Whatever followed the first request in this read is dropped, as the router drops it.
                    if (text.IndexOf("\r\n\r\n", headEnd + 4, StringComparison.Ordinal) >= 0)
                        Interlocked.Increment(ref _requestsDiscarded);
                    pending.Clear();

                    await Task.Delay(_responseDelayMs).ConfigureAwait(false);

                    byte[] payload = Encoding.UTF8.GetBytes("[{\".id\":\"*1\",\"name\":\"" + target + "\"}]");
                    byte[] head = Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\n" +
                        "Content-Type: application/json\r\n" +
                        "Content-Length: " + payload.Length + "\r\n" +
                        "Connection: Keep-Alive\r\n\r\n");
                    await stream.WriteAsync(head, 0, head.Length).ConfigureAwait(false);
                    await stream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                }
            }
            catch
            {
                // A test that ends while a request is in flight tears the socket down under us.
            }
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
