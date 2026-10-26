// ApiScalarAbsentFieldTests.cs — ExecuteScalar(target) / ExecuteScalarOrDefault(default, target) on the binary API when
// the one row lacks the named field (command-layer X15). The router answers such a print with an empty !re, recorded
// live (RouterOS 7.25):
//   /system/identity/print =.proplist=no-such-field  →  !re (no words), !done
// The answer is the generic command's: TikSentenceException "Field '<f>' is not in the row the router returned.", or the
// caller's default for the OrDefault forms.

#nullable enable

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
    public class ApiScalarAbsentFieldTests
    {
        private static string ExtractTag(IEnumerable<string> words)
            => words.Single(w => w.StartsWith(TikSpecialProperties.Tag + "=", StringComparison.Ordinal))
                    .Substring((TikSpecialProperties.Tag + "=").Length);

        private static string Tag(string tag) => $"{TikSpecialProperties.Tag}={tag}";

        /// <summary>Logs in, runs <paramref name="call"/> and answers its one command with one row of
        /// <paramref name="rowWords"/> and !done. Returns what the call returned.</summary>
        private static string? Exchange(Func<ITikCommand, string?> call, params string[] rowWords)
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");

                var command = server.ReadSentence();
                string t = ExtractTag(command);
                server.WriteSentence(new[] { "!re", Tag(t) }.Concat(rowWords).ToArray());
                server.WriteSentence("!done", Tag(t));

                server.ReadSentence();                       // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            string? result;
            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, "admin", "secret");
                try
                {
                    result = call(connection.CreateCommand("/system/identity/print"));
                }
                finally
                {
                    connection.Close();
                }
            }

            Assert.IsTrue(serverTask.Wait(5000));
            return result;
        }

        private static string? Run(Func<Task<string?>> call) => call().GetAwaiter().GetResult();

        // ── red on the old code: the row lacks the named field ─────────────────

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ExecuteScalar_AbsentTarget_ThrowsTikSentenceException(bool async)
        {
            var ex = Assert.ThrowsException<TikSentenceException>(() =>
                Exchange(c => async ? Run(async () => await c.ExecuteScalarAsync("no-such-field")) : c.ExecuteScalar("no-such-field")));
            StringAssert.Contains(ex.Message, "Field 'no-such-field' is not in the row the router returned.");
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ExecuteScalarOrDefault_AbsentTarget_ReturnsTheDefault(bool async)
        {
            Assert.AreEqual("dflt",
                Exchange(c => async ? Run(() => c.ExecuteScalarOrDefaultAsync("dflt", "no-such-field")) : c.ExecuteScalarOrDefault("dflt", "no-such-field")));
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ExecuteScalarOrDefault_AbsentTarget_NullDefault_ReturnsNull(bool async)
        {
            Assert.IsNull(
                Exchange(c => async ? Run(() => c.ExecuteScalarOrDefaultAsync(null, "no-such-field")) : c.ExecuteScalarOrDefault(null, "no-such-field")));
        }

        // ── pins: green before and after ──────────────────────────────────────

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ExecuteScalar_PresentTarget_ReturnsItsValue(bool async)
        {
            Assert.AreEqual("MikroTik",
                Exchange(c => async ? Run(async () => await c.ExecuteScalarAsync("name")) : c.ExecuteScalar("name"), "=name=MikroTik"));
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ExecuteScalarOrDefault_PresentTarget_ReturnsItsValue(bool async)
        {
            Assert.AreEqual("MikroTik",
                Exchange(c => async ? Run(() => c.ExecuteScalarOrDefaultAsync("dflt", "name")) : c.ExecuteScalarOrDefault("dflt", "name"), "=name=MikroTik"));
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ExecuteScalar_NoTarget_SingleFieldRow_ReturnsThatField(bool async)
        {
            Assert.AreEqual("MikroTik",
                Exchange(c => async ? Run(async () => await c.ExecuteScalarAsync()) : c.ExecuteScalar(), "=name=MikroTik"));
        }
    }
}
