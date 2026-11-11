using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    /// <summary>
    /// The pre-6.43 MD5 login is unencrypted, so a WinBox session never falls back to it on its own: a router that does
    /// not answer EC-SRP5, or stays silent, fails the open — the way an attacker who drops the EC-SRP5 reply would
    /// otherwise downgrade the session to clear text (security review D2). Only <c>AllowLegacyWinboxLogin</c> opts in.
    /// </summary>
    [TestClass]
    public class WinboxLegacyLoginOptInTests
    {
        [TestMethod]
        public void ARouterRefusingEcSrp5_FailsTheOpen_AndNoLegacyLoginIsTried()
        {
            using (var server = new FakeWinboxServer())
            using (var session = new WinboxM2Session())
            {
                var serverTask = Task.Run(() =>
                {
                    server.AcceptClient();
                    server.RejectEcSrp5();
                });

                var ex = Assert.ThrowsException<WinboxEcSrp5UnsupportedException>(
                    () => session.Open("127.0.0.1", server.Port, "admin", "", 5000, 5000));
                StringAssert.Contains(ex.Message, "6.43");
                StringAssert.Contains(ex.Message, "AllowLegacyWinboxLogin");
                serverTask.Wait(5000);

                // The legacy login reconnects before it starts: no second connection means no downgrade.
                Assert.ThrowsException<TimeoutException>(() => server.AcceptClient(1500),
                    "the session reconnected for the unencrypted legacy login without being asked to");
            }
        }

        [TestMethod]
        public void ASilentRouter_FailsTheOpen_AndNoLegacyLoginIsTried()
        {
            using (var server = new FakeWinboxServer())
            using (var session = new WinboxM2Session())
            {
                // Accept and say nothing: what a man in the middle dropping the EC-SRP5 challenge looks like.
                var serverTask = Task.Run(() => server.AcceptClient());

                Assert.ThrowsException<WinboxEcSrp5UnsupportedException>(
                    () => session.Open("127.0.0.1", server.Port, "admin", "", 1000, 1000));
                serverTask.Wait(5000);
                Assert.ThrowsException<TimeoutException>(() => server.AcceptClient(1500),
                    "silence downgraded the session to the unencrypted legacy login");
            }
        }

        [TestMethod]
        public void WithTheOptIn_ARouterRefusingEcSrp5_GetsTheLegacyLogin()
        {
            using (var server = new FakeWinboxServer())
            using (var session = new WinboxM2Session { AllowLegacyLogin = true })
            {
                var serverTask = Task.Run(() => server.RunFullLoginSequence());
                session.Open("127.0.0.1", server.Port, "admin", "", 5000, 5000);
                Assert.IsTrue(serverTask.Wait(5000));
                Assert.IsFalse(session.IsEncrypted, "the legacy session is in clear: that is what the option opts in to");
            }
        }

        [TestMethod]
        public void TheConnectionHandsTheOptionToItsCarrier()
        {
            // Through the setup, as a caller gets it: off by default, and the refusal names the option.
            using (var server = new FakeWinboxServer())
            {
                var serverTask = Task.Run(() =>
                {
                    server.AcceptClient();
                    server.RejectEcSrp5();
                });
                var setup = new TikConnectionSetup("127.0.0.1", "admin", "") { Port = server.Port, ConnectTimeout = TimeSpan.FromSeconds(5) };
                var ex = Assert.ThrowsException<TikConnectionLoginException>(() => setup.Create(TikConnectionType.WinboxNative).Dispose());
                StringAssert.Contains(ex.Message, "AllowLegacyWinboxLogin");
                serverTask.Wait(5000);
                Assert.ThrowsException<TimeoutException>(() => server.AcceptClient(1500));
            }

            using (var server = new FakeWinboxServer())
            {
                var serverTask = Task.Run(() =>
                {
                    server.AcceptClient();
                    server.RejectEcSrp5();
                    server.AcceptClient();   // the legacy login's reconnect: the option reached the carrier
                });
                var setup = new TikConnectionSetup("127.0.0.1", "admin", "")
                {
                    Port = server.Port, ConnectTimeout = TimeSpan.FromSeconds(2), ReceiveTimeout = TimeSpan.FromSeconds(2),
                    AllowLegacyWinboxLogin = true,
                };
                var open = Task.Run(() => { try { setup.Create(TikConnectionType.WinboxNative).Dispose(); } catch (Exception) { /* the fake stops here */ } });
                Assert.IsTrue(serverTask.Wait(10000), "no reconnect for the legacy login");
                server.Dispose();
                open.Wait(15000);
            }
        }
    }
}
