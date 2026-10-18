using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;

namespace tik4net.integrationtests
{
    /// <summary>
    /// Rejected credentials, over <b>the transport under test</b> (P2.24).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ConnectionTest.OpenConnectionWithInvalidCredential_WillFailWithProperException</c> pins the same
    /// contract but hardcodes <see cref="TikConnectionType.Api"/>, so it runs eleven times against one
    /// transport and the other ten have never been covered. That matters most on the CLI family, where a
    /// rejection is not a protocol status but a line of English the router prints — a phrase list that does
    /// not match this RouterOS version cannot fail loudly, it can only wait for the receive deadline.
    /// </para>
    /// <para>
    /// Hence the duration assertion: a transport that recognises the refusal reports it immediately, and one
    /// that does not still ends up throwing the same exception type, just a receive-timeout later. Asserting
    /// only the type would call both of those a pass.
    /// </para>
    /// </remarks>
    [TestClass]
    [SafeInParallelLegs]
    public class LoginFailureTest : TestBase
    {
        /// <summary>
        /// Well clear of a prompt round trip on the slowest MAC-layer transport, and well below the 30 s
        /// receive deadline an unrecognised refusal would burn.
        /// </summary>
        private const int MaxRejectionMs = 15000;

        [TestMethod]
        public void RejectedPassword_FailsWithLoginException_Promptly()
        {
            var connType = ResolveConnectionType();
            string host = LabConfig.Get("host");
            string user = LabConfig.Get("user");
            string pass = LabConfig.Get("pass") ?? "";

            // An account with an EMPTY password authenticates over SSH with method "none": the router hands
            // out a shell without ever checking the password, so the wrong one is accepted — verified with
            // `ssh -o PreferredAuthentications=none admin@<host>`, which returns a working prompt. That is
            // the router's policy for a password-less account, not something the client can detect or refuse,
            // and Telnet/WinBox-CLI (which have no such method) do reject the same credentials. There is
            // nothing to assert here until App.config points at an account that has a password.
            if (connType == TikConnectionType.Ssh && string.IsNullOrEmpty(pass))
                Assert.Inconclusive(
                    $"'{user}' has no password, and RouterOS accepts SSH auth method 'none' for such an " +
                    "account — a rejection cannot be provoked. Configure a password-protected user to cover SSH.");

            var sw = Stopwatch.StartNew();
            try
            {
                using (var conn = CreateUnopenedConnection())
                {
                    conn.Open(host, user, "--InvalidPassword--");
                    Assert.Fail($"'{connType}' accepted a wrong password.");
                }
            }
            catch (TikConnectionLoginException ex)
            {
                sw.Stop();
                Console.WriteLine($"{connType}: rejected in {sw.ElapsedMilliseconds} ms — {ex.Message}");
                Assert.IsTrue(sw.ElapsedMilliseconds < MaxRejectionMs,
                    $"'{connType}' took {sw.ElapsedMilliseconds} ms to report rejected credentials. It reached the " +
                    "receive deadline instead of recognising the router's refusal — see RouterOsCliLogin.IsLoginFailure.");
            }
        }

        /// <summary>
        /// A port the router does not listen on is the <see cref="System.Net.Sockets.SocketException"/> itself, on every
        /// transport with a port: nothing answered, so it is not a login failure (the open-failure taxonomy, D5).
        /// </summary>
        [TestMethod]
        public void AClosedPort_IsTheSocketException()
        {
            var connType = ResolveConnectionType();
            if (connType == TikConnectionType.MacTelnet || connType == TikConnectionType.WinboxCliMac
                || connType == TikConnectionType.WinboxNativeMac)
                Assert.Inconclusive($"'{connType}' is addressed by MAC; it has no TCP port to find closed.");

            var setup = LabSetup(connType);
            setup.Port = 1;   // tcpmux: nothing on RouterOS listens there, so the router answers RST
            setup.ConnectTimeout = TimeSpan.FromSeconds(10);
            var ex = Assert.ThrowsException<System.Net.Sockets.SocketException>(() => setup.Create(connType).Dispose(),
                $"'{connType}' did not report a closed port as the SocketException");
            Console.WriteLine($"{connType}: {ex.SocketErrorCode} — {ex.Message}");
        }

        /// <summary>
        /// The plain API or REST pointed at the TLS port, and the reverse: the peer answers in another protocol, which is
        /// reported as such or as a failed login — never as a raw I/O exception (the open-failure taxonomy, D5).
        /// </summary>
        [TestMethod]
        public void TheOtherTlsPort_FailsAsAProtocolOrLoginFailure()
        {
            var connType = ResolveConnectionType();
            int otherPort;
            switch (connType)
            {
                case TikConnectionType.Api: otherPort = 8729; break;
                case TikConnectionType.ApiSsl: otherPort = 8728; break;
                case TikConnectionType.Rest: otherPort = 443; break;
                case TikConnectionType.RestSsl: otherPort = 80; break;
                default: Assert.Inconclusive($"'{connType}' has no plain/TLS port pair."); return;
            }

            var setup = LabSetup(connType);
            setup.Port = otherPort;
            setup.ConnectTimeout = TimeSpan.FromSeconds(10);
            try
            {
                setup.Create(connType).Dispose();
                Assert.Fail($"'{connType}' opened on port {otherPort}.");
            }
            catch (Exception ex) when (!(ex is AssertFailedException))
            {
                Console.WriteLine($"{connType} on {otherPort}: {ex.GetType().Name} — {ex.Message}");
                Assert.IsTrue(ex is TikConnectionLoginException || ex is TikConnectionProtocolMismatchException
                              || ex is TikConnectionSSLErrorException,
                    $"'{connType}' on port {otherPort} threw {ex.GetType().FullName}, outside the open-failure taxonomy: {ex}");
            }
        }
    }
}
