using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Configuration;
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
            string host = ConfigurationManager.AppSettings["host"];
            string user = ConfigurationManager.AppSettings["user"];
            string pass = ConfigurationManager.AppSettings["pass"] ?? "";

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
    }
}
