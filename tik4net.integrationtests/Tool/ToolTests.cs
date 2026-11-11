using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Objects;
using tik4net.Objects.Tool;

namespace tik4net.integrationtests
{
    [TestClass]
    [SafeInParallelLegs]
    public class ToolTests : TestBase
    {
        #region ping
        /// <summary>
        /// A synchronous ping is a plain <c>LoadList&lt;ToolPing&gt;</c> and must work on every transport.
        /// </summary>
        /// <remarks>
        /// This used to gate on <c>Streaming</c>, which only the binary API reports — so it was Inconclusive on
        /// ten of eleven transports, and the whole synchronous monitor path had no coverage at all. What that
        /// hid: the CLI family sent <c>:put [/ping as-value]</c> with every parameter dropped, REST
        /// posted <c>/ping/print</c> ("no such command"), and native WinBox ran a getall on a monitor window
        /// and returned zero rows while reporting success. <c>Ping</c> never needed
        /// <c>Streaming</c>: it does not stream, it asks for a bounded count and waits.
        /// </remarks>
        [TestMethod]
        public void PingLocalhostWillNotFail()
        {
            const string HOST = "127.0.0.1";
            var result = Connection.Ping(HOST, 4);
            Assert.IsTrue(result.Count() == 4);
        }

        /// <summary>
        /// A loopback ping answers in microseconds, which the API writes <c>124us</c> (7.25). Each row's time must read
        /// as a duration, not as a word kept verbatim, and the round trips must parse too, on every transport: native
        /// read the wire's microseconds as milliseconds (F12), and <see cref="TikDuration"/> had no <c>us</c>.
        /// </summary>
        [TestMethod]
        public async Task PingAsyncLocalhostReturnsEveryReply()
        {
            EnsureCapability(TikConnectionCapability.AsyncCommands, "PingAsync");
            var rows = await Connection.PingAsync("127.0.0.1", 2);
            Assert.AreEqual(2, rows.Count);
            Assert.IsTrue(rows.All(r => r.Time.TryGetValue(out var t) && t.HasValue), "a loopback reply without its time");
        }

        [TestMethod]
        public void PingLocalhostTimesAreDurations()
        {
            var rows = Connection.Ping("127.0.0.1", 2).ToList();
            Assert.AreEqual(2, rows.Count);
            foreach (var row in rows)
            {
                Assert.IsTrue(row.Time.TryGetValue(out var time) && time.HasValue, "no time on " + row);
                Assert.AreEqual(TikValueKind.Value, time.Value.Kind, "time kept as a word: " + time.Value.Token);
                Assert.IsTrue(time.Value.Value > TimeSpan.Zero && time.Value.Value < TimeSpan.FromSeconds(1),
                    "a loopback round trip of " + time.Value);
                // The CLI's `ping … as-value` prints no round trips (nor sent/received): not this test's subject.
                foreach (var rtt in new[] { row.MinRtt, row.AvgRtt, row.MaxRtt }.Where(r => r.State != TikFieldState.Absent))
                    Assert.IsTrue(TikDuration.TryParseTimeSpan(rtt.GetValueOrDefault(), out var span)
                                  && span > TimeSpan.Zero && span < TimeSpan.FromSeconds(1),
                        "a round trip of '" + rtt.GetValueOrDefault() + "'");
            }
        }

        [TestMethod]
        public void PingLocalhostAsyncWillNotFail()
        {
            EnsureCapability(TikConnectionCapability.Listen, "async ping");
            const string HOST = "127.0.0.1";

            List<ToolPing> responseList = new List<ToolPing>();
            Exception responseException = null;

            ITikCommand pingCommand = Connection.LoadWithCallback<ToolPing>(
                ping => responseList.Add(ping), //read callback
                exception => responseException = exception, //exception callback
                Connection.CreateParameter("address", HOST),
                Connection.CreateParameter("count", "1"),
                Connection.CreateParameter("size", "64"));

            WaitUntil(() => responseList.Count > 0 || responseException != null, TimeSpan.FromSeconds(2));

            // Carry the exception itself: a bare "Assert.IsNull failed" once left a MAC-Telnet failure with
            // nothing to diagnose it by.
            Assert.IsNull(responseException, responseException?.ToString());
            Assert.AreEqual(responseList.Count, 1);
            Assert.AreEqual(responseList.Single().Host, HOST);
        }

        /// <summary>
        /// A callback torch given <c>duration=</c>: an instruction to the client, not an input of the WinBox torch window
        /// (WinBox + MAC review, §5 X4). The read method dropped it; the callback path sent it to the resolver, which
        /// failed the whole monitor with "cannot resolve API field 'duration'". (Torch, not ping: the API refuses
        /// <c>duration</c> on <c>/ping</c> as an unknown parameter.)
        /// </summary>
        [TestMethod]
        public void TorchAsyncWithDurationWillNotFail()
        {
            EnsureCapability(TikConnectionCapability.Listen, "async torch");
            var rows = new List<ITikReSentence>();
            ITikTrapSentence trap = null;
            var cmd = Connection.CreateCommandAndParameters("/tool/torch",
                "interface", TestConstants.Interface, "duration", "3");
            cmd.ExecuteWithCallback(row => { lock (rows) rows.Add(row); }, error => trap = error);
            try
            {
                WaitUntil(() => { lock (rows) return rows.Count > 0 || trap != null; }, TimeSpan.FromSeconds(5));
                Assert.IsNull(trap, trap?.Message);
                lock (rows) Assert.IsTrue(rows.Count > 0, "torch reported nothing on " + TestConstants.Interface);
            }
            finally
            {
                try { cmd.Cancel(); } catch { /* best-effort */ }
            }
        }

        /// <summary>
        /// An async ping must keep delivering for as long as the router keeps pinging — not stop after the
        /// first few seconds.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Over both native WinBox transports this delivered exactly 5 rows at ~4 s and then went
        /// silent for good — no exception, no completion. A <c>type:'query'</c> monitor is ONE getall pass in
        /// which the router blocks each continuation until the next record exists, and the poll had a
        /// 4-second budget: it abandoned the pass mid-stream, lost the continuation cursor, and every
        /// subsequent poll started a fresh getall that the router answered "no more rows".
        /// </para>
        /// <para>
        /// The same assertion then failed on all five CLI transports, for an unrelated reason — the
        /// monitor was issued as <c>:put [/ping … as-value]</c>, which hands <c>:put</c> a completed array,
        /// so RouterOS printed nothing until the whole ping had finished (0 rows for 20 s, then all 20).
        /// They now send the bare interactive form, which the router streams a row at a time.
        /// </para>
        /// <para>
        /// Nothing caught either defect, because the only async-ping tests used <c>count=1</c> (satisfied
        /// inside the first pass / the first second) and <c>count=100</c>-with-close, which asserts merely
        /// that FEWER than 100 rows arrived — and zero satisfies that. Hence the two things asserted here:
        /// rows still arriving AFTER the old cut-off, and no row delivered twice (a restarted pass would
        /// repeat sequence 0).
        /// </para>
        /// </remarks>
        [TestMethod]
        public void PingAsyncKeepsStreamingPastTheFirstSeconds()
        {
            EnsureCapability(TikConnectionCapability.Listen, "async ping");
            const string HOST = "127.0.0.1";
            const int COUNT = 12;      // must outlast the observation window, so a short ping cannot pass by ending
            // The defect stopped the stream at 5 rows, ~4 s in. Seven rows are past that cut-off; the old
            // fixed windows (5 s + 5 s) are the budget, so a stream that stops still fails the same assertions.
            const int PastTheCutOff = 7;

            var responseList = new List<ToolPing>();
            Exception responseException = null;

            ITikCommand pingCommand = Connection.LoadWithCallback<ToolPing>(
                ping => { lock (responseList) responseList.Add(ping); },
                exception => responseException = exception,
                Connection.CreateParameter("address", HOST),
                Connection.CreateParameter("count", COUNT.ToString()));

            try
            {
                WaitUntil(() => { lock (responseList) return responseList.Count >= 5; }, TimeSpan.FromSeconds(5));
                int afterFive;
                lock (responseList) afterFive = responseList.Count;

                WaitUntil(() => { lock (responseList) return responseList.Count >= Math.Max(afterFive + 1, PastTheCutOff); },
                          TimeSpan.FromSeconds(5));
                int afterTen;
                List<ToolPing> snapshot;
                lock (responseList) { afterTen = responseList.Count; snapshot = responseList.ToList(); }

                Assert.IsNull(responseException, "the monitor reported an error");

                if (DeliversMonitorRowsLive())
                {
                    // The defect's signature: the stream stops dead. RouterOS pings once a second, so the second
                    // window must add rows — the exact count is timing-dependent and deliberately not asserted.
                    Assert.IsTrue(afterTen > afterFive,
                        $"the ping stream stopped: {afterFive} rows after 5 s, still {afterTen} after 10 s");
                    Assert.IsTrue(afterTen > 5,
                        $"expected the stream to outlive the first few seconds, got only {afterTen} rows in 10 s");
                }
                else
                {
                    // REST delivers the whole ping at once when it ends, so "did it stop dead?" cannot be asked
                    // of the first ten seconds — but the property the test exists for still can: every reply
                    // must arrive. Waiting past the ping's own duration is the REST spelling of the same check.
                    WaitUntil(() => { lock (responseList) return responseList.Count >= COUNT; }, TimeSpan.FromSeconds(COUNT + 5));
                    lock (responseList) snapshot = responseList.ToList();
                    Assert.AreEqual(COUNT, snapshot.Count,
                        $"the ping delivered {snapshot.Count} of {COUNT} replies (see TestBase.DeliversMonitorRowsLive)");
                }

                Assert.AreEqual(snapshot.Count, snapshot.Select(p => p.SequenceNo).Distinct().Count(),
                    "a sequence number arrived twice — the monitor restarted its pass instead of continuing it");
                Assert.IsTrue(snapshot.All(p => p.Host == HOST), "a row came back for the wrong host");
            }
            finally
            {
                try { pingCommand.Cancel(); } catch { /* best-effort: the assert above is the result */ }
            }
        }

        /// <summary>
        /// <c>CancelAndJoin</c> stops a streaming ping at once, and frees the connection for a command queued behind it.
        /// </summary>
        /// <remarks>
        /// The CLI family streams a finite monitor (ping, traceroute) through one read that holds the console, and
        /// <c>Cancel</c> used to stop only the delivery of rows: the router pinged on to its <c>count</c>, and a command on
        /// another thread waited for all of it (measured on Telnet, CHR3 7.21.5: <c>CancelAndJoin</c> 11.5 s, the queued
        /// command 12.2 s, against 3 ms on the API). Now the cancel interrupts the read with Ctrl-C. REST is not asked: the
        /// router buffers a REST monitor's whole answer, and an abandoned REST command blocks REST for about a minute.
        /// </remarks>
        [TestMethod]
        [TestLock(TestLockScope.Router)]   // timings
        public void PingCancelAndJoinStopsAtOnceAndReleasesTheConnection()
        {
            EnsureCapability(TikConnectionCapability.Listen, "async ping");
            if (!DeliversMonitorRowsLive())
                Assert.Inconclusive("REST buffers a monitor's whole answer, so there is nothing to cancel mid-stream");
            const int COUNT = 15;

            int rows = 0;
            Exception error = null;
            var done = new ManualResetEventSlim(false);
            ITikCommand ping = Connection.CreateCommand("/ping",
                Connection.CreateParameter("address", "127.0.0.1"),
                Connection.CreateParameter("count", COUNT.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            ping.ExecuteWithCallback(_ => Interlocked.Increment(ref rows),
                trap => error = new Exception(trap.Message), () => done.Set());

            long crudMs = -1;
            Thread crud = null;
            try
            {
                WaitUntil(() => Volatile.Read(ref rows) >= 2, TimeSpan.FromSeconds(8));
                Assert.IsTrue(Volatile.Read(ref rows) >= 2, "the ping delivered no rows to cancel in the middle of");

                // A command on another thread, queued behind the running ping on a console transport.
                crud = new Thread(() =>
                {
                    var t = System.Diagnostics.Stopwatch.StartNew();
                    Connection.CreateCommand("/system/identity/print").ExecuteScalar("name");
                    crudMs = t.ElapsedMilliseconds;
                }) { IsBackground = true };
                crud.Start();
                Thread.Sleep(200);

                var sw = System.Diagnostics.Stopwatch.StartNew();
                ping.CancelAndJoin();
                long cancelMs = sw.ElapsedMilliseconds;
                Assert.IsTrue(crud.Join(TimeSpan.FromSeconds(COUNT + 10)), "the queued command never finished");
                int delivered = Volatile.Read(ref rows);
                Console.WriteLine($"CancelAndJoin {cancelMs} ms; queued command {crudMs} ms; rows {delivered} of {COUNT}");

                Assert.IsTrue(cancelMs < 1000, $"CancelAndJoin took {cancelMs} ms: the ping ran on after the cancel");
                Assert.IsTrue(crudMs < 1200 + 1000,
                    $"the queued command waited {crudMs} ms: the cancelled ping still held the connection");
                Assert.IsTrue(delivered < COUNT, $"the ping ran to its count ({delivered} rows)");
                Assert.IsTrue(done.Wait(2000), "a cancelled monitor ends in onDone");
                Assert.IsNull(error, error?.Message);
                Assert.IsFalse(ping.IsRunning);
            }
            finally
            {
                try { ping.Cancel(); } catch { }
                crud?.Join(TimeSpan.FromSeconds(COUNT + 10));
            }
        }

        [TestMethod]
        public void PingLocalhostAsyncWithCloseWillNotFail()
        {
            EnsureCapability(TikConnectionCapability.Listen, "async ping");
            const string HOST = "127.0.0.1";
            // Long enough that the close lands mid-ping, which is the point of the test. On REST it must also
            // be SHORT enough to finish soon after: closing the connection does not stop the router's command,
            // and until it ends the router's REST session refuses everything else — including the reconnect
            // below (measured: a count=100 ping left REST unusable for the rest of its 100 s).
            int MAX_CNT = DeliversMonitorRowsLive() ? 100 : 10;

            List<ToolPing> responseList = new List<ToolPing>();
            Exception responseException = null;

            ITikCommand pingCommand = Connection.LoadWithCallback<ToolPing>(
                ping => responseList.Add(ping), //read callback
                exception => responseException = exception, //exception callback
                Connection.CreateParameter("address", HOST),
                Connection.CreateParameter("count", MAX_CNT.ToString()),
                Connection.CreateParameter("size", "64"));

            // Close mid-ping: once it answers (the old fixed 2 s is the budget).
            WaitUntil(() => responseList.Count > 0 || responseException != null, TimeSpan.FromSeconds(2));
            Connection.Close();
            Thread.Sleep(1500);

            Assert.IsTrue(!Connection.IsOpened);
            Assert.IsNull(responseException, responseException?.ToString());
            Assert.IsTrue(responseList.Count < MAX_CNT);
            Assert.IsTrue(!responseList.Any(ping => ping.Host != HOST));

            RecreateConnection(); //Cleanup
        }
        #endregion


        #region --- WOL ---
        [TestMethod]
        public void WolWillNotFail()
        {
            // ToolWol.ExecuteWol issues a plain ExecuteSingleRow() — no streaming involved — so it
            // works on every transport (Crud is the baseline capability); no EnsureCapability gate needed.
            const string OK_MAC = "AA:BB:CC:DD:EE:FF";

            ToolWol.ExecuteWol(Connection, new MacAddress(OK_MAC));
        }

        [TestMethod]
        public void WolWithOkIfaceWillNotFail()
        {
            const string OK_MAC = "FF:FF:FF:FF:FF:FF";
            string OK_IFACE = TestConstants.Interface;

            ToolWol.ExecuteWol(Connection, new MacAddress(OK_MAC), OK_IFACE);
        }

        [TestMethod]
        [ExpectedException(typeof(TikCommandTrapException), "input does not match any value of interface")]
        public void WolWithInvalidInterfaceWillFail()
        {
            const string OK_MAC = "FF:FF:FF:FF:FF:FF";
            const string BAD_IFACE = "kjdshfkjdhfkjdaskjfhs";

            ToolWol.ExecuteWol(Connection, new MacAddress(OK_MAC), BAD_IFACE);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void WolWithBadMacWillFail()
        {
            const string BAD_MAC = "00:00";
            ToolWol.ExecuteWol(Connection, BAD_MAC);
        }

        #endregion

        #region ExecuteListUntilDone

        /// <remarks>
        /// No <c>Streaming</c> gate: on a transport without it <c>ExecuteListUntilDone</c> falls back to
        /// <c>ExecuteList()</c>, which is exactly what this asserts. See <see cref="PingLocalhostWillNotFail"/>
        /// for what the gate was hiding.
        /// </remarks>
        [TestMethod]
        public void ExecuteListUntilDone_Traceroute_ReturnsResults()
        {
            const string IP = "127.0.0.1";

            var cmd = Connection.CreateCommandAndParameters("/tool/traceroute", TikCommandParameterFormat.NameValue,
                "address", IP,
                "count", "1");
            var result = cmd.ExecuteListUntilDone();

            Assert.IsTrue(result.Any());
        }

        [TestMethod]
        public void ExecuteListUntilDone_Traceroute_WithTimeout_ReturnsResults()
        {
            const string IP = "127.0.0.1";

            var cmd = Connection.CreateCommandAndParameters("/tool/traceroute", TikCommandParameterFormat.NameValue,
                "address", IP,
                "count", "1");
            var result = cmd.ExecuteListUntilDone(timeout: TimeSpan.FromSeconds(30));

            Assert.IsTrue(result.Any());
        }

        [TestMethod]
        [ExpectedException(typeof(TikCommandAbortException))]
        public void ExecuteListUntilDone_NeverEndingCommand_ThrowsAbortException()
        {
            // The API, WinBox native and the CLI family read torch until the deadline. REST has the test below.
            var type = ResolveConnectionType();
            if (type == TikConnectionType.Rest || type == TikConnectionType.RestSsl)
                Assert.Inconclusive($"{type}: covered by ExecuteListUntilDone_NeverEndingCommand_OverRest_AbortsAndTheRouterRecovers.");
            // /tool/torch never sends !done on its own — timeout must kick in
            var cmd = Connection.CreateCommandAndParameters("/tool/torch", TikCommandParameterFormat.NameValue,
                "interface", TestConstants.Interface);
            cmd.ExecuteListUntilDone(timeout: TimeSpan.FromSeconds(2));
        }

        /// <summary>
        /// Under Cooperative a cancelled read is interrupted with Ctrl-C and read on to its prompt, so it returns at once and
        /// the terminal stays in step: the next command gets its own answer.
        /// </summary>
        [TestMethod]
        public void ADeadlineInterruptsACliRead_AndTheNextCommandReadsItsOwnAnswer()
        {
            if (!(Connection is tik4net.Cli.CliConnectionBase))
                Assert.Inconclusive("Ctrl-C is about a terminal.");

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var cmd = Connection.CreateCommandAndParameters("/ping", TikCommandParameterFormat.NameValue, "address", "127.0.0.1", "count", "20");
            Assert.ThrowsException<TikCommandAbortException>(() => cmd.ExecuteListUntilDone(timeout: TimeSpan.FromSeconds(2)));
            watch.Stop();
            Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(10), "the 20 s ping was cut short: " + watch.Elapsed);

            string identity = Connection.CreateCommand("/system/identity/print").ExecuteScalar("name");
            Assert.IsFalse(string.IsNullOrEmpty(identity));
        }

        /// <summary>
        /// The deadline ends the HTTP read, but the router's REST session stays busy with the abandoned command and answers
        /// nothing, new connections included, for about a minute (Docs/findings-rest-api.md §12). So this test holds the
        /// router and waits that out; left to itself, the abandoned torch fails the next test's connect.
        /// </summary>
        [TestMethod]
        [TestLock(TestLockScope.Router)]
        public void ExecuteListUntilDone_NeverEndingCommand_OverRest_AbortsAndTheRouterRecovers()
        {
            var type = ResolveConnectionType();
            if (type != TikConnectionType.Rest && type != TikConnectionType.RestSsl)
                Assert.Inconclusive($"{type}: this is about REST's session.");

            var cmd = Connection.CreateCommandAndParameters("/tool/torch", TikCommandParameterFormat.NameValue,
                "interface", TestConstants.Interface);
            Assert.ThrowsException<TikCommandAbortException>(() => cmd.ExecuteListUntilDone(timeout: TimeSpan.FromSeconds(2)));

            Exception last = null;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(120))
            {
                try
                {
                    Connection.CreateCommand("/system/identity/print").ExecuteList();
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    Thread.Sleep(1000);
                }
            }
            Assert.Fail("The router's REST did not answer within 120 s of the abandoned torch: " + last);
        }

        #endregion

        #region ExecuteListWithDuration — early !done

        [TestMethod]
        public void ExecuteListWithDuration_EarlyDone_DoesNotSetWasAborted()
        {
            EnsureCapability(TikConnectionCapability.Streaming, "ExecuteListWithDuration");
            // Before the fix this set wasAborted=true when !done arrived before the duration elapsed.
            const string IP = "127.0.0.1";

            var cmd = Connection.CreateCommandAndParameters("/tool/traceroute", TikCommandParameterFormat.NameValue,
                "address", IP,
                "count", "1");

            bool wasAborted;
            string abortReason;
            var result = cmd.ExecuteListWithDuration(TimeSpan.FromSeconds(30), out wasAborted, out abortReason);

            Assert.IsFalse(wasAborted, "wasAborted must be false when command finishes naturally via !done");
            Assert.IsNull(abortReason);
            Assert.IsTrue(result.Any());
        }

        [TestMethod]
        public void ExecuteListWithDuration_EarlyDone_DoesNotThrow()
        {
            EnsureCapability(TikConnectionCapability.Streaming, "ExecuteListWithDuration");
            // Before the fix this threw TikCommandAbortException when !done arrived before the duration elapsed.
            const string IP = "127.0.0.1";

            var cmd = Connection.CreateCommandAndParameters("/tool/traceroute", TikCommandParameterFormat.NameValue,
                "address", IP,
                "count", "1");
            var result = cmd.ExecuteListWithDuration(TimeSpan.FromSeconds(30));

            Assert.IsTrue(result.Any());
        }

        #endregion

        #region /tool/traceroute

        [TestMethod]
        public void TracerouteToLocalhostWillNotFail()
        {
            const string IP = "127.0.0.1";

            var result = Connection.Traceroute(IP);
            Assert.IsTrue(result.Count() > 0);
            // The hop ADDRESS, not just the row count: over WinBox native the hop rode in a nested field the
            // M2 parser could not read, so the rows arrived complete but with a blank address.
            Assert.AreEqual(IP, result.First().Address, "a hop must name the host that answered");
        }

        [TestMethod]
        public async Task TracerouteAsyncToLocalhostNamesTheHop()
        {
            EnsureCapability(TikConnectionCapability.AsyncCommands, "TracerouteAsync");
            const string IP = "127.0.0.1";

            var result = await Connection.TracerouteAsync(IP);
            Assert.IsTrue(result.Count > 0);
            Assert.AreEqual(IP, result[0].Address, "a hop must name the host that answered");
        }

        [TestMethod]
        public void TracerouteToLocalhostWillNotFail_2()
        {
            const string IP = "127.0.0.1";

            var cmd = Connection.CreateCommandAndParameters("/tool/traceroute", TikCommandParameterFormat.NameValue,
                "address", IP,
                "count", "1");
            var result = cmd.ExecuteList();
            Assert.IsTrue(result.Count() > 0);
        }


        [TestMethod]
        public void TracerouteToGoogleDnsWillNotFail()
        {
            const string IP = "8.8.8.8";

            var cmd = Connection.CreateCommandAndParameters("/tool/traceroute", TikCommandParameterFormat.NameValue,
                "address", IP,
                "count", "1");
            var result = cmd.ExecuteList();
            Assert.IsTrue(result.Count() > 1);
        }

        /// <summary>
        /// An address nothing answers for still yields hop rows rather than an error.
        /// </summary>
        /// <remarks>
        /// <c>max-hops</c> is what keeps this test finite. Unbounded, RouterOS walks its full hop limit at one
        /// second of timeout per hop: measured on 7.23.2, the binary API took <b>30.2 s</b> to return, and over
        /// a terminal that is past the 30 s default <c>ReceiveTimeout</c> — which a CLI transport fixes at
        /// Open, so a test cannot raise it afterwards. Bounding the walk keeps what the test is actually
        /// about (an unreachable target answers with rows, not a trap) and costs ~5 s on every transport.
        /// It only ever ran on the binary API before the sync monitors were fixed, which is why the duration never
        /// showed up.
        /// </remarks>
        [TestMethod]
        public void TracerouteUnreachableAddressWillNotFail()
        {
            const string IP = "192.168.4.255";

            var cmd = Connection.CreateCommandAndParameters("/tool/traceroute", TikCommandParameterFormat.NameValue,
                "address", IP,
                "count", "1",
                "max-hops", "5");
            var result = cmd.ExecuteList();
            Assert.IsTrue(result.Count() > 1);
        }


        #endregion
    }
}
