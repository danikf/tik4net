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
        [TestMethod]
        public void PingLocalhostWillNotFail()
        {
            EnsureCapability(TikConnectionCapability.Streaming, "sync ping");
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
        /// P2.45: over both native WinBox transports this delivered exactly 5 rows at ~4 s and then went
        /// silent for good — no exception, no completion. A <c>type:'query'</c> monitor is ONE getall pass in
        /// which the router blocks each continuation until the next record exists, and the poll had a
        /// 4-second budget: it abandoned the pass mid-stream, lost the continuation cursor, and every
        /// subsequent poll started a fresh getall that the router answered "no more rows". Nothing caught it,
        /// because the only async-ping tests used <c>count=1</c> and <c>count=100</c>-with-close, both of
        /// which are satisfied inside the first pass. Hence the two things asserted here: rows still arriving
        /// AFTER the old cut-off, and no row delivered twice (a restarted pass would repeat sequence 0).
        /// </remarks>
        [TestMethod]
        public void PingAsyncKeepsStreamingPastTheFirstSeconds()
        {
            EnsureCapability(TikConnectionCapability.Listen, "async ping");
            SkipOnNonStreamingAsyncTransport("a streaming async ping");
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

        [TestMethod]
        public void ExecuteListUntilDone_Traceroute_ReturnsResults()
        {
            EnsureCapability(TikConnectionCapability.Streaming, "traceroute");
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
            EnsureCapability(TikConnectionCapability.Streaming, "traceroute");
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
            EnsureCapability(TikConnectionCapability.Streaming, "traceroute");
            const string IP = "127.0.0.1";

            var result = ToolTraceroute.Execute(Connection, IP);
            Assert.IsTrue(result.Count() > 0);
        }

        [TestMethod]
        public void TracerouteToLocalhostWillNotFail_2()
        {
            EnsureCapability(TikConnectionCapability.Streaming, "traceroute");
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
            EnsureCapability(TikConnectionCapability.Streaming, "traceroute");
            const string IP = "8.8.8.8";

            var cmd = Connection.CreateCommandAndParameters("/tool/traceroute", TikCommandParameterFormat.NameValue,
                "address", IP,
                "count", "1");
            var result = cmd.ExecuteList();
            Assert.IsTrue(result.Count() > 1);
        }

        [TestMethod]
        public void TracerouteUnreachableAddressWillNotFail()
        {
            EnsureCapability(TikConnectionCapability.Streaming, "traceroute");
            const string IP = "192.168.4.255";

            var cmd = Connection.CreateCommandAndParameters("/tool/traceroute", TikCommandParameterFormat.NameValue,
                "address", IP,
                "count", "1");
            var result = cmd.ExecuteList();
            Assert.IsTrue(result.Count() > 1); //returns exactly 20 rows ...
        }


        #endregion
    }
}
