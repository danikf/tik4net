// FakeCommandParityTests.cs — TikFakeCommand answers as the binary API's own command does. Each case runs twice: once
// through the real ApiConnection/ApiCommand against FakeRouterServer, once through TikFakeConnection with the same
// scripted answer, and both must send the same rows and end the same way (the value, or the exception type). A case
// added here is the guard against the fake drifting from the command it stands in for.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;
using tik4net.Testing;
using tik4net.unittests.Api;

namespace tik4net.unittests.Testing
{
    [TestClass]
    [DoNotParallelize]   // a fake peer answers within a receive timeout
    public class FakeCommandParityTests
    {
        private sealed class Case
        {
            public Case(string name, string commandText, Action<ITikCommand> setup, Func<ITikCommand, object?> act,
                params string[][] answer)
            {
                Name = name;
                CommandText = commandText;
                Setup = setup;
                Act = act;
                Answer = answer;
            }

            public string Name { get; }
            public string CommandText { get; }
            public Action<ITikCommand> Setup { get; }
            public Func<ITikCommand, object?> Act { get; }
            public string[][] Answer { get; }
            public override string ToString() => Name;
        }

        private static readonly string[] Done = { "!done" };
        private static string[] Row(params string[] words) => new[] { "!re" }.Concat(words).ToArray();
        private static string[] Trap(string message) => new[] { "!trap", "=message=" + message };

        private static readonly Action<ITikCommand> NoSetup = _ => { };

        private static IEnumerable<object[]> Cases()
        {
            Case[] cases =
            {
                // What goes on the wire.
                new Case("an unformatted parameter of a read is a filter", "/ip/address/print",
                    c => c.WithParameter("disabled", "yes"), c => Rows(c.ExecuteList()), Done),
                new Case("AddParameter leaves the format to the read", "/ip/address/print",
                    c => c.AddParameter("disabled", "yes"), c => Rows(c.ExecuteList()), Done),
                new Case("an unformatted parameter of a write is a value", "/ip/address/set",
                    c => c.AddParameter("disabled", "yes"), c => { c.ExecuteNonQuery(); return "ok"; }, Done),
                new Case("the command's default format wins over the read's", "/ip/address/print",
                    c => { c.DefaultParameterFormat = TikCommandParameterFormat.NameValue; c.AddParameter("detail", ""); },
                    c => Rows(c.ExecuteList()), Done),
                new Case("a path without its slash gets one", "ip/address/print",
                    NoSetup, c => Rows(c.ExecuteList()), Done),
                new Case("ExecuteList(fields) sends the field list", "/ip/address/print",
                    NoSetup, c => Rows(c.ExecuteList("address", "interface")), Row("=address=a", "=interface=b"), Done),
                new Case("ExecuteScalar(target) sends the field list", "/ip/address/print",
                    NoSetup, c => c.ExecuteScalar("address"), Row("=address=a"), Done),
                new Case("a print argument makes a scalar's parameters values", "/ip/address/print",
                    c => c.AddParameter("count-only", ""), c => c.ExecuteScalar(), Done.Concat(new[] { "=ret=3" }).ToArray()),
                new Case("a client marker is not sent", "/ip/address/print",
                    c => c.AddParameter(TikSpecialProperties.CliFlags, "disabled"), c => Rows(c.ExecuteList()), Done),

                // What an answer means.
                new Case("scalar: !done with =ret=", "/ip/address/add",
                    NoSetup, c => c.ExecuteScalar(), new[] { "!done", "=ret=*9" }),
                new Case("scalar: a bare !done after print", "/ip/address/print",
                    NoSetup, c => c.ExecuteScalar(), Done),
                new Case("scalar: a bare !done after set", "/ip/address/set",
                    NoSetup, c => c.ExecuteScalar(), Done),
                new Case("scalar: a bare !done after add", "/ip/address/add",
                    NoSetup, c => c.ExecuteScalar(), Done),
                new Case("scalar or default: a bare !done after add", "/ip/address/add",
                    NoSetup, c => c.ExecuteScalarOrDefault("d"), Done),
                new Case("scalar or default: a bare !done after print", "/ip/address/print",
                    NoSetup, c => c.ExecuteScalarOrDefault("d"), Done),
                new Case("scalar or default: two rows", "/ip/address/print",
                    NoSetup, c => c.ExecuteScalarOrDefault("d"), Row("=name=a"), Row("=name=b"), Done),
                new Case("scalar: one row, one field", "/system/identity/print",
                    NoSetup, c => c.ExecuteScalar(), Row("=name=r1"), Done),
                new Case("scalar: one row, two fields", "/system/identity/print",
                    NoSetup, c => c.ExecuteScalar(), Row("=name=r1", "=other=x"), Done),
                new Case("scalar(target): no row", "/ip/address/print",
                    NoSetup, c => c.ExecuteScalar("name"), Done),
                new Case("scalar(target): two rows", "/ip/address/print",
                    NoSetup, c => c.ExecuteScalar("name"), Row("=name=a"), Row("=name=b"), Done),
                new Case("scalar(target): the row lacks the field", "/ip/address/print",
                    NoSetup, c => c.ExecuteScalar("name"), Row("=other=a"), Done),
                new Case("scalar or default(target): two rows", "/ip/address/print",
                    NoSetup, c => c.ExecuteScalarOrDefault("d", "name"), Row("=name=a"), Row("=name=b"), Done),
                new Case("scalar or default(target): the row lacks the field", "/ip/address/print",
                    NoSetup, c => c.ExecuteScalarOrDefault("d", "name"), Row("=other=a"), Done),
                new Case("list of get: the value is a row named after value-name", "/system/identity/get",
                    c => c.AddParameter("value-name", "name"), c => Rows(c.ExecuteList()), new[] { "!done", "=ret=r1" }),
                new Case("list of get: without value-name the row is ret", "/system/identity/get",
                    NoSetup, c => Rows(c.ExecuteList()), new[] { "!done", "=ret=name=r1" }),
                new Case("list of get: a bare !done is no row", "/interface/get",
                    c => c.AddParameter(".id", "*FFFF"), c => Rows(c.ExecuteList()), Done),
                new Case("list of print: the !done's words are not a row", "/ip/address/print",
                    NoSetup, c => Rows(c.ExecuteList()), new[] { "!done", "=ret=3" }),
                new Case("single row: none", "/ip/address/print",
                    NoSetup, c => c.ExecuteSingleRow().Words.Count, Done),
                new Case("single row: two", "/ip/address/print",
                    NoSetup, c => c.ExecuteSingleRow().Words.Count, Row("=a=1"), Row("=a=2"), Done),
                new Case("single row or default: none", "/ip/address/print",
                    NoSetup, c => c.ExecuteSingleRowOrDefault()?.Words.Count, Done),
                new Case("single row or default: two", "/ip/address/print",
                    NoSetup, c => c.ExecuteSingleRowOrDefault()?.Words.Count, Row("=a=1"), Row("=a=2"), Done),
                new Case("non-query discards printed rows", "/system/script/run",
                    NoSetup, c => { c.ExecuteNonQuery(); return "ok"; }, Row("=status=x"), Done),
                new Case("non-query: !fatal after a reboot is success", "/system/reboot",
                    NoSetup, c => { c.ExecuteNonQuery(); return "ok"; }, new[] { "!fatal", "=message=rebooting" }),
                new Case("a trap is classified", "/ip/address/remove",
                    NoSetup, c => { c.ExecuteNonQuery(); return "ok"; }, Trap("no such item"), Done),
                new Case("list: a trap is classified", "/ip/address/print",
                    NoSetup, c => Rows(c.ExecuteList()), Trap("no such command prefix"), Done),
                new Case("duration read: a trap is an abort", "/interface/monitor-traffic",
                    NoSetup, c => Rows(c.ExecuteListWithDuration(TimeSpan.FromSeconds(5))), Row("=a=1"), Trap("boom"), Done),
                new Case("duration read (out): a trap is reported, not thrown", "/interface/monitor-traffic",
                    NoSetup, c =>
                    {
                        var rows = c.ExecuteListWithDuration(TimeSpan.FromSeconds(5), out bool aborted, out string? why);
                        return Rows(rows) + " aborted=" + aborted + " why=" + why;
                    }, Row("=a=1"), Trap("boom"), Done),
                new Case("until-done read: a trap is the classified trap", "/tool/fetch",
                    NoSetup, c => Rows(c.ExecuteListUntilDone(TimeSpan.FromSeconds(5))), Row("=a=1"), Trap("boom"), Done),
                new Case("until-done read: the rows to !done", "/tool/fetch",
                    NoSetup, c => Rows(c.ExecuteListUntilDone(TimeSpan.FromSeconds(5))), Row("=a=1"), Row("=a=2"), Done),
            };
            return cases.Select(c => new object[] { c });
        }

        public static string CaseName(System.Reflection.MethodInfo _, object[] data) => data[0].ToString()!;

        [TestMethod]
        [DynamicData(nameof(Cases), DynamicDataSourceType.Method, DynamicDataDisplayName = nameof(CaseName))]
        public void TheFakeAnswersAsTheBinaryApiCommandDoes(object testCase)
        {
            var c = (Case)testCase;
            var (realOutcome, realSent) = RunReal(c);
            var (fakeOutcome, fakeSent) = RunFake(c);

            Assert.AreEqual(string.Join(" | ", realSent), string.Join(" | ", fakeSent), "the rows sent");
            Assert.AreEqual(realOutcome, fakeOutcome, "how the call ended");
        }

        // The binary API echoes the command's .tag into the rows of a callback-driven read; it addresses the answer and is
        // not part of it, so it is left out of the comparison.
        private static string Rows(IEnumerable<ITikReSentence> rows)
            => "[" + string.Join("; ", rows.Select(r => string.Join(",", r.Words
                .Where(w => w.Key != TikSpecialProperties.Tag).Select(w => w.Key + "=" + w.Value)))) + "]";

        private static string Outcome(Func<object?> act)
        {
            try
            {
                return "= " + (act() ?? "(null)");
            }
            catch (TikCommandAbortException ex)
            {
                return "throws " + ex.GetType().Name + " / " + ex.InnerException?.GetType().Name;
            }
            catch (Exception ex)
            {
                return "throws " + ex.GetType().Name;
            }
        }

        private static (string Outcome, string[] Sent) RunReal(Case c)
        {
            string[] sent = Array.Empty<string>();
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                 // login
                server.WriteSentence("!done");
                while (true)
                {
                    var words = server.ReadSentence();
                    if (words[0] == "/console/inspect")
                    {
                        // The filter check asks for the menu's fields first; RouterOS 6 refuses, and so does this peer.
                        server.WriteSentence("!trap", "=message=no such command prefix");
                        server.WriteSentence("!done");
                        continue;
                    }
                    sent = words.Where(w => !w.StartsWith(TikSpecialProperties.Tag + "=", StringComparison.Ordinal)).ToArray();
                    foreach (var sentence in c.Answer)
                        server.WriteSentence(sentence);
                    break;
                }
                try
                {
                    var quit = server.ReadSentence();   // /quit, sent by Close()
                    server.WriteSentence("!fatal", "=message=session terminated on request");
                }
                catch (Exception)
                {
                    // the answer ended the session itself (!fatal)
                }
            });

            string outcome;
            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, "admin", "secret");
                var cmd = connection.CreateCommand(c.CommandText);
                c.Setup(cmd);
                outcome = Outcome(() => c.Act(cmd));
            }
            Assert.IsTrue(serverTask.Wait(TimeSpan.FromSeconds(10)), "the scripted peer did not finish");
            return (outcome, sent);
        }

        private static (string Outcome, string[] Sent) RunFake(Case c)
        {
            var connection = new TikFakeConnection().WithResponse(_ => true, c.Answer.Select(ToSentence).ToList());
            var cmd = connection.CreateCommand(c.CommandText);
            c.Setup(cmd);
            string outcome = Outcome(() => c.Act(cmd));
            return (outcome, connection.SentCommands.LastOrDefault() ?? Array.Empty<string>());
        }

        private static ITikSentence ToSentence(string[] words)
        {
            var fields = words.Skip(1).ToDictionary(
                w => w.Substring(1, w.IndexOf('=', 1) - 1),
                w => w.Substring(w.IndexOf('=', 1) + 1));
            switch (words[0])
            {
                case "!re": return new TikFakeReSentence(fields);
                case "!done": return new TikFakeDoneSentence(fields);
                case "!trap": return new TikFakeTrapSentence(fields["message"]);
                case "!fatal": return new TikFakeFatalSentence(fields["message"]);
                default: throw new ArgumentException(words[0]);
            }
        }
    }
}
