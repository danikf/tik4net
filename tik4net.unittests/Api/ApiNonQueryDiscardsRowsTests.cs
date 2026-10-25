// ApiNonQueryDiscardsRowsTests.cs — ExecuteNonQuery on the binary API runs the command and discards whatever rows it
// printed, as ADO.NET's DbCommand.ExecuteNonQuery does (command-layer X14). Only what failed throws: a trap (classified),
// and the rows before it do not turn it into a success.
//
// The progress rows are the router's own shape, recorded live (RouterOS 7.25):
//   /system/package/update/check-for-updates  →  !re =status=… =.section=0, !re =status=… =.section=1, !done

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    [TestClass]
    [DoNotParallelize]   // a fake peer answers within a receive timeout
    public class ApiNonQueryDiscardsRowsTests
    {
        private static string ExtractTag(IEnumerable<string> words)
            => words.Single(w => w.StartsWith(TikSpecialProperties.Tag + "=", StringComparison.Ordinal))
                    .Substring((TikSpecialProperties.Tag + "=").Length);

        private static string Tag(string tag) => $"{TikSpecialProperties.Tag}={tag}";

        /// <summary>Logs in, runs the non-query and answers its one command with <paramref name="reply"/>.</summary>
        private static void Exchange(string commandText, bool async, Action<FakeRouterServer, string> reply)
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");

                var command = server.ReadSentence();
                reply(server, ExtractTag(command));

                server.ReadSentence();                       // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, "admin", "secret");
                try
                {
                    var cmd = connection.CreateCommand(commandText);
                    if (async) cmd.ExecuteNonQueryAsync().GetAwaiter().GetResult();
                    else cmd.ExecuteNonQuery();
                }
                finally
                {
                    connection.Close();
                }
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ARowThenDone_Succeeds(bool async)
        {
            Exchange("/system/resource/print", async, (s, t) =>
            {
                s.WriteSentence("!re", Tag(t), "=uptime=1d", "=version=7.25");
                s.WriteSentence("!done", Tag(t));
            });
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ProgressRowsThenDone_Succeed(bool async)
        {
            Exchange("/system/package/update/check-for-updates", async, (s, t) =>
            {
                s.WriteSentence("!re", Tag(t), "=status=finding out latest version...", "=.section=0");
                s.WriteSentence("!re", Tag(t), "=status=System is already up to date", "=.section=1");
                s.WriteSentence("!done", Tag(t));
            });
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void SectionOnlyProgressThenDone_Succeeds(bool async)
        {
            Exchange("/system/script/run", async, (s, t) =>
            {
                s.WriteSentence("!re", Tag(t), "=.section=0");
                s.WriteSentence("!done", Tag(t));
            });
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ARowThenTrap_ThrowsTheClassifiedTrap(bool async)
        {
            Assert.ThrowsException<TikNoSuchItemException>(() =>
                Exchange("/interface/ethernet/monitor", async, (s, t) =>
                {
                    s.WriteSentence("!re", Tag(t), "=rx=1");
                    s.WriteSentence("!trap", Tag(t), "=category=4", "=message=no such item (4)");
                    s.WriteSentence("!done", Tag(t));
                }));
        }
    }
}
