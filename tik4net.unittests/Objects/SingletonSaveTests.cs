// SingletonSaveTests.cs — a loaded singleton saves what changed, like any other entity.
//
// It used to send every writable field. That wrote back values the mapper had filled in for fields the router
// does not have: RouterOS 6.49.13's /tool/e-mail has no tls, certificate-verification or vrf, the load fills
// them with their defaults, and saving a changed server then failed with "unknown parameter" on every transport.

using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Tool;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    [TestClass]
    public class SingletonSaveTests
    {
        // The RouterOS 6.49.13 shape of /tool/e-mail: no tls, certificate-verification or vrf.
        private static TikFakeConnection RouterOs6Email()
            => new TikFakeConnection()
                .WithResponse(
                    cmd => cmd.FirstOrDefault() == "/tool/e-mail/print",
                    _ => new ITikSentence[]
                    {
                        new TikFakeReSentence(new Dictionary<string, string>
                        {
                            ["address"] = "0.0.0.0", ["port"] = "25", ["start-tls"] = "no", ["from"] = "<>",
                            ["user"] = "", ["password"] = "",
                        }),
                        new TikFakeDoneSentence(),
                    })
                .WithNonQuery(cmd => cmd.First() == "/tool/e-mail/set");

        private static string[] TheSet(TikFakeConnection connection)
            => connection.SentCommands.Single(c => c.First() == "/tool/e-mail/set");

        [TestMethod]
        public void ALoadedSingleton_SendsOnlyWhatChanged()
        {
            var connection = RouterOs6Email();
            var email = connection.LoadSingle<ToolEmail>();

            email.Server = "192.0.2.25";
            connection.Save(email);

            CollectionAssert.AreEquivalent(new[] { "/tool/e-mail/set", "=address=192.0.2.25" }, TheSet(connection));
        }

        [TestMethod]
        public void ALoadedSingletonThatDidNotChange_SendsNothing()
        {
            var connection = RouterOs6Email();
            var email = connection.LoadSingle<ToolEmail>();

            connection.Save(email);

            Assert.IsFalse(connection.SentCommands.Any(c => c.First() == "/tool/e-mail/set"));
        }

        [TestMethod]
        public void FullUpdate_ComparesAgainstAFreshRead()
        {
            var connection = RouterOs6Email();
            var email = connection.LoadSingle<ToolEmail>();

            email.Server = "192.0.2.25";
            connection.Save(email, saveMode: TikSaveMode.FullUpdate);

            CollectionAssert.AreEquivalent(new[] { "/tool/e-mail/set", "=address=192.0.2.25" }, TheSet(connection));
        }

        [TestMethod]
        public void ASingletonNeverLoaded_StillSendsEveryFieldItHolds()
        {
            // Nothing is known about the router's state, so everything the caller's object holds is sent, as before.
            var connection = RouterOs6Email();

            connection.Save(new ToolEmail { Server = "192.0.2.25" });

            var set = TheSet(connection);
            CollectionAssert.Contains(set, "=server=192.0.2.25");
            Assert.IsTrue(set.Any(w => w.StartsWith("=tls=")), "the other fields it holds go too (tls is non-nullable)");
        }
    }
}
