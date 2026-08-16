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
    // Minimal loopback HTTP peer for the REST timeout tests. It answers the first N requests from a
    // scripted list of bodies and then STALLS: the socket stays open, the request is never answered, and
    // nothing is closed. That is the shape a black-holed router has from the client's side, and it is the
    // only way to prove a timeout actually bounds something — a peer that refuses or resets the connection
    // fails fast on its own and would let a missing timeout pass.
    internal sealed class ScriptedHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly ConcurrentQueue<string> _responseBodies;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        // Held so the stalled connections are not closed by a finalizer, which would end the client's wait
        // early and turn a timeout test into a "connection closed" test.
        private readonly List<TcpClient> _accepted = new List<TcpClient>();

        public int Port { get; }

        /// <param name="responseBodies">JSON bodies to answer with, in order. Requests past the end stall forever.</param>
        public ScriptedHttpServer(params string[] responseBodies)
        {
            _responseBodies = new ConcurrentQueue<string>(responseBodies ?? new string[0]);
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
                if (!await ReadRequestHeadAsync(stream).ConfigureAwait(false))
                    return;

                if (!_responseBodies.TryDequeue(out string body))
                    return; // stall: hold the socket, answer nothing

                byte[] payload = Encoding.UTF8.GetBytes(body);
                byte[] head = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: application/json\r\n" +
                    "Content-Length: " + payload.Length + "\r\n" +
                    "Connection: close\r\n\r\n");
                await stream.WriteAsync(head, 0, head.Length).ConfigureAwait(false);
                await stream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
                client.Close();
            }
            catch
            {
                // A test that ends while a request is in flight tears the socket down under us; that is the
                // normal way this server stops, not a failure worth surfacing.
            }
        }

        // Reads up to the blank line ending the request head. The body (if any) is deliberately not read:
        // nothing here depends on it, and the tests only need to know a request arrived.
        private static async Task<bool> ReadRequestHeadAsync(NetworkStream stream)
        {
            var buffer = new byte[1];
            int matched = 0;
            const string terminator = "\r\n\r\n";
            while (true)
            {
                int n = await stream.ReadAsync(buffer, 0, 1).ConfigureAwait(false);
                if (n == 0)
                    return false;
                matched = buffer[0] == terminator[matched] ? matched + 1 : (buffer[0] == '\r' ? 1 : 0);
                if (matched == terminator.Length)
                    return true;
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
