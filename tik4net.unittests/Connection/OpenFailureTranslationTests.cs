using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.unittests.Cli;
using tik4net.Winbox;
using tik4net.WinboxNative;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// What a failed open lets out as itself and what it reports as a failed login, for the CLI transports
    /// (<c>CliConnectionBase.OpenTerminal</c>) and WinBox native (its carrier's <c>Open</c>); and that the half-open
    /// client is closed either way.
    /// </summary>
    [TestClass]
    public class OpenFailureTranslationTests
    {
        private static Exception Make(string kind)
        {
            switch (kind)
            {
                case "cancel": return new OperationCanceledException();
                case "login": return new TikConnectionLoginException(new IOException("bad password"));
                case "refused": return new TikConnectionLoginRefusedException("Test", "invalid user name or password");
                case "romon": return new TikRomonRelayException(TikRomonRelayFailure.RomonNotEnabledOnAgent, "romon is off");
                case "socket": return new SocketException((int)SocketError.ConnectionRefused);
                case "io": return new IOException("reset");
                case "invalid": return new InvalidOperationException("odd state");
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static void AssertLeaves(Exception thrown, Exception sent, bool passesThrough)
        {
            if (passesThrough)
                Assert.AreSame(sent, thrown);
            else
            {
                Assert.AreEqual(typeof(TikConnectionLoginException), thrown.GetType());
                Assert.AreSame(sent, thrown.InnerException);
            }
        }

        // ── CLI ─────────────────────────────────────────────────────────────

        private sealed class FailingCliConnection : ScriptedCliConnectionBase
        {
            private readonly Exception _loginFailure;

            public FailingCliConnection(Exception loginFailure) { _loginFailure = loginFailure; }

            public int Closed { get; private set; }

            private protected override string TransportName => "Failing";

            private void OpenFailing()
                => OpenScripted(_ => { throw _loginFailure; },
                    (cmd, ct) => Task.FromResult(string.Empty),
                    (raw, ct) => Task.FromResult(string.Empty),
                    () => Closed++);

            public override void Open(string host, string user, string password) => OpenFailing();
            public override void Open(string host, int port, string user, string password) => OpenFailing();
            public override Task OpenAsync(string host, string user, string password,
                CancellationToken cancellationToken = default) { OpenFailing(); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password,
                CancellationToken cancellationToken = default) { OpenFailing(); return Task.FromResult(0); }
        }

        [DataTestMethod]
        [DataRow("cancel", true)]
        [DataRow("login", true)]
        [DataRow("refused", true)]
        [DataRow("romon", true)]
        [DataRow("socket", true)]
        [DataRow("io", false)]
        [DataRow("invalid", false)]
        public void Cli_AFailedLogin_LeavesAsItselfOrAsALoginFailure_AndClosesTheClient(string kind, bool passesThrough)
        {
            Exception sent = Make(kind);
            using (var connection = new FailingCliConnection(sent))
            {
                Exception thrown = null;
                try { connection.Open("h", "u", "p"); }
                catch (Exception ex) { thrown = ex; }

                Assert.IsNotNull(thrown, "the open succeeded");
                AssertLeaves(thrown, sent, passesThrough);
                Assert.AreEqual(1, connection.Closed);
                Assert.IsFalse(connection.IsOpened);
            }
        }

        // ── WinBox native ───────────────────────────────────────────────────

        private sealed class FailingChannel : IWinboxM2Channel
        {
            private readonly Exception _openFailure;

            public FailingChannel(Exception openFailure) { _openFailure = openFailure; }

            public bool Disposed { get; private set; }

            public void Open(string host, int port, string user, string password, int connectTimeoutMs, int ioTimeoutMs,
                int sendTimeoutMs = 0) => throw _openFailure;

            public void Dispose() => Disposed = true;

            public bool IsEncrypted => false;
            public bool DataAvailable => false;
            public bool SupportsStaleDrain => false;
            public bool SendAbandoned => false;
            public bool SendStalled => false;
            public long BytesReceived => 0;
            public bool SupportsReaderLoop => false;
            public byte[] NextReqIdField() => throw new NotSupportedException();
            public void Send(byte[] m2) => throw new NotSupportedException();
            public byte[] Receive(int timeoutMs) => throw new NotSupportedException();
            public byte[] SendReceive(byte[] m2, int timeoutMs) => throw new NotSupportedException();
            public byte[] ReceiveNextFrame() => throw new NotSupportedException();
            public void StartIdleServicing() => throw new NotSupportedException();
        }

        private sealed class FailingNativeConnection : WinboxNativeConnection
        {
            private readonly Exception _openFailure;

            public FailingNativeConnection(Exception openFailure) { _openFailure = openFailure; }

            public System.Collections.Generic.List<FailingChannel> Carriers { get; } = new System.Collections.Generic.List<FailingChannel>();

            private protected override IWinboxM2Channel CreateCarrier()
            {
                var carrier = new FailingChannel(_openFailure);
                Carriers.Add(carrier);
                return carrier;
            }
        }

        // No "cancel" case: no token reaches IWinboxM2Channel.Open, so an OperationCanceledException cannot come
        // out of it.
        [DataTestMethod]
        [DataRow("login", true, 1)]
        [DataRow("refused", true, RouterLoginRetry.MaxAttempts)]   // a transient refusal is retried on a new carrier
        [DataRow("romon", true, 1)]
        [DataRow("socket", true, 1)]
        [DataRow("io", false, 1)]
        [DataRow("invalid", false, 1)]
        public void Native_AFailedCarrierOpen_LeavesAsItselfOrAsALoginFailure_AndDisposesTheCarrier(string kind,
            bool passesThrough, int carriers)
        {
            Exception sent = Make(kind);
            using (var connection = new FailingNativeConnection(sent))
            {
                Exception thrown = null;
                try { connection.Open("h", "u", "p"); }
                catch (Exception ex) { thrown = ex; }

                Assert.IsNotNull(thrown, "the open succeeded");
                AssertLeaves(thrown, sent, passesThrough);
                Assert.AreEqual(carriers, connection.Carriers.Count);
                foreach (var carrier in connection.Carriers)
                    Assert.IsTrue(carrier.Disposed, "a carrier was left open");
                Assert.IsFalse(connection.IsOpened);
            }
        }
    }
}
