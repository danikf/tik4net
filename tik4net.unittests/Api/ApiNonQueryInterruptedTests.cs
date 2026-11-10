using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    /// <summary>
    /// <c>ExecuteNonQuery</c> counts a <c>!fatal</c> as success only when the router sent it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A router that ends the session after running the command — <c>/system/reboot</c>, <c>shutdown</c> — answers with a
    /// <c>!fatal</c>, and the write did happen, so that one is a success. The reader loop also ends every wait with a
    /// <c>!fatal</c> of its own making when the caller closes the connection or the socket dies, and those say nothing of
    /// the kind: the write may never have reached the router. Reporting them as success gave the caller a write that read as
    /// done when its fate was unknown. A close is <c>ClosedWhileRunning</c> (<see cref="TikConnectionNotOpenException"/>),
    /// as on the CLI and REST transports; a lost connection is <see cref="TikCommandFatalException"/> carrying its cause.
    /// </para>
    /// <para>
    /// The same interpreter serves the synchronous and both asynchronous forms, so the asynchronous one is pinned too.
    /// </para>
    /// </remarks>
    [TestClass]
    [DoNotParallelize]   // a fake peer answers within a receive timeout
    public class ApiNonQueryInterruptedTests
    {
        private static ITikCommand SetCommand(ApiConnection connection)
            => connection.CreateCommand("/ip/address/set", connection.CreateParameter(".id", "*1"),
                connection.CreateParameter("comment", "x"));

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void CloseUnderARunningNonQuery_IsClosedWhileRunning(bool awaited)
        {
            using var server = new FakeRouterServer();
            var commandArrived = new ManualResetEventSlim(false);
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence(); server.WriteSentence("!done");   // login
                server.ReadSentence();                                  // the write — never answered
                commandArrived.Set();
                server.ReadSentence();                                  // /quit from Close
                server.WriteSentence("!fatal", "session terminated on request");
            });

            using var connection = new ApiConnection(false);
            connection.ReceiveTimeout = TimeSpan.FromSeconds(5);
            connection.Open("127.0.0.1", server.Port, "admin", "");
            var cmd = SetCommand(connection);

            var write = awaited ? cmd.ExecuteNonQueryAsync() : Task.Run(() => cmd.ExecuteNonQuery());
            Assert.IsTrue(commandArrived.Wait(5000));
            connection.Close();

            Exception caught = null;
            try { write.Wait(5000); } catch (AggregateException ex) { caught = ex.InnerException; }
            Assert.IsTrue(write.IsCompleted, "the write never returned");
            Assert.IsNotNull(caught,
                "ExecuteNonQuery returned normally although the connection was closed under it and the router never "
                + "answered: a write whose fate is unknown read as done");
            Assert.IsInstanceOfType(caught, typeof(TikConnectionNotOpenException), caught.ToString());
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ARouterDroppingTheSocketUnderANonQuery_IsTikCommandFatalExceptionWithItsCause(bool awaited)
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence(); server.WriteSentence("!done");   // login
                server.ReadSentence();                                  // the write
                server.CloseClientConnection();                         // gone, no answer at all
            });

            using var connection = new ApiConnection(false);
            connection.ReceiveTimeout = TimeSpan.FromSeconds(5);
            connection.Open("127.0.0.1", server.Port, "admin", "");
            var cmd = SetCommand(connection);

            Exception caught = null;
            try
            {
                if (awaited) cmd.ExecuteNonQueryAsync().GetAwaiter().GetResult();
                else cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { caught = ex; }
            Assert.IsNotNull(caught,
                "ExecuteNonQuery returned normally on a connection that died before answering: a lost write reads as done");
            Assert.IsInstanceOfType(caught, typeof(TikCommandFatalException), caught.ToString());
            Assert.IsNotNull(caught.InnerException, "the loss's cause is what tells a reset from an EOF: " + caught);
            StringAssert.Contains(caught.Message, "connection lost");
        }

        // The router's own !fatal after running the command (the shape of /system/reboot) stays a success.
        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void TheRoutersOwnFatal_IsStillASuccess(bool awaited)
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence(); server.WriteSentence("!done");   // login
                server.ReadSentence();                                  // /system/reboot
                server.WriteSentence("!fatal", "system rebooting");     // the router's own answer…
                server.CloseClientConnection();                         // …and it goes down
            });

            using var connection = new ApiConnection(false);
            connection.ReceiveTimeout = TimeSpan.FromSeconds(5);
            connection.Open("127.0.0.1", server.Port, "admin", "");
            var cmd = connection.CreateCommand("/system/reboot");

            if (awaited) cmd.ExecuteNonQueryAsync().GetAwaiter().GetResult();
            else cmd.ExecuteNonQuery();
            Assert.IsTrue(serverTask.Wait(5000));
        }
    }
}
