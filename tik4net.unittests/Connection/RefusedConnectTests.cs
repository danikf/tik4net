#nullable enable
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Ssh;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// A port that refuses the TCP connection is a network failure, and every TCP transport reports it the way
    /// the binary API does: the <see cref="SocketException"/> itself, as <see cref="ITikConnection.Open(string, string, string)"/>
    /// documents. Only a router that answered and said no is a <see cref="TikConnectionLoginException"/> — a caller
    /// (and the MCP server, which labels that one "auth") told to check credentials when the service is simply off
    /// is sent the wrong way.
    /// </summary>
    [TestClass]
    public class RefusedConnectTests
    {
        private const string TargetId = "AA:BB:CC:DD:EE:FF";

        private static readonly TikConnectionType[] TcpTransports =
        {
            TikConnectionType.Api, TikConnectionType.Telnet, TikConnectionType.Ssh,
            TikConnectionType.WinboxCli, TikConnectionType.WinboxNative,
        };

        [ClassInitialize]
        public static void RegisterSatelliteTransports(TestContext context) => Tik4NetSsh.Register();

        /// <summary>A loopback port nothing listens on: bound, read back, released.</summary>
        private static int ClosedPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static TikConnectionSetup Direct(int port)
            => new TikConnectionSetup("127.0.0.1", "admin", "") { Port = port, ConnectTimeout = TimeSpan.FromSeconds(10) };

        private static TikConnectionSetup ThroughAgent(int agentPort)
            => new TikConnectionSetup(TikRouterAddress.FromRomonId(TargetId), "target-user", "")
            {
                ConnectTimeout = TimeSpan.FromSeconds(10),
                RomonAgentSetup = new TikRomonAgentSetup("127.0.0.1", "agent-user", "") { Port = agentPort },
            };

        [TestMethod]
        public void ARefusedPortIsASocketException_OnEveryTcpTransport()
        {
            int port = ClosedPort();
            foreach (var type in TcpTransports)
            {
                var ex = Assert.ThrowsException<SocketException>(() => Direct(port).Create(type),
                    type + ": a refused TCP connection is a network failure, not a login failure");
                Assert.AreEqual(SocketError.ConnectionRefused, ex.SocketErrorCode, type.ToString());
            }
        }

        [TestMethod]
        public async Task ARefusedPortIsASocketException_OnEveryTcpTransport_Async()
        {
            int port = ClosedPort();
            foreach (var type in TcpTransports)
            {
                var ex = await Assert.ThrowsExceptionAsync<SocketException>(() => Direct(port).CreateAsync(type),
                    type + ": a refused TCP connection is a network failure, not a login failure");
                Assert.AreEqual(SocketError.ConnectionRefused, ex.SocketErrorCode, type.ToString());
            }
        }

        /// <summary>
        /// Through a RoMON agent the refusal is the AGENT's port, and it is still not a login: the agent-leg
        /// wrapper ("RoMON agent … refused the login") is for an agent that answered and said no.
        /// </summary>
        [TestMethod]
        public void ARefusedAgentPortIsASocketException_NotAnAgentLoginRefusal()
        {
            int port = ClosedPort();
            foreach (var type in new[] { TikConnectionType.Telnet, TikConnectionType.Ssh })
            {
                var ex = Assert.ThrowsException<SocketException>(() => ThroughAgent(port).Create(type),
                    type + ": the agent's port refusing the connection is not the agent refusing a login");
                Assert.AreEqual(SocketError.ConnectionRefused, ex.SocketErrorCode, type.ToString());
            }
        }

        /// <summary>The other half: an agent that answers and refuses the credentials is still named as such.</summary>
        [TestMethod]
        public void AnAgentThatRefusesTheLoginIsStillAnAgentLoginFailure()
        {
            using (var agent = new RefusingTelnetPeer())
            {
                var ex = Assert.ThrowsException<TikConnectionLoginException>(
                    () => ThroughAgent(agent.Port).Create(TikConnectionType.Telnet));
                StringAssert.Contains(ex.Message, "RoMON agent 127.0.0.1 refused the login");
            }
        }

        /// <summary>
        /// A Telnet peer that asks for a login and refuses it the way RouterOS 7.23.2 does
        /// (<c>RouterOsCliLogin.IsLoginFailure</c>), then hangs up.
        /// </summary>
        private sealed class RefusingTelnetPeer : IDisposable
        {
            private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly CancellationTokenSource _cts = new CancellationTokenSource();
            private readonly Task _serve;

            public int Port { get; }

            public RefusingTelnetPeer()
            {
                _listener.Start();
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _serve = Task.Run(ServeAsync);
            }

            private async Task ServeAsync()
            {
                try
                {
                    using (TcpClient client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false))
                    {
                        NetworkStream stream = client.GetStream();
                        await WriteAsync(stream, "Login: ").ConfigureAwait(false);
                        await ReadLineAsync(stream).ConfigureAwait(false);
                        await WriteAsync(stream, "Password: ").ConfigureAwait(false);
                        await ReadLineAsync(stream).ConfigureAwait(false);
                        await WriteAsync(stream, "\r\nLogin failed, incorrect username or password\r\n\r\n").ConfigureAwait(false);
                    }
                }
                catch (Exception) when (_cts.IsCancellationRequested)
                {
                    // disposed while waiting — the test is over
                }
            }

            private static Task WriteAsync(NetworkStream stream, string text)
            {
                byte[] bytes = Encoding.ASCII.GetBytes(text);
                return stream.WriteAsync(bytes, 0, bytes.Length);
            }

            private async Task ReadLineAsync(NetworkStream stream)
            {
                var one = new byte[1];
                while (await stream.ReadAsync(one, 0, 1, _cts.Token).ConfigureAwait(false) == 1)
                {
                    if (one[0] == (byte)'\n' || one[0] == (byte)'\r')
                        return;
                }
            }

            public void Dispose()
            {
                _cts.Cancel();
                _listener.Stop();
                try { _serve.Wait(5000); } catch (AggregateException) { /* the accept was cancelled */ }
            }
        }
    }
}
