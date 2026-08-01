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
    public class ToolTests : TestBase
    {
        #region ping
        /// <summary>
        /// A synchronous ping is a plain <c>LoadList&lt;ToolPing&gt;</c> and must work on every transport.
        /// </summary>
        /// <remarks>
        /// This used to gate on <c>Streaming</c>, which only the binary API reports — so it was Inconclusive on
        /// ten of eleven transports, and the whole synchronous monitor path had no coverage at all. What that
        /// hid (P2.51): the CLI family sent <c>:put [/ping as-value]</c> with every parameter dropped, REST
        /// posted <c>/ping/print</c> ("no such command"), and native WinBox ran a getall on a monitor window
        /// and returned zero rows while reporting success. <c>ToolPing.Execute</c> never needed
        /// <c>Streaming</c>: it does not stream, it asks for a bounded count and waits.
        /// </remarks>
        [TestMethod]
        public void PingLocalhostWillNotFail()
        {
            const string HOST = "127.0.0.1";
            var result = ToolPing.Execute(Connection, HOST, 4);
            Assert.IsTrue(result.Count() == 4);
        }

        [TestMethod]
        public void PingLocalhostAsyncWillNotFail()
        {
            EnsureCapability(TikConnectionCapability.Listen, "async ping");
            const string HOST = "127.0.0.1";

            List<ToolPing> responseList = new List<ToolPing>();
            Exception responseException = null;

            ITikCommand pingCommand = Connection.LoadAsync<ToolPing>(
                ping => responseList.Add(ping), //read callback
                exception => responseException = exception, //exception callback
                Connection.CreateParameter("address", HOST),
                Connection.CreateParameter("count", "1"),
                Connection.CreateParameter("size", "64"));

            Thread.Sleep(2 * 1000);

            Assert.IsNull(responseException);
            Assert.AreEqual(responseList.Count, 1);
            Assert.AreEqual(responseList.Single().Host, HOST);
        }

        /// <summary>
        /// An async ping must keep delivering for as long as the router keeps pinging — not stop after the
        /// first few seconds.
        /// </summary>
        /// <remarks>
        /// <para>
        /// P2.45: over both native WinBox transports this delivered exactly 5 rows at ~4 s and then went
        /// silent for good — no exception, no completion. A <c>type:'query'</c> monitor is ONE getall pass in
        /// which the router blocks each continuation until the next record exists, and the poll had a
        /// 4-second budget: it abandoned the pass mid-stream, lost the continuation cursor, and every
        /// subsequent poll started a fresh getall that the router answered "no more rows".
        /// </para>
        /// <para>
        /// P2.50: the same assertion then failed on all five CLI transports, for an unrelated reason — the
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
            const int COUNT = 20;      // must outlast the observation window, so a short ping cannot pass by ending

            var responseList = new List<ToolPing>();
            Exception responseException = null;

            ITikCommand pingCommand = Connection.LoadAsync<ToolPing>(
                ping => { lock (responseList) responseList.Add(ping); },
                exception => responseException = exception,
                Connection.CreateParameter("address", HOST),
                Connection.CreateParameter("count", COUNT.ToString()));

            try
            {
                Thread.Sleep(5 * 1000);
                int afterFive;
                lock (responseList) afterFive = responseList.Count;

                Thread.Sleep(5 * 1000);
                int afterTen;
                List<ToolPing> snapshot;
                lock (responseList) { afterTen = responseList.Count; snapshot = responseList.ToList(); }

                Assert.IsNull(responseException, "the monitor reported an error");
                // The defect's signature: the stream stops dead. RouterOS pings once a second, so the second
                // window must add rows — the exact count is timing-dependent and deliberately not asserted.
                Assert.IsTrue(afterTen > afterFive,
                    $"the ping stream stopped: {afterFive} rows after 5 s, still {afterTen} after 10 s");
                Assert.IsTrue(afterTen > 5,
                    $"expected the stream to outlive the first few seconds, got only {afterTen} rows in 10 s");
                Assert.AreEqual(snapshot.Count, snapshot.Select(p => p.SequenceNo).Distinct().Count(),
                    "a sequence number arrived twice — the monitor restarted its pass instead of continuing it");
                Assert.IsTrue(snapshot.All(p => p.Host == HOST), "a row came back for the wrong host");
            }
            finally
            {
                try { pingCommand.Cancel(); } catch { /* best-effort: the assert above is the result */ }
            }
        }

        [TestMethod]
        public void PingLocalhostAsyncWithCloseWillNotFail()
        {
            EnsureCapability(TikConnectionCapability.Listen, "async ping");
            const string HOST = "127.0.0.1";
            const int MAX_CNT = 100;

            List<ToolPing> responseList = new List<ToolPing>();
            Exception responseException = null;

            ITikCommand pingCommand = Connection.LoadAsync<ToolPing>(
                ping => responseList.Add(ping), //read callback
                exception => responseException = exception, //exception callback
                Connection.CreateParameter("address", HOST),
                Connection.CreateParameter("count", MAX_CNT.ToString()),
                Connection.CreateParameter("size", "64"));

            Thread.Sleep(2 * 1000);
            Connection.Close();
            Thread.Sleep(1500);

            Assert.IsTrue(!Connection.IsOpened);
            Assert.IsNull(responseException);
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
            //const string OK_MAC = "FF:FF:FF:FF:FF:FF"; //
            const string OK_MAC = "00:11:32:71:AD:AD";

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
        /// for what the gate was hiding (P2.51).
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
            var result = cmd.ExecuteListUntilDone(timeoutSec: 30);

            Assert.IsTrue(result.Any());
        }

        [TestMethod]
        [ExpectedException(typeof(TikCommandAbortException))]
        public void ExecuteListUntilDone_NeverEndingCommand_ThrowsAbortException()
        {
            EnsureCapability(TikConnectionCapability.Streaming, "torch streaming");
            // /tool/torch never sends !done on its own — timeout must kick in
            var cmd = Connection.CreateCommandAndParameters("/tool/torch", TikCommandParameterFormat.NameValue,
                "interface", TestConstants.Interface);
            cmd.ExecuteListUntilDone(timeoutSec: 2);
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
            var result = cmd.ExecuteListWithDuration(30, out wasAborted, out abortReason);

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
            var result = cmd.ExecuteListWithDuration(30);

            Assert.IsTrue(result.Any());
        }

        #endregion

        #region /tool/traceroute

        [TestMethod]
        public void TracerouteToLocalhostWillNotFail()
        {
            const string IP = "127.0.0.1";

            var result = ToolTraceroute.Execute(Connection, IP);
            Assert.IsTrue(result.Count() > 0);
            // The hop ADDRESS, not just the row count: over WinBox native the hop rode in a nested field the
            // M2 parser could not read, so the rows arrived complete but with a blank address (P2.52).
            Assert.AreEqual(IP, result.First().Address, "a hop must name the host that answered");
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
        /// It only ever ran on the binary API before P2.51, which is why the duration never showed up.
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
