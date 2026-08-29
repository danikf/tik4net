using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// Closing a connection while a command is running is the caller's race to start — but what they are
    /// told about it is ours.
    /// </summary>
    /// <remarks>
    /// <c>Close</c> deliberately does <b>not</b> wait for an in-flight command. It has to stay prompt: a
    /// caller closing a connection precisely to escape a stuck command would be defeated by a Close that
    /// blocked for <c>ReceiveTimeout</c> first. So the running command loses its socket, and the only
    /// question is what surfaces.
    /// <para>
    /// Left alone that is whatever the framework threw — an <c>ObjectDisposedException</c>, a raw
    /// <c>IOException</c> — which is outside the tik4net hierarchy, escapes every
    /// <c>catch (TikConnectionException)</c> the caller wrote, and reads like a defect in the library rather
    /// than a race they started. <c>WinboxNativeConnection</c> already got this right by faulting its
    /// outstanding requests with a reason; these tests hold the CLI family to the same bar.
    /// </para>
    /// </remarks>
    [TestClass]
    public class CloseDuringCommandTests
    {
        /// <summary>A terminal whose send blocks until the test lets it through, then fails as a dead socket would.</summary>
        private sealed class BlockingCliConnection : CliConnectionBase
        {
            private readonly TaskCompletionSource<bool> _inFlight = new TaskCompletionSource<bool>();
            private readonly TaskCompletionSource<bool> _release = new TaskCompletionSource<bool>();

            /// <summary>Completes once a command is actually inside the transport.</summary>
            public Task InFlight => _inFlight.Task;

            /// <summary>True when the close delegate ran while <see cref="ITikConnection.IsOpened"/> was already false.</summary>
            public bool SawClosedFlagBeforeTeardown { get; private set; }

            protected override string TransportName => "Blocking";

            public void OpenScripted()
                => OpenWith(_ => Task.FromResult(0), SendAsync,
                    (raw, ct) => Task.FromResult(string.Empty),
                    () =>
                    {
                        SawClosedFlagBeforeTeardown = !IsOpened;
                        _release.TrySetResult(true);
                    });

            private async Task<string> SendAsync(string cliText, CancellationToken ct)
            {
                _inFlight.TrySetResult(true);
                await _release.Task.ConfigureAwait(false);
                // What a socket disposed under a running read actually produces.
                throw new ObjectDisposedException("NetworkStream");
            }

            public override void Open(string host, string user, string password) => OpenScripted();
            public override void Open(string host, int port, string user, string password) => OpenScripted();
            public override Task OpenAsync(string host, string user, string password,
                CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password,
                CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }

            public Task<string> RunAsync(string text) => ExecuteCliCommandAsync(text, CancellationToken.None);
        }

        /// <summary>
        /// The ordering the whole fix rests on: <c>IsOpened</c> is false before anything is torn down.
        /// </summary>
        /// <remarks>
        /// It is what lets a failing command tell "the user closed this" from "the transport broke", so it is
        /// pinned separately rather than left as an implementation detail of Close.
        /// </remarks>
        [TestMethod]
        public void CloseMarksTheConnectionClosedBeforeTearingDownTheTransport()
        {
            var conn = new BlockingCliConnection();
            conn.OpenScripted();

            conn.Close();

            Assert.IsTrue(conn.SawClosedFlagBeforeTeardown,
                "Close tore the transport down while IsOpened was still true — a command failing in that "
                + "window cannot tell a deliberate close from a broken socket.");
        }

        [TestMethod]
        public async Task ACommandLosingItsSocketToCloseGetsATikConnectionException()
        {
            var conn = new BlockingCliConnection();
            conn.OpenScripted();

            Task<string> running = conn.RunAsync("/system/resource/print");
            await conn.InFlight.ConfigureAwait(false);   // the command is genuinely inside the transport

            conn.Close();                                 // races it, as a caller on another thread would

            var ex = await Assert.ThrowsExceptionAsync<TikConnectionNotOpenException>(() => running);

            Assert.IsInstanceOfType(ex.InnerException, typeof(ObjectDisposedException),
                "the framework exception is worth keeping underneath — it is what a bug report needs");
            StringAssert.Contains(ex.Message, "not known",
                "the message must not claim the command did not run: the bytes may have reached the router "
                + "before the socket went, and saying otherwise is how somebody duplicates a write");
        }
    }
}
