using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;
using tik4net.unittests.Api;
using tik4net.WinboxNative;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// Safe Mode means the same on every transport (<see cref="ITikSafeModeConnection"/>): a take while held, and a
    /// release or unroll while nothing is held, are no-ops that send nothing; an unroll a transport cannot do in place
    /// closes the connection, which rolls back.
    /// </summary>
    [TestClass]
    [DoNotParallelize]   // a fake peer answers within a receive timeout
    public class SafeModeSemanticsTests
    {
        [TestMethod]
        public void Api_SendsOnlyTheCallsThatChangeTheHeldState()
        {
            var commands = new List<string>();
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                    // login
                server.WriteSentence("!done");
                while (true)
                {
                    string command = server.ReadSentence()[0];
                    commands.Add(command);
                    if (command == "/quit")
                    {
                        server.WriteSentence("!fatal", "=message=session terminated on request");
                        return;
                    }
                    server.WriteSentence("!done", "=ret=true");
                }
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, "admin", "");

                connection.SafeModeRelease();            // nothing held: nothing sent
                connection.SafeModeUnroll();             // nothing held: nothing sent
                connection.SafeModeTake();
                connection.SafeModeTake();               // already held: nothing sent
                Assert.IsTrue(connection.SafeModeGet());
                connection.SafeModeRelease();
                connection.SafeModeTake();
                connection.SafeModeUnroll();
                Assert.IsFalse(connection.SafeModeGet());

                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
            CollectionAssert.AreEqual(
                new[] { "/safe-mode/take", "/safe-mode/release", "/safe-mode/take", "/safe-mode/unroll", "/quit" },
                commands);
        }

        [TestMethod]
        public void WinboxNative_UnrollIsNotRefused()
        {
            // Native has no in-place unroll; its unroll closes the connection, which rolls back. Unopened, that is the
            // not-open error every Safe Mode call gives, not NotSupportedException.
            using (var connection = new WinboxNativeConnection())
                Assert.ThrowsException<TikConnectionNotOpenException>(() => connection.SafeModeUnroll());
        }
    }
}
