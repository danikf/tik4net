using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;
using tik4net.Connection;
using tik4net.Rest;
using tik4net.unittests.Rest;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// One open-failure taxonomy on every transport: nothing answered is a <see cref="SocketException"/>; the router
    /// refused, or answered the login with silence, is a <see cref="TikConnectionLoginException"/> (and kin); it answered
    /// in another protocol is a <see cref="TikConnectionProtocolMismatchException"/> or
    /// <see cref="TikConnectionSSLErrorException"/>; anything else is a <see cref="TikConnectionLoginException"/> wrapping
    /// it. Here for the API and REST, whose opens are not the CLI's and native's (OpenFailureTranslationTests).
    /// </summary>
    [TestClass]
    [DoNotParallelize]   // local listeners answer within a time budget
    public class OpenFailureTaxonomyTests
    {
        // A loopback port nothing listens on: bound to learn a free number, then released.
        private static int ClosedPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        [TestMethod]
        public void TranslatePassesTheSpecificAnswersThroughAndWrapsTheRest()
        {
            var mismatch = new TikConnectionProtocolMismatchException(8729, false, 8728, 8729, "test", null);
            var ssl = new TikConnectionSSLErrorException(new System.Security.Authentication.AuthenticationException("test"));
            var socket = new SocketException((int)SocketError.ConnectionRefused);
            Assert.AreSame(mismatch, TikOpenGuard.Translate(mismatch));
            Assert.AreSame(ssl, TikOpenGuard.Translate(ssl));
            Assert.AreSame(socket, TikOpenGuard.Translate(socket));

            var io = new System.IO.IOException("reset");
            var wrapped = TikOpenGuard.Translate(io);
            Assert.AreEqual(typeof(TikConnectionLoginException), wrapped.GetType());
            Assert.AreSame(io, wrapped.InnerException);
        }

        // ── REST ────────────────────────────────────────────────────────────

        [TestMethod]
        public void Rest_APortNothingListensOn_IsTheSocketException()
        {
            using (var conn = new RestConnection(useSsl: false))
            {
                conn.ConnectTimeout = TimeSpan.FromSeconds(5);
                var ex = Assert.ThrowsException<SocketException>(() => conn.Open("127.0.0.1", ClosedPort(), "admin", ""));
                Assert.AreEqual(SocketError.ConnectionRefused, ex.SocketErrorCode);
                Assert.IsFalse(conn.IsOpened);
            }
        }

        [TestMethod]
        public void Rest_APeerThatNeverAnswersTheProbe_IsALoginWithNoAnswer()
        {
            using (var server = new ScriptedHttpServer())   // accepts, answers nothing
            using (var conn = new RestConnection(useSsl: false))
            {
                conn.ConnectTimeout = TimeSpan.FromMilliseconds(1000);
                var ex = Assert.ThrowsException<TikConnectionLoginNoAnswerException>(
                    () => conn.Open("127.0.0.1", server.Port, "admin", ""));
                Assert.AreEqual("REST", ex.Transport);
                Assert.IsInstanceOfType(ex.InnerException, typeof(TikConnectionReceiveTimeoutException));
                Assert.AreEqual(TimeSpan.FromMilliseconds(1000), ((TikConnectionReceiveTimeoutException)ex.InnerException).Timeout);
            }
        }

        // ── API ─────────────────────────────────────────────────────────────

        [TestMethod]
        public void Api_APeerThatHangsUpOnTheLogin_IsAProtocolMismatch()
        {
            // Pinning: the IsPeerClosedDuringHandshake rule already gave this.
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var peer = Task.Run(() =>
                {
                    using (var socket = listener.AcceptSocket())
                    {
                        socket.Receive(new byte[1]);
                        socket.Close();
                    }
                });
                using (var conn = new ApiConnection(false))
                {
                    conn.ReceiveTimeout = TimeSpan.FromSeconds(5);
                    Assert.ThrowsException<TikConnectionProtocolMismatchException>(
                        () => conn.Open("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, "admin", ""));
                }
                Assert.IsTrue(peer.Wait(5000));
            }
            finally { listener.Stop(); }
        }

        [TestMethod]
        public void Api_APeerThatNeverAnswersTheLogin_IsALoginFailure()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Socket accepted = null;
            try
            {
                var peer = Task.Run(() => { accepted = listener.AcceptSocket(); });   // accepts, never answers
                using (var conn = new ApiConnection(false))
                {
                    conn.ReceiveTimeout = TimeSpan.FromMilliseconds(1000);
                    var ex = Assert.ThrowsException<TikConnectionLoginException>(
                        () => conn.Open("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, "admin", ""));
                    Assert.IsInstanceOfType(ex.InnerException, typeof(TikConnectionReceiveTimeoutException),
                        "the login's receive timeout is what is wrapped: " + ex);
                    Assert.IsFalse(conn.IsOpened);
                }
                Assert.IsTrue(peer.Wait(5000));
            }
            finally
            {
                accepted?.Close();
                listener.Stop();
            }
        }
    }
}
