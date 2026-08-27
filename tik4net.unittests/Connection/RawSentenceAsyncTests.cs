// RawSentenceAsyncTests.cs — the low level, awaited.
//
// CallCommandSync was the only level with no awaitable form, which was backwards: it is where the long
// commands live (/export, a script, a monitor), while ITikCommandAsync and (on net8.0) ITikStreamingCommand
// had been awaitable for some time. CallCommandAsync is now a member of ITikRawSentenceConnection, so every
// transport that has a command language has it.
//
// What these check is that it is the SAME command — same text on the wire, same sentences back, same errors
// — rather than a second path that can drift from the synchronous one.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Testing;

namespace tik4net.unittests.Connection
{
    [TestClass]
    public class RawSentenceAsyncTests
    {
        private sealed class RecordingCliConnection : CliConnectionBase
        {
            public readonly List<string> Sent = new List<string>();
            public string Reply = string.Empty;

            protected override string TransportName => "Recording";

            public void OpenScripted()
                => OpenWith(_ => Task.FromResult(0),
                    (cliText, ct) => { Sent.Add(cliText); return Task.FromResult(Reply); },
                    (rawBytes, ct) => Task.FromResult(string.Empty),
                    () => { });

            public override void Open(string host, string user, string password) => OpenScripted();
            public override void Open(string host, int port, string user, string password) => OpenScripted();
            public override Task OpenAsync(string host, string user, string password) { OpenScripted(); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password) { OpenScripted(); return Task.FromResult(0); }
        }

        private static RecordingCliConnection OpenConnection(string reply = "")
        {
            var conn = new RecordingCliConnection { Reply = reply };
            conn.OpenScripted();
            return conn;
        }

        // ── The two forms are one command ─────────────────────────────────────

        [TestMethod]
        public async Task AsyncAndSyncPutTheSameTextOnTheWire()
        {
            // The synchronous call blocks on the asynchronous one — one code path, so they cannot disagree
            // about what they send. Pinning it keeps that true if someone re-splits them.
            using (var conn = OpenConnection(".id=*1;name=ether1"))
            {
                conn.CallCommandSync(":put [/interface print as-value]");
                await conn.CallCommandAsync(new[] { ":put [/interface print as-value]" });

                Assert.AreEqual(2, conn.Sent.Count);
                Assert.AreEqual(conn.Sent[0], conn.Sent[1]);
            }
        }

        [TestMethod]
        public async Task AsyncReadsTheSameSentencesBack()
        {
            using (var conn = OpenConnection(".id=*1;name=ether1;mtu=1500"))
            {
                var sentences = await conn.CallCommandAsync(new[] { ":put [/interface print as-value]" });

                var re = sentences.OfType<ITikReSentence>().ToList();
                Assert.AreEqual(1, re.Count);
                Assert.AreEqual("ether1", re[0].GetResponseField("name"));
                Assert.AreEqual(1, sentences.OfType<ITikDoneSentence>().Count());
            }
        }

        [TestMethod]
        public async Task TheRowsAreJoinedTheSameWayAsSync()
        {
            using (var conn = OpenConnection())
            {
                await conn.CallCommandAsync(new[] { ":put [/interface print", "as-value]" });
                Assert.AreEqual(":put [/interface print as-value]", conn.Sent[0]);
            }
        }

        [TestMethod]
        public async Task TheEnumerableOverloadIsTheSameCall()
        {
            using (var conn = OpenConnection("MikroTik"))
            {
                IEnumerable<string> rows = new List<string> { ":put [/system identity get name]" };
                var sentences = await conn.CallCommandAsync(rows);

                Assert.AreEqual("MikroTik", sentences.OfType<ITikDoneSentence>().Single().GetResponseWord());
            }
        }

        // ── Failures behave the same, awaited ─────────────────────────────────

        [TestMethod]
        public async Task ARouterErrorStillRaisesRatherThanBeingReturned()
        {
            using (var conn = OpenConnection("bad command name prnt (line 1 column 12)"))
            {
                await Assert.ThrowsExceptionAsync<TikNoSuchCommandException>(
                    () => conn.CallCommandAsync(new[] { "/interface prnt" }));
            }
        }

        [TestMethod]
        public async Task AnEmptyCommandIsStillRefused()
        {
            using (var conn = OpenConnection())
            {
                await Assert.ThrowsExceptionAsync<ArgumentException>(
                    () => conn.CallCommandAsync(new[] { "   " }));
            }
        }

        [TestMethod]
        public async Task AClosedConnectionIsStillRefused()
        {
            var conn = new RecordingCliConnection();
            await Assert.ThrowsExceptionAsync<TikConnectionNotOpenException>(
                () => conn.CallCommandAsync(new[] { ":put [/system identity get name]" }));
        }

        // ── Cancellation ──────────────────────────────────────────────────────

        [TestMethod]
        public async Task ATokenCancelledBeforeDispatchWritesNothing()
        {
            // The one guarantee every transport makes, whatever CancelInFlight says: a token already
            // cancelled when the call is made must not put the command on the wire at all.
            using (var conn = OpenConnection())
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                // Caught rather than ThrowsExceptionAsync'd: that assert is exact-type, and the concrete type
                // here is TaskCanceledException. What a caller writes is catch (OperationCanceledException),
                // so that is what this asserts.
                bool cancelled = false;
                try
                {
                    await conn.CallCommandAsync(new[] { ":put [/system identity get name]" }, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }

                Assert.IsTrue(cancelled, "an already-cancelled token must not produce a normal result");
                Assert.AreEqual(0, conn.Sent.Count, "nothing may reach the router once the caller gave up");
            }
        }

        // ── The fake connection offers the shape too ──────────────────────────

        [TestMethod]
        public async Task TheTestingFakeSupportsTheAwaitableForm()
        {
            // tik4net.testing exists so a consumer can test their own code without a router. If the fake did
            // not offer this, `await conn.CallCommandAsync(...)` in consumer code would be untestable — which
            // is the one thing that class must never be true of.
            using (var conn = new TikFakeConnection()
                       .WithScalarResponse(rows => rows.First() == "/system/identity/print", "MikroTik"))
            {
                conn.Open("fake", "user", "pass");

                var sentences = await conn.CallCommandAsync(new[] { "/system/identity/print" });

                Assert.AreEqual("MikroTik",
                    sentences.OfType<ITikDoneSentence>().Single().GetResponseWord());
                conn.AssertWasSent("/system/identity/print");
            }
        }
    }
}
