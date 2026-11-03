// ApiGetListTests.cs — ExecuteList of `get` on the binary API answers with the row the other transports answer with
// (TikGetResult). The API puts a get's value in the !done's =ret= word, recorded live (RouterOS 7.25):
//   /system/identity/get =value-name=name  →  !done =ret=CHR
//   /system/identity/get                   →  !done =ret=name=CHR
//   /interface/get =.id=*FFFF =value-name=name  →  !done   (no such row: no =ret=, no trap)
// A list read that dropped the !done's words answered every get with no rows.

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
    public class ApiGetListTests
    {
        private static string Tag(string tag) => $"{TikSpecialProperties.Tag}={tag}";

        /// <summary>Logs in, runs <paramref name="call"/> on <paramref name="commandText"/> and answers its one command
        /// with <paramref name="doneWords"/> on a !done. Returns what the call returned and the words it sent.</summary>
        private static (string Rows, string[] Sent) Exchange(string commandText, Action<ITikCommand> setup,
            Func<ITikCommand, IEnumerable<ITikReSentence>> call, params string[] doneWords)
        {
            string[] sent = Array.Empty<string>();
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");

                var command = server.ReadSentence();
                string t = command.Single(w => w.StartsWith(TikSpecialProperties.Tag + "=", StringComparison.Ordinal))
                    .Substring((TikSpecialProperties.Tag + "=").Length);
                sent = command.Where(w => !w.StartsWith(TikSpecialProperties.Tag + "=", StringComparison.Ordinal)).ToArray();
                server.WriteSentence(new[] { "!done", Tag(t) }.Concat(doneWords).ToArray());

                server.ReadSentence();                       // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            string rows;
            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, "admin", "secret");
                try
                {
                    var cmd = connection.CreateCommand(commandText);
                    setup(cmd);
                    rows = Format(call(cmd));
                }
                finally
                {
                    connection.Close();
                }
            }

            Assert.IsTrue(serverTask.Wait(5000));
            return (rows, sent);
        }

        private static string Format(IEnumerable<ITikReSentence> rows)
            => "[" + string.Join("; ", rows.Select(r => string.Join(",", r.Words
                .Where(w => w.Key != TikSpecialProperties.Tag).Select(w => w.Key + "=" + w.Value)))) + "]";

        private static IEnumerable<ITikReSentence> Sync(ITikCommand c) => c.ExecuteList();

        private static IEnumerable<ITikReSentence> Async(ITikCommand c) => c.ExecuteListAsync().GetAwaiter().GetResult();

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void Get_WithValueName_IsOneRowNamedAfterTheField(bool async)
        {
            var (rows, _) = Exchange("/system/identity/get", c => c.AddParameter("value-name", "name", TikCommandParameterFormat.NameValue),
                async ? Async : Sync, "=ret=CHR");
            Assert.AreEqual("[name=CHR]", rows);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void Get_WithoutValueName_IsOneRowOfRet(bool async)
        {
            var (rows, _) = Exchange("/system/identity/get", _ => { }, async ? Async : Sync, "=ret=name=CHR");
            Assert.AreEqual("[ret=name=CHR]", rows);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void Get_BareDone_IsNoRows(bool async)
        {
            var (rows, _) = Exchange("/interface/get",
                c => { c.AddParameter(".id", "*FFFF", TikCommandParameterFormat.NameValue); c.AddParameter("value-name", "name", TikCommandParameterFormat.NameValue); },
                async ? Async : Sync);
            Assert.AreEqual("[]", rows);
        }

        [TestMethod]
        public void Get_UnformattedParameters_AreSentAsValues()
        {
            // get's .id and value-name are its arguments; as filters the API reads them as a query.
            var (rows, sent) = Exchange("/interface/get",
                c => { c.AddParameter(".id", "*1"); c.AddParameter("value-name", "name"); }, Sync, "=ret=lo");
            Assert.AreEqual("/interface/get | =.id=*1 | =value-name=name", string.Join(" | ", sent));
            Assert.AreEqual("[name=lo]", rows);
        }

        [TestMethod]
        public void Print_DoneWords_AreNotARow()
        {
            // Only get reads the !done: a print's rows are its !re sentences, whatever the !done carries.
            var (rows, _) = Exchange("/ip/address/print", _ => { }, Sync, "=ret=3");
            Assert.AreEqual("[]", rows);
        }
    }
}
