using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.MacTelnet;
using tik4net.Winbox;

namespace tik4net.unittests.MacTelnet
{
    /// <summary>
    /// A MAC-Telnet login the router ends with nothing on screen is a refusal whose text was lost, and is retried
    /// like one.
    /// </summary>
    /// <remarks>
    /// RouterOS refuses a MAC-Telnet login with one data packet (<c>Login failed, incorrect username or password</c>)
    /// and ends the session 1–4 ms later, without waiting for that packet's acknowledgement and without resending it
    /// (measured on 7.24, wrong-password logins). With that one datagram lost, the client sees only the end of the
    /// session. Dropping it on purpose produced <c>the router closed the session during login.</c> three times in three,
    /// each after a single attempt — the message of a one-off integration failure — while the same refusal with its
    /// text was retried, as the router also refuses about one valid login in a hundred.
    /// </remarks>
    [TestClass]
    public class MacTelnetLoginClosureTests
    {
        [TestMethod]
        public void AClosureWithNothingOnScreen_IsRetried()
        {
            int attempts = 0;
            RouterLoginRetry.Run(() =>
            {
                attempts++;
                if (attempts < 2)
                    throw MacTelnetUdpClient.RefusalOrClosure(string.Empty);
            });
            Assert.AreEqual(2, attempts);
        }

        [TestMethod]
        public void AClosureThatPersists_IsReportedAsAClosedSessionAfterTheRetries()
        {
            int attempts = 0;
            Exception thrown = null;
            try { RouterLoginRetry.Run(() => { attempts++; throw MacTelnetUdpClient.RefusalOrClosure("\r\n"); }); }
            catch (Exception ex) { thrown = ex; }
            Assert.IsInstanceOfType(thrown, typeof(TikConnectionSessionClosedException), "what a caller catches today");
            Assert.AreEqual(RouterLoginRetry.MaxAttempts, attempts);
            StringAssert.Contains(thrown.Message, "closed the session during login");
        }

        [TestMethod]
        public void AClosureThatSaysSomethingElse_IsNotRetried()
        {
            int attempts = 0;
            var thrown = Assert.ThrowsException<TikConnectionSessionClosedException>(() =>
                RouterLoginRetry.Run(() => { attempts++; throw MacTelnetUdpClient.RefusalOrClosure("some other reason\r\n"); }));
            Assert.AreEqual(1, attempts);
            StringAssert.Contains(thrown.Message, "some other reason");
        }

        [TestMethod]
        public void TheRefusalText_IsARefusal()
            => Assert.IsInstanceOfType(MacTelnetUdpClient.RefusalOrClosure("Login failed, incorrect username or password\r\n"),
                typeof(TikConnectionLoginRefusedException));
    }
}
