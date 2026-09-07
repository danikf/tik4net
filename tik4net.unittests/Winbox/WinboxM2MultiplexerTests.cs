using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    /// <summary>
    /// Loopback tests for <see cref="WinboxM2Multiplexer"/> — the reader-loop dispatch that replaces the
    /// lockstep "read the next frame" model (<c>Docs/winbox-m2-multiplexing-design.md</c>).
    /// <para>
    /// The lockstep tests in <see cref="WinboxM2SessionProtocolTests"/> pin the framing these build on;
    /// what is tested here is the property lockstep could not have: a reply is delivered to <b>its own</b>
    /// caller regardless of the order replies arrive in.
    /// </para>
    /// </summary>
    [TestClass]
    public class WinboxM2MultiplexerTests
    {
        private const string Host = "127.0.0.1";
        private const int TimeoutMs = 5000;

        /// <summary>Runs a logged-in session with the multiplexer owning the read side.</summary>
        private static async Task WithMultiplexer(Action<FakeWinboxServer> serverScript,
            Action<WinboxM2Multiplexer, FakeWinboxServer> clientScript)
        {
            using (var server = new FakeWinboxServer())
            using (var session = new WinboxM2Session())
            {
                var serverTask = Task.Run(() =>
                {
                    server.RunFullLoginSequence();
                    serverScript?.Invoke(server);
                });

                await Task.Run(() =>
                {
                    session.Open(Host, server.Port, "admin", "", TimeoutMs, TimeoutMs);
                    using (var mux = new WinboxM2Multiplexer(session))
                        clientScript(mux, server);
                });

                await serverTask;
            }
        }

        /// <summary><see cref="WithMultiplexer"/> for a client script that awaits.</summary>
        private static async Task WithMultiplexerAsync(Action<FakeWinboxServer> serverScript,
            Func<WinboxM2Multiplexer, FakeWinboxServer, Task> clientScript)
        {
            using (var server = new FakeWinboxServer())
            using (var session = new WinboxM2Session())
            {
                var serverTask = Task.Run(() =>
                {
                    server.RunFullLoginSequence();
                    serverScript?.Invoke(server);
                });

                // The login sequence is lockstep and blocking, so it stays off the test's own thread.
                await Task.Run(() => session.Open(Host, server.Port, "admin", "", TimeoutMs, TimeoutMs));
                using (var mux = new WinboxM2Multiplexer(session))
                    await clientScript(mux, server);

                await serverTask;
            }
        }

        /// <summary>Builds a minimal request carrying <paramref name="reqIdField"/>.</summary>
        private static byte[] Request(byte[] reqIdField)
            => M2Message.BuildM2(M2Message.SysToArr(24, 1), M2Message.SysFrom(), reqIdField);

        /// <summary>Builds a reply echoing <paramref name="reqId"/> and carrying it again as a payload marker.</summary>
        private static byte[] Reply(int reqId)
            => M2Message.BuildM2(
                M2Message.SysToArr(0, 8), M2Message.SysFrom(),
                M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, (byte)reqId),
                M2Message.RawUser(0x0C, new byte[] { (byte)reqId }));

        /// <summary>
        /// The whole point of the change: three requests in flight, answered by the server in reverse order,
        /// and each caller still gets the reply to the request <i>it</i> sent. Under the lockstep model every
        /// caller would have taken whichever frame arrived next.
        /// </summary>
        [TestMethod]
        public async Task ConcurrentRequests_EachCallerGetsItsOwnReply_WhenRepliesArriveOutOfOrder()
        {
            const int n = 3;
            var received = new byte[n][];

            await WithMultiplexer(
                server =>
                {
                    // Collect all requests first, then answer them backwards.
                    var ids = new List<int>();
                    for (int i = 0; i < n; i++)
                        ids.Add(M2Message.ParseSysReqId(server.ReadRawFrame()).Value);

                    foreach (int id in Enumerable.Reverse(ids))
                        server.SendRawFrame(Reply(id));
                },
                (mux, server) =>
                {
                    // Each request must be fully written before the server starts replying to any of them,
                    // so the replies genuinely come back out of order rather than by luck of scheduling.
                    var reqIdFields = Enumerable.Range(0, n).Select(_ => mux.NextReqIdField()).ToArray();
                    var sentIds = reqIdFields.Select(f => M2Message.ParseSysReqId(
                        M2Message.BuildM2(M2Message.SysToArr(24, 1), M2Message.SysFrom(), f)).Value).ToArray();

                    var calls = Enumerable.Range(0, n)
                        .Select(i => Task.Run(() => received[i] = mux.SendReceive(Request(reqIdFields[i]), TimeoutMs)))
                        .ToArray();

                    Assert.IsTrue(Task.WaitAll(calls, TimeoutMs), "All multiplexed calls should complete.");

                    for (int i = 0; i < n; i++)
                        Assert.AreEqual(sentIds[i], M2Message.ParseSysReqId(received[i]),
                            $"Caller {i} received the reply to request {M2Message.ParseSysReqId(received[i])}, not its own ({sentIds[i]}).");
                });
        }

        /// <summary>
        /// A frame with no request id — the shape of a stray async/monitor frame that used to desynchronize
        /// the lockstep reader — must be dropped, not handed to the waiting caller.
        /// </summary>
        [TestMethod]
        public async Task UnmatchedFrame_IsDroppedAndReported_NotDeliveredAsAReply()
        {
            var unmatched = new List<byte[]>();

            await WithMultiplexer(
                server =>
                {
                    int id = M2Message.ParseSysReqId(server.ReadRawFrame()).Value;
                    // A stray frame with no request id arrives first, then the real reply.
                    server.SendRawFrame(M2Message.BuildM2(M2Message.SysToArr(0, 8), M2Message.SysFrom()));
                    server.SendRawFrame(Reply(id));
                },
                (mux, server) =>
                {
                    mux.OnUnmatchedFrame = f => { lock (unmatched) unmatched.Add(f); };

                    byte[] reqIdField = mux.NextReqIdField();
                    byte[] request = Request(reqIdField);
                    byte[] response = mux.SendReceive(request, TimeoutMs);

                    Assert.AreEqual(M2Message.ParseSysReqId(request), M2Message.ParseSysReqId(response),
                        "The caller must get the reply echoing its own id, not the stray frame.");
                    lock (unmatched)
                        Assert.AreEqual(1, unmatched.Count, "The id-less frame should have been reported as unmatched.");
                });
        }

        /// <summary>
        /// A request the server never answers must fail on its own deadline and, critically, must not leave
        /// its registration behind — otherwise the id stays permanently "pending" and is refused for reuse.
        /// </summary>
        [TestMethod]
        public async Task UnansweredRequest_TimesOut_AndReleasesItsRegistration()
        {
            await WithMultiplexer(
                server =>
                {
                    server.ReadRawFrame();          // read it, answer nothing
                    int id = M2Message.ParseSysReqId(server.ReadRawFrame()).Value;
                    server.SendRawFrame(Reply(id)); // the follow-up request is answered normally
                },
                (mux, server) =>
                {
                    Assert.ThrowsException<TikConnectionReceiveTimeoutException>(
                        () => mux.SendReceive(Request(mux.NextReqIdField()), 300),
                        "An unanswered request must surface as a timeout.");

                    // The connection stays usable: a later request completes normally.
                    byte[] request = Request(mux.NextReqIdField());
                    byte[] response = mux.SendReceive(request, TimeoutMs);
                    Assert.AreEqual(M2Message.ParseSysReqId(request), M2Message.ParseSysReqId(response));
                });
        }

        /// <summary>
        /// When the peer disappears, every waiting caller must be failed by the reader loop rather than each
        /// blocking until its own deadline expires.
        /// </summary>
        [TestMethod]
        public async Task ChannelClose_FaultsPendingRequests_RatherThanLettingThemTimeOut()
        {
            using (var server = new FakeWinboxServer())
            using (var session = new WinboxM2Session())
            {
                var serverTask = Task.Run(() =>
                {
                    server.RunFullLoginSequence();
                    server.ReadRawFrame();
                    server.Dispose();          // peer vanishes with the request outstanding
                });

                await Task.Run(() =>
                {
                    session.Open(Host, server.Port, "admin", "", TimeoutMs, TimeoutMs);
                    using (var mux = new WinboxM2Multiplexer(session))
                    {
                        // A generous deadline: if this only fails on the timeout rather than on the close,
                        // the test would still pass on the assertion but take 30 s — so assert on the clock.
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        Assert.ThrowsException<IOException>(
                            () => mux.SendReceive(Request(mux.NextReqIdField()), 30000));
                        sw.Stop();

                        Assert.IsTrue(sw.ElapsedMilliseconds < 10000,
                            $"Pending requests must be faulted when the channel closes, not left to time out (took {sw.ElapsedMilliseconds} ms).");
                    }
                });

                await serverTask;
            }
        }

        /// <summary>
        /// Request ids must never be handed out as 0: <see cref="M2Message.ParseSysReqId"/> reports an
        /// absent id as <c>null</c>, so an actual id of 0 would make "no id" and "reply to request 0"
        /// indistinguishable at the dispatch point.
        /// </summary>
        [TestMethod]
        public async Task NextReqIdField_SkipsZero_AcrossTheByteWraparound()
        {
            await WithMultiplexer(null, (mux, server) =>
            {
                // 300 allocations take the one-byte counter past its wraparound at 256.
                var ids = Enumerable.Range(0, 300)
                    .Select(_ => M2Message.ParseSysReqId(Request(mux.NextReqIdField())).Value)
                    .ToList();

                CollectionAssert.DoesNotContain(ids, 0, "Id 0 is reserved for \"no request id\".");
                Assert.IsTrue(ids.All(id => id >= 1 && id <= 255), "Ids must fit the one-byte wire field.");
            });
        }

        // ── The awaitable surface (P2.8) ──────────────────────────────────────
        //
        // SendReceiveAsync is what carries TikConnectionCapability.AsyncCommands on the native transports, and
        // its cancellation is what CancelInFlight rests on for an ordinary round trip. The claim being tested
        // is narrow and specific: cancelling frees the CALLER while the router keeps working, and the reply
        // that then arrives late is identified by its request id and discarded — never handed to whoever asked
        // next. Asserting only "did it throw OperationCanceledException" would pass on a client that simply
        // stopped reading and left the next caller to receive someone else's answer.

        /// <summary>Level 0: a token that is already cancelled must put nothing on the wire.</summary>
        [TestMethod]
        public async Task PreCancelledToken_WritesNothing()
        {
            await WithMultiplexerAsync(null, async (mux, server) =>
            {
                using (var cts = new CancellationTokenSource())
                {
                    cts.Cancel();
                    await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                        () => mux.SendReceiveAsync(Request(mux.NextReqIdField()), TimeoutMs, cts.Token));
                }

                // Read straight off the socket rather than asking the fake to parse a frame: a byte having
                // arrived at all is the failure, and ReadRawFrame would block for the full timeout to say so.
                Assert.IsFalse(server.HasBufferedData,
                    "A pre-cancelled request must never reach the router.");
            });
        }

        /// <summary>
        /// Cancelling a request in flight frees the caller, and the reply that arrives afterwards is dropped
        /// as unmatched instead of being delivered to the next caller. That second half is the whole safety
        /// argument for declaring CancelInFlight on a protocol with no cancel verb for ordinary round trips.
        /// </summary>
        [TestMethod]
        public async Task CancellingARunningRequest_FreesTheCaller_AndItsLateReplyIsNotGivenToTheNextOne()
        {
            var firstRequestSeen = new ManualResetEventSlim(false);
            var cancelled = new ManualResetEventSlim(false);
            int abandonedId = 0, secondId = 0;

            await WithMultiplexerAsync(
                server =>
                {
                    abandonedId = M2Message.ParseSysReqId(server.ReadRawFrame()).Value;
                    firstRequestSeen.Set();

                    // Answer only once the caller has given up — this is the "the router finished the work
                    // anyway" case that the abandoned registration has to survive.
                    cancelled.Wait(TimeoutMs);
                    server.SendRawFrame(Reply(abandonedId));

                    secondId = M2Message.ParseSysReqId(server.ReadRawFrame()).Value;
                    server.SendRawFrame(Reply(secondId));
                },
                async (mux, server) =>
                {
                    var unmatched = new List<byte[]>();
                    mux.OnUnmatchedFrame = f => { lock (unmatched) unmatched.Add(f); };

                    using (var cts = new CancellationTokenSource())
                    {
                        var running = mux.SendReceiveAsync(Request(mux.NextReqIdField()), TimeoutMs, cts.Token);
                        Assert.IsTrue(firstRequestSeen.Wait(TimeoutMs), "the request never reached the router");

                        cts.Cancel();
                        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => running);
                    }
                    cancelled.Set();

                    // The connection is still good, and the answer it gives is its own.
                    byte[] request = Request(mux.NextReqIdField());
                    byte[] response = await mux.SendReceiveAsync(request, TimeoutMs, CancellationToken.None);

                    Assert.AreEqual(M2Message.ParseSysReqId(request), M2Message.ParseSysReqId(response),
                        "The follow-up caller received the abandoned request's reply — id dispatch is what "
                        + "makes cancelling one request safe for the next.");
                    lock (unmatched)
                        Assert.IsTrue(unmatched.Any(f => M2Message.ParseSysReqId(f) == abandonedId),
                            "The late reply to the cancelled request should have been reported as unmatched.");
                });
        }

        /// <summary>
        /// Two awaited requests in flight at once, answered in reverse: the async path must keep the per-caller
        /// correlation the synchronous one has, not merely compile.
        /// </summary>
        [TestMethod]
        public async Task ConcurrentAsyncRequests_EachAwaiterGetsItsOwnReply()
        {
            await WithMultiplexerAsync(
                server =>
                {
                    int a = M2Message.ParseSysReqId(server.ReadRawFrame()).Value;
                    int b = M2Message.ParseSysReqId(server.ReadRawFrame()).Value;
                    server.SendRawFrame(Reply(b));
                    server.SendRawFrame(Reply(a));
                },
                async (mux, server) =>
                {
                    byte[] first = Request(mux.NextReqIdField());
                    byte[] second = Request(mux.NextReqIdField());

                    var firstCall = mux.SendReceiveAsync(first, TimeoutMs, CancellationToken.None);
                    var secondCall = mux.SendReceiveAsync(second, TimeoutMs, CancellationToken.None);

                    Assert.AreEqual(M2Message.ParseSysReqId(first), M2Message.ParseSysReqId(await firstCall));
                    Assert.AreEqual(M2Message.ParseSysReqId(second), M2Message.ParseSysReqId(await secondCall));
                });
        }

        /// <summary>
        /// The deadline belongs to the request, not to the connection: a caller with a short deadline must not
        /// take a caller with a long one down with it, and the survivor's own reply must still arrive.
        /// </summary>
        [TestMethod]
        public async Task AsyncRequests_EachGetTheirOwnDeadline()
        {
            var impatientGaveUp = new ManualResetEventSlim(false);

            await WithMultiplexerAsync(
                server =>
                {
                    server.ReadRawFrame();                      // the impatient one — never answered
                    int patient = M2Message.ParseSysReqId(server.ReadRawFrame()).Value;
                    impatientGaveUp.Wait(TimeoutMs);
                    server.SendRawFrame(Reply(patient));
                },
                async (mux, server) =>
                {
                    var impatient = mux.SendReceiveAsync(Request(mux.NextReqIdField()), 300, CancellationToken.None);
                    byte[] patientRequest = Request(mux.NextReqIdField());
                    var patient = mux.SendReceiveAsync(patientRequest, TimeoutMs, CancellationToken.None);

                    await Assert.ThrowsExceptionAsync<TikConnectionReceiveTimeoutException>(() => impatient);
                    impatientGaveUp.Set();

                    Assert.AreEqual(M2Message.ParseSysReqId(patientRequest), M2Message.ParseSysReqId(await patient),
                        "The patient caller must still be served after its neighbour's deadline expired.");
                });
        }
        /// <summary>
        /// A13: a timeout has to say enough to tell the two explanations apart. A silent channel and a slow
        /// answer look identical from the waiter's side and want opposite fixes — a dead connection versus
        /// paging or a longer deadline — so the message names what the READER has seen, not only the id it
        /// was waiting on.
        /// </summary>
        [TestMethod]
        public async Task Timeout_SaysWhetherTheChannelWasSilentOrMerelySlow()
        {
            // Nothing has ever arrived on this channel: the message must say so outright.
            await WithMultiplexer(
                server => server.ReadRawFrame(),
                (mux, server) =>
                {
                    var ex = Assert.ThrowsException<TikConnectionReceiveTimeoutException>(
                        () => mux.SendReceive(Request(mux.NextReqIdField()), 300));
                    StringAssert.Contains(ex.Message, "Not one byte has arrived",
                        "a channel that has never answered is a different diagnosis from a slow one");
                    StringAssert.Contains(ex.Message, "no frame at all has arrived");
                });

            // One request answered, the next abandoned: the channel is demonstrably alive, so the message
            // must not blame it.
            await WithMultiplexer(
                server =>
                {
                    int first = M2Message.ParseSysReqId(server.ReadRawFrame()).Value;
                    server.SendRawFrame(Reply(first));
                    server.ReadRawFrame();          // the second one is never answered
                },
                (mux, server) =>
                {
                    mux.SendReceive(Request(mux.NextReqIdField()), TimeoutMs);

                    var ex = Assert.ThrowsException<TikConnectionReceiveTimeoutException>(
                        () => mux.SendReceive(Request(mux.NextReqIdField()), 300));
                    StringAssert.Contains(ex.Message, "Not one byte has arrived",
                        "nothing came back for THIS request, whatever the channel served earlier");
                    StringAssert.Contains(ex.Message, "1 frame(s)",
                        "the frame history is what shows the channel itself is alive");
                });
        }

        /// <summary>
        /// V-7: a reply that is <b>arriving</b> and one that was never sent are the same event to a counter of
        /// completed frames, because a frame is only counted once it is whole. The router has been measured
        /// pausing tens of seconds mid-table (<c>Docs/winbox-native-m2-protocol.md</c> §29), so "bytes are
        /// coming, we are mid-frame" is a diagnosis that has to be reachable — its fix is a longer deadline,
        /// the opposite of the fix for a dead channel.
        /// </summary>
        [TestMethod]
        public async Task Timeout_ReportsBytesArrivingWithoutACompletedFrame()
        {
            await WithMultiplexer(
                server =>
                {
                    server.ReadRawFrame();
                    server.SendFrameFirstChunkOnly();   // real bytes, no frame ever completed
                },
                (mux, server) =>
                {
                    var ex = Assert.ThrowsException<TikConnectionReceiveTimeoutException>(
                        () => mux.SendReceive(Request(mux.NextReqIdField()), 2000));

                    StringAssert.Contains(ex.Message, "without completing a frame",
                        "a half-delivered reply must not be reported as a router that answered nothing");
                    StringAssert.Contains(ex.Message, "byte(s) have arrived");
                    StringAssert.Contains(ex.Message, "no frame at all has arrived",
                        "and the frame count is still zero — which is exactly why it cannot carry this alone");
                });
        }

        // ── Request-id reuse (defect S-1) ─────────────────────────────────────
        //
        // The id is the only thing that identifies a reply, and it is one byte, so a connection doing paged
        // reads wraps through all 255 in seconds. If an id whose caller gave up goes straight back into
        // circulation, the reply the router still owes completes the NEXT request that draws it: right shape,
        // wrong rows, no error. Measured against a live router, a raw reader without id matching read a
        // 1672-row table back as 2391 and 3400 rows and called both a success.

        /// <summary>
        /// An id whose caller gave up while a reply was still owed must not be handed out again — not even
        /// after the counter has wrapped all the way round onto it.
        /// </summary>
        [TestMethod]
        public async Task AbandonedRequestId_IsNotReissued_WhileTheRouterStillOwesItAReply()
        {
            await WithMultiplexer(
                server => server.ReadRawFrame(),        // taken, never answered
                (mux, server) =>
                {
                    byte[] request = Request(mux.NextReqIdField());
                    int abandonedId = M2Message.ParseSysReqId(request).Value;

                    Assert.ThrowsException<TikConnectionReceiveTimeoutException>(
                        () => mux.SendReceive(request, 300));

                    // 300 allocations wrap the one-byte counter right back over the abandoned id.
                    var ids = Enumerable.Range(0, 300)
                        .Select(_ => M2Message.ParseSysReqId(Request(mux.NextReqIdField())).Value)
                        .ToList();

                    CollectionAssert.DoesNotContain(ids, abandonedId,
                        $"id {abandonedId} was handed out again while its reply could still arrive — that "
                        + "reply would then be delivered to whoever drew it, as a successful answer.");
                });
        }

        /// <summary>
        /// The debt is settled by the late reply, not by the clock: once it lands, nothing more can arrive
        /// under that id and it is safe again immediately. A quarantine released only on a timer would be a
        /// slow leak of the 255-id space on a connection that times out occasionally.
        /// </summary>
        [TestMethod]
        public async Task LateReply_ReturnsItsRequestIdToCirculation()
        {
            var gaveUp = new ManualResetEventSlim(false);

            await WithMultiplexer(
                server =>
                {
                    int id = M2Message.ParseSysReqId(server.ReadRawFrame()).Value;
                    gaveUp.Wait(TimeoutMs);
                    server.SendRawFrame(Reply(id));     // the router finished the work anyway
                },
                (mux, server) =>
                {
                    var lateReplySeen = new ManualResetEventSlim(false);
                    mux.OnUnmatchedFrame = _ => lateReplySeen.Set();

                    byte[] request = Request(mux.NextReqIdField());
                    int abandonedId = M2Message.ParseSysReqId(request).Value;

                    Assert.ThrowsException<TikConnectionReceiveTimeoutException>(
                        () => mux.SendReceive(request, 300));
                    gaveUp.Set();
                    Assert.IsTrue(lateReplySeen.Wait(TimeoutMs), "the late reply never arrived");

                    var ids = Enumerable.Range(0, 300)
                        .Select(_ => M2Message.ParseSysReqId(Request(mux.NextReqIdField())).Value)
                        .ToList();

                    CollectionAssert.Contains(ids, abandonedId,
                        "once the late reply has landed the id is owed nothing and must go back into use, "
                        + "rather than waiting out a quarantine that exists for replies that never come.");
                });
        }

        /// <summary>
        /// With every id either awaiting a reply or owed one there is no safe id left, and the only honest
        /// answer is to refuse. Handing one out anyway is the S-1 failure by another route, and it would be
        /// reported as data rather than as an error.
        /// </summary>
        [TestMethod]
        public async Task WhenEveryIdIsOutstanding_AnotherRequestIsRefused_RatherThanReusingOne()
        {
            using (var server = new FakeWinboxServer())
            using (var session = new WinboxM2Session())
            {
                var serverTask = Task.Run(() => server.RunFullLoginSequence());
                await Task.Run(() => session.Open(Host, server.Port, "admin", "", TimeoutMs, TimeoutMs));
                await serverTask;

                var calls = new List<Task<byte[]>>();
                using (var mux = new WinboxM2Multiplexer(session))
                {
                    // 255 requests in flight and unanswered — every usable id is registered.
                    for (int i = 0; i < 255; i++)
                        calls.Add(mux.SendReceiveAsync(Request(mux.NextReqIdField()), 60000, CancellationToken.None));

                    var ex = Assert.ThrowsException<InvalidOperationException>(() => mux.NextReqIdField());
                    StringAssert.Contains(ex.Message, "reconnect",
                        "the caller has to be told the connection is spent, not handed a colliding id");
                }

                // Disposing the multiplexer failed every waiter; observe them all so none is left unobserved.
                foreach (var call in calls)
                    await Assert.ThrowsExceptionAsync<ObjectDisposedException>(() => call);
            }
        }
    }
}