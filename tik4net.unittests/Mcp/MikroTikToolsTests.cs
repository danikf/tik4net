// MikroTikToolsTests.cs — the tik4net.mcp tools over the binary API against FakeRouterServer: the verb dispatch and
// the answer each verb gets, the argument refusals, and the trace sections. net8.0 only: the tool is a net8.0 program.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Diagnostics;
using tik4net.mcp;
using tik4net.unittests.Api;

namespace tik4net.unittests.Mcp
{
    [TestClass]
    [DoNotParallelize]   // a fake peer answers within a receive timeout, and the byte trace is process-wide
    public class MikroTikToolsTests
    {
        private const string ServerLine = "--- MCP SERVER ---";

        private static readonly string[] Done = { "!done" };

        // ── the scripted router ───────────────────────────────────────────────

        /// <summary>Serves <paramref name="sessions"/> API sessions one after another: a login, then every command
        /// answered by <paramref name="answer"/>, until the client quits or goes away. The filter check's
        /// /console/inspect is refused, as RouterOS 6 refuses it.</summary>
        private static Task Serve(FakeRouterServer server, int sessions, Func<List<string>, string[][]> answer,
            List<List<string>>? received = null)
            => Task.Run(() =>
            {
                for (int i = 0; i < sessions; i++)
                {
                    server.AcceptClient(10000);
                    server.ReadSentence();                   // login
                    server.WriteSentence("!done");
                    try
                    {
                        while (true)
                        {
                            var words = server.ReadSentence();
                            if (words[0] == "/quit")
                            {
                                server.WriteSentence("!fatal", "=message=session terminated on request");
                                break;
                            }
                            if (words[0] == "/console/inspect")
                            {
                                server.WriteSentence("!trap", "=message=no such command prefix");
                                server.WriteSentence("!done");
                                continue;
                            }
                            lock (received ?? new List<List<string>>())
                                received?.Add(words.Where(w => !w.StartsWith(TikSpecialProperties.Tag + "=", StringComparison.Ordinal)).ToList());
                            foreach (var sentence in answer(words))
                                server.WriteSentence(sentence);
                        }
                    }
                    catch (IOException)
                    {
                        // the client closed the session without /quit
                    }
                }
            });

        private static string Run(object result)
            => result is Task<string> task ? task.GetAwaiter().GetResult() : (string)result;

        /// <summary>One mikrotik_call over the binary API to <paramref name="server"/>, answered by
        /// <paramref name="answer"/>. Returns the answer without its build stamp.</summary>
        private static string Call(string command, string[]? parameters, Func<List<string>, string[][]> answer,
            string executeMode = "auto", string traceLevel = "off", List<List<string>>? received = null)
        {
            using var server = new FakeRouterServer();
            var serverTask = Serve(server, 1, answer, received);
            string result = Run(new MikroTikTools().MikrotikCall(command: command, host: "127.0.0.1", username: "admin",
                password: "", transport: "Api", port: server.Port, parameters: parameters, executeMode: executeMode,
                traceLevel: traceLevel));
            Assert.IsTrue(serverTask.Wait(10000), "the scripted peer did not finish");
            return Body(result);
        }

        private static string Body(string answer)
        {
            int stamp = answer.IndexOf(ServerLine, StringComparison.Ordinal);
            Assert.IsTrue(stamp >= 0, "every answer ends with the build stamp: " + answer);
            return answer.Substring(0, stamp).TrimEnd();
        }

        private static List<Dictionary<string, string>> Records(string body)
            => JsonSerializer.Deserialize<List<Dictionary<string, string>>>(body)!;

        // ── the verb dispatch ─────────────────────────────────────────────────

        [TestMethod]
        public void Add_AnswersTheNewId()
        {
            string body = Call("/ip/address/add", new[] { "=address=192.168.4.250/24", "=interface=ether1" },
                _ => new[] { new[] { "!done", "=ret=*AB" } });

            var records = Records(body);
            Assert.AreEqual(1, records.Count);
            Assert.AreEqual("*AB", records[0]["ret"]);
        }

        [TestMethod]
        public void Get_AnswersTheValueAsARow()
        {
            var received = new List<List<string>>();
            string body = Call("/system/identity/get", new[] { "=value-name=name" },
                _ => new[] { new[] { "!done", "=ret=CHR" } }, received: received);

            Assert.AreEqual("/system/identity/get | =value-name=name", string.Join(" | ", received.Single()));
            Assert.AreEqual("CHR", Records(body).Single()["name"]);
        }

        [TestMethod]
        public void Print_AnswersTheRows()
        {
            string body = Call("/ip/address/print", null,
                _ => new[] { new[] { "!re", "=.id=*1", "=address=192.168.4.1/24" }, new[] { "!re", "=.id=*2", "=address=192.168.4.2/24" }, Done });

            var records = Records(body);
            CollectionAssert.AreEqual(new[] { "*1", "*2" }, records.Select(r => r[".id"]).ToArray());
        }

        [TestMethod]
        public void Print_NoRows_IsNoData()
            => Assert.AreEqual("OK (no data returned)", Call("/ip/address/print", null, _ => new[] { Done }));

        [TestMethod]
        public void Set_IsNoData()
        {
            var received = new List<List<string>>();
            string body = Call("/ip/address/set", new[] { "=.id=*1", "=disabled=yes" }, _ => new[] { Done }, received: received);

            Assert.AreEqual("OK (no data returned)", body);
            Assert.AreEqual("/ip/address/set | =.id=*1 | =disabled=yes", string.Join(" | ", received.Single()));
        }

        [TestMethod]
        public void NonQuery_DiscardsRows()
            => Assert.AreEqual("OK (action executed, no data returned)",
                Call("/system/script/run", new[] { "=.id=*1" }, _ => new[] { new[] { "!re", "=status=x" }, Done }, executeMode: "nonquery"));

        [TestMethod]
        public void Trap_IsAnError()
        {
            string body = Call("/ip/address/remove", new[] { "=.id=*9" },
                _ => new[] { new[] { "!trap", "=message=no such item" }, Done });
            StringAssert.StartsWith(body, "ERROR (");
            StringAssert.Contains(body, "no such item");
        }

        [TestMethod]
        public void ClientCancel_CancelsTheRunningCommand()
        {
            // The print is never answered; the client's cancel reaches the router as /cancel, which ends it.
            var received = new List<List<string>>();
            using var server = new FakeRouterServer();
            var serverTask = Serve(server, 1, words =>
            {
                if (words[0] != "/cancel")
                    return Array.Empty<string[]>();
                string tag = words.Single(w => w.StartsWith("=tag=", StringComparison.Ordinal)).Substring("=tag=".Length);
                return new[]
                {
                    new[] { "!trap", "=category=2", "=message=interrupted", TikSpecialProperties.Tag + "=" + tag },
                    new[] { "!done", TikSpecialProperties.Tag + "=" + tag },
                    Done,
                };
            }, received);

            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            string body = Body(new MikroTikTools().MikrotikCall(command: "/interface/print", host: "127.0.0.1",
                username: "admin", password: "", transport: "Api", port: server.Port, cancellationToken: cancel.Token)
                .GetAwaiter().GetResult());
            watch.Stop();
            Assert.IsTrue(serverTask.Wait(10000), "the scripted peer did not finish");

            Assert.AreEqual("ERROR (cancelled): the client cancelled the call", body);
            Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(10), "ended by the cancel, not a receive timeout: " + watch.Elapsed);
            Assert.IsTrue(received.Any(w => w[0] == "/cancel"), "the router was told");
        }

        // ── argument refusals: no router is reached ───────────────────────────

        [DataTestMethod]
        [DataRow("executeMode", "sometimes")]
        [DataRow("traceLevel", "loud")]
        [DataRow("transport", "Carrier-Pigeon")]
        public void UnknownOption_IsRefused(string option, string value)
        {
            var tools = new MikroTikTools();
            string result = option switch
            {
                "executeMode" => Run(tools.MikrotikCall(command: "/ip/address/print", host: "127.0.0.1", username: "admin", executeMode: value)),
                "traceLevel" => Run(tools.MikrotikCall(command: "/ip/address/print", host: "127.0.0.1", username: "admin", traceLevel: value)),
                _ => Run(tools.MikrotikCall(command: "/ip/address/print", host: "127.0.0.1", username: "admin", transport: value)),
            };
            StringAssert.StartsWith(Body(result), "ERROR (argument):");
        }

        [TestMethod]
        public void ConnectionStringWithHost_IsRefused()
        {
            string result = Run(new MikroTikTools().MikrotikCall(command: "/ip/address/print",
                connectionString: "host=127.0.0.1;user=admin", host: "127.0.0.1"));
            StringAssert.StartsWith(Body(result), "ERROR (argument): connectionString already names the connection");
        }

        [TestMethod]
        public void MissingHost_IsRefused()
            => StringAssert.StartsWith(Body(Run(new MikroTikTools().MikrotikCall(command: "/ip/address/print"))),
                "ERROR (argument): host and username are required");

        // ── trace sections ────────────────────────────────────────────────────

        [TestMethod]
        public void WordTrace_ShowsTheCommandsWords()
        {
            string body = Call("/ip/address/print", null, _ => new[] { new[] { "!re", "=.id=*1" }, Done }, traceLevel: "words");
            StringAssert.Contains(body, "--- RAW TRACE (Api, ");
            StringAssert.Contains(body, ">> /ip/address/print");
            StringAssert.Contains(body, "<< !re");
        }

        [TestMethod]
        public void ByteTrace_ShowsTheApiWordsAndEndsWithTheCall()
        {
            string body = Call("/ip/address/print", null, _ => new[] { Done }, traceLevel: "bytes");
            StringAssert.Contains(body, "--- WIRE TRACE (bytes) (Api, ");
            StringAssert.Contains(body, "api.word");
            Assert.IsFalse(TikWireTrace.Enabled, "the byte trace's sink is removed when the call ends");
        }

        [TestMethod]
        public void ByteTraceWithRouterLog_HoldsOnlyTheCommandsBytes()
        {
            // Three sessions: the log anchor, the command, the log window. Over the binary API the side connection
            // dials the port the command did.
            using var server = new FakeRouterServer();
            var serverTask = Serve(server, 3, words =>
            {
                if (words[0] == "/log/print")
                    return words.Any(w => w.StartsWith("=.proplist=.id,time", StringComparison.Ordinal))
                        ? new[] { new[] { "!re", "=.id=*1", "=message=before" }, new[] { "!re", "=.id=*2", "=time=10:00:00", "=topics=system", "=message=during" }, Done }
                        : new[] { new[] { "!re", "=.id=*1" }, Done };
                return new[] { Done };
            });

            string body = Body(Run(new MikroTikTools().MikrotikCall(command: "/ip/address/print", host: "127.0.0.1",
                username: "admin", password: "", transport: "Api", port: server.Port, traceLevel: "bytes",
                includeRouterLog: true)));
            Assert.IsTrue(serverTask.Wait(10000), "the scripted peer did not finish");

            StringAssert.Contains(body, "--- ROUTER LOG ---");
            StringAssert.Contains(body, "during");
            Assert.IsFalse(body.Contains("before"), "the window starts after the anchor");
            string wire = body.Substring(body.IndexOf("--- WIRE TRACE", StringComparison.Ordinal));
            wire = wire.Substring(0, wire.IndexOf("--- ROUTER LOG ---", StringComparison.Ordinal));
            Assert.IsFalse(wire.Contains("/log/print"), "the side connection's read is not in the command's byte trace:\n" + wire);
        }

        [TestMethod]
        public void SecondByteTrace_WhileOneRuns_IsRefused()
        {
            using var held = new ManualResetEventSlim();
            using var server = new FakeRouterServer();
            var serverTask = Serve(server, 1, _ =>
            {
                held.Wait(TimeSpan.FromSeconds(10));
                return new[] { Done };
            });

            var first = Task.Run(() => Run(new MikroTikTools().MikrotikCall(command: "/ip/address/print", host: "127.0.0.1",
                username: "admin", password: "", transport: "Api", port: server.Port, traceLevel: "bytes")));
            try
            {
                SpinWait.SpinUntil(() => TikWireTrace.Enabled, TimeSpan.FromSeconds(5));

                using var other = new FakeRouterServer();
                var otherTask = Serve(other, 1, _ => new[] { Done });
                string second = Body(Run(new MikroTikTools().MikrotikCall(command: "/ip/address/print", host: "127.0.0.1",
                    username: "admin", password: "", transport: "Api", port: other.Port, traceLevel: "bytes")));
                StringAssert.StartsWith(second, "ERROR (argument): another byte trace is running");
            }
            finally
            {
                held.Set();
            }

            StringAssert.Contains(Body(first.GetAwaiter().GetResult()), "--- WIRE TRACE (bytes)");
            Assert.IsTrue(serverTask.Wait(10000), "the scripted peer did not finish");
            Assert.IsFalse(TikWireTrace.Enabled, "the sink is removed when the trace that holds it ends");
        }
    }
}
