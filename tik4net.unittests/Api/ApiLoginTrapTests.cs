using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    /// <summary>
    /// A <c>!trap</c> answering <c>/login</c> is the router refusing the login in its own words — wrong
    /// credentials, or a user whose group lacks the <c>api</c> policy (<c>std failure: not allowed (9)</c>).
    /// Every such trap surfaces as <see cref="TikConnectionLoginRefusedException"/>, as it does on WinBox.
    /// </summary>
    [TestClass]
    [DoNotParallelize]   // a fake peer answers within a receive timeout
    public class ApiLoginTrapTests
    {
        private static TikConnectionLoginRefusedException OpenAgainstTrap(string message)
        {
            using var server = new FakeRouterServer();

            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                var login = server.ReadSentence();
                string tag = login.FirstOrDefault(w => w.StartsWith(TikSpecialProperties.Tag + "=", StringComparison.Ordinal));
                var reply = new List<string> { "!trap", "=message=" + message };
                if (tag != null) reply.Add(tag);
                server.WriteSentence(reply.ToArray());
                if (tag != null) server.WriteSentence("!done", tag);
            });

            using var connection = new ApiConnection(false);
            connection.ReceiveTimeout = TimeSpan.FromMilliseconds(5000);
            var ex = Assert.ThrowsException<TikConnectionLoginRefusedException>(
                () => connection.Open("127.0.0.1", server.Port, "limited", "pw"));
            serverTask.Wait(5000);
            return ex;
        }

        [TestMethod]
        public void NotAllowedTrap_IsALoginRefusal_WithTheRoutersText()
        {
            var ex = OpenAgainstTrap("std failure: not allowed (9)");
            Assert.AreEqual("std failure: not allowed (9)", ex.RouterMessage);
            Assert.AreEqual("API", ex.Transport);
        }

        [TestMethod]
        public void WrongCredentialsTrap_IsStillALoginException()
        {
            var ex = OpenAgainstTrap("invalid user name or password (6)");
            Assert.IsInstanceOfType(ex, typeof(TikConnectionLoginException));
            Assert.AreEqual("invalid user name or password (6)", ex.RouterMessage);
        }

        // ── The password never reaches the exception (security review X1) ─────

        private const string Password = "S3cretPw!";

        /// <summary>Opens against a peer answering <c>/login</c> with <paramref name="reply"/> (the tag is appended to each
        /// sentence) and returns what <c>Open</c> threw.</summary>
        private static Exception OpenAgainst(params string[][] reply)
        {
            using var server = new FakeRouterServer();

            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                var login = server.ReadSentence();
                string tag = login.FirstOrDefault(w => w.StartsWith(TikSpecialProperties.Tag + "=", StringComparison.Ordinal));
                foreach (var sentence in reply)
                    server.WriteSentence(tag == null ? sentence : sentence.Concat(new[] { tag }).ToArray());
            });

            using var connection = new ApiConnection(false);
            connection.ReceiveTimeout = TimeSpan.FromMilliseconds(3000);
            Exception thrown = null;
            try { connection.Open("127.0.0.1", server.Port, "admin", Password); }
            catch (Exception ex) { thrown = ex; }
            serverTask.Wait(5000);
            Assert.IsNotNull(thrown, "the login was accepted");
            return thrown;
        }

        private static void AssertNoPassword(Exception ex)
        {
            for (Exception e = ex; e != null; e = e.InnerException)
                Assert.IsFalse(e.Message.Contains(Password), e.GetType().Name + ".Message carries the password: " + e.Message);
            Assert.IsFalse(ex.ToString().Contains(Password), "ToString() carries the password: " + ex);
        }

        /// <summary>A peer answering <c>/login</c> with rows: the unexpected-response text listed every parameter.</summary>
        [TestMethod]
        public void ALoginAnsweredWithRows_KeepsThePasswordOutOfTheException()
        {
            var ex = OpenAgainst(new[] { "!re", "=a=1" }, new[] { "!re", "=a=2" }, new[] { "!done" });
            Assert.IsInstanceOfType(ex, typeof(TikConnectionLoginException));
            AssertNoPassword(ex);
        }

        /// <summary>A peer answering <c>/login</c> with <c>!fatal</c>: <c>ToString()</c> printed the login command.</summary>
        [TestMethod]
        public void ALoginAnsweredWithFatal_KeepsThePasswordOutOfTheException()
        {
            var ex = OpenAgainst(new[] { "!fatal", "not logged in" });
            Assert.IsInstanceOfType(ex, typeof(TikConnectionException));
            AssertNoPassword(ex);
        }

        /// <summary>A refusal in the router's words stays clean too, and keeps the router's text.</summary>
        [TestMethod]
        public void ALoginRefusal_KeepsThePasswordOutOfTheException()
        {
            var ex = OpenAgainst(new[] { "!trap", "=message=invalid user name or password (6)" }, new[] { "!done" });
            Assert.IsInstanceOfType(ex, typeof(TikConnectionLoginRefusedException));
            AssertNoPassword(ex);
        }

        [TestMethod]
        public void ALoginRefusal_IsNotAnApiLoginReset()
        {
            var ex = OpenAgainstTrap("std failure: not allowed (9)");
            Assert.IsFalse(tik4net.Connection.TikOpenRetry.IsApiLoginReset(ex),
                "a refusal must not be retried by the API's open");
        }
    }
}
