using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface;
using System.Collections.Generic;
using System.Threading;
using tik4net.Objects.Interface.Wireless;
using System.Linq;

namespace tik4net.integrationtests
{
    [TestClass]
    public class InterfaceTest: TestBase
    {
        [TestMethod]
        public void ListAllInterfaceWillNotFail()
        {
            var list = Connection.LoadAll<Interface>();
            Assert.IsNotNull(list);
        }

        /// <summary>
        /// <c>rx-byte</c>/<c>rx-packet</c> are LIFETIME counters on every transport — not the live rates the
        /// same record also carries.
        /// </summary>
        /// <remarks>
        /// The invariant is physical rather than numeric, so it holds on any router at any moment: an Ethernet
        /// frame is at least 20 bytes, so a genuine byte counter cannot be smaller than 20× the packet counter.
        /// It is what distinguishes the two families. WinBox labels the interface window's live bit rate 'Rx'
        /// and the lifetime total 'Rx Bytes'; normalizing those labels handed the API name <c>rx-byte</c> to the
        /// RATE, so the native transport reported 5 536 where the API reported 76 024 833 for the same
        /// interface — a wrong value under a right name, which no "did it throw" test can see (P2.52).
        /// </remarks>
        [TestMethod]
        public void InterfaceByteCountersAreCountersNotRates()
        {
            var iface = Connection.LoadAll<Interface>().FirstOrDefault(i => i.Name == TestConstants.Interface);
            Assert.IsNotNull(iface, $"Interface {TestConstants.Interface} not found on this router.");

            long rxPackets = iface.RxPacket;
            long rxBytes = iface.RxByte;
            if (rxPackets == 0)
                Assert.Inconclusive($"{TestConstants.Interface} has received nothing yet — no counter to check.");

            Assert.IsTrue(rxBytes >= rxPackets * 20,
                $"rx-byte ({rxBytes}) is smaller than the smallest possible frame times rx-packet " +
                $"({rxPackets}), so it is not a byte counter — most likely a live rate under the wrong name.");
        }

        [TestMethod]
        public void ListAllWirelessInterfaceWillNotFail()
        {
            EnsureCommandAvailable("/interface/wireless");
            var list = Connection.LoadAll<InterfaceWireless>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void ListAllWirelessRegistrationsWillNotFail()
        {
            EnsureCommandAvailable("/interface/wireless");
            var list = Connection.LoadAll<WirelessRegistrationTable>();
            Assert.IsNotNull(list);
        }

        // ListAllEthernet coverage lives in InterfaceEthernetTest.ListAllInterfaceEthernetWillNotFail.

        [TestMethod]
        public void FilteredUntypedListOfInterfacesWillNotFail()
        {
            var cmd = Connection.CreateCommandAndParameters(@"/interface/print
                            ?type=ether
                            ?type=wlan
                            ?#|");
            var list = cmd.ExecuteList();

            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void FilteredTypedListOfInterfacesWillNotFail()
        {
            var cmd = Connection.CreateCommandAndParameters(@"/interface/print
                            ?type=ether
                            ?type=wlan
                            ?#|");
            var list = cmd.LoadList<Interface>();

            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void ReadEthernetInterfaceRXWillNotFail()
        {
            var ethIface = Connection.LoadByName<Interface>(TestConstants.Interface);
            var rx = ethIface.RxByte;
        }


        [TestMethod]
        public void FilteredTypedAsyncListOfInterfacesWillNotFail()
        {
            EnsureCapability(TikConnectionCapability.Listen, "async interface list");
            var cmd = Connection.CreateCommandAndParameters(@"/interface/print
                            ?type=ether
                            ?type=wlan
                            ?#|");
            var list = new List<Interface>();
            cmd.LoadWithCallback<Interface>(i=>list.Add(i));
            Thread.Sleep(1000);
            cmd.CancelAndJoin();

            Assert.IsNotNull(list);
            Assert.IsTrue(list.Count > 0);
        }

        [TestMethod]
        public void UpdateCommentOnEth1WillNotFail()
        {
            var list = Connection.LoadAll<Interface>();
            Assert.IsNotNull(list);

            var eth = list.Where(iface => iface.DefaultName == TestConstants.Interface).Single();
            var originalComment = eth.Comment;
            try
            {
                eth.Comment = "My comment";
                Connection.Save(eth);
            }
            finally
            {
                //restore original comment so the test leaves no garbage on the router
                eth.Comment = originalComment;
                Connection.Save(eth);
            }
        }

        [TestMethod]
        public void UpdateCommentOnEth1_2_WillNotFail()
        {
            var originalComment = Connection.LoadByName<Interface>(TestConstants.Interface).Comment ?? "";
            try
            {
                var cmd = Connection.CreateCommand("/interface/set");
                cmd.AddParameter(TikSpecialProperties.Id, TestConstants.Interface);
                cmd.AddParameter("comment", "My next comment");
                cmd.ExecuteNonQuery();
            }
            finally
            {
                //restore original comment so the test leaves no garbage on the router
                var restoreCmd = Connection.CreateCommand("/interface/set");
                restoreCmd.AddParameter(TikSpecialProperties.Id, TestConstants.Interface);
                restoreCmd.AddParameter("comment", originalComment);
                restoreCmd.ExecuteNonQuery();
            }
        }

        [TestMethod]
        public void InterfaceTraficAsync_WillNotFail()
        {
            EnsureCapability(TikConnectionCapability.Listen, "async monitor-traffic");
            var cmd = Connection.CreateCommandAndParameters("/interface/monitor-traffic", "interface", TestConstants.Interface);
            List<ITikReSentence> responses = new List<ITikReSentence>();
            cmd.ExecuteWithCallback(re => { lock (responses) responses.Add(re); });
            Thread.Sleep(2 * 1000);

            ITikReSentence[] got;
            lock (responses) got = responses.ToArray();
            Assert.IsTrue(got.Length > 0, "the monitor produced no rows at all");
            // Assert on the CONTENT, not just the count: over WinBox native this streamed two blank records a
            // second — a monitor cycle on the wrong window — and a row count read that as success (P2.52).
            Assert.AreEqual(TestConstants.Interface, got[0].GetResponseField("name"),
                "a monitor-traffic row must name the interface it is monitoring");
            Assert.IsNotNull(got[0].GetResponseField("rx-bits-per-second"),
                "a monitor-traffic row must carry the reading the command exists for");
            cmd.CancelAndJoin(2 * 1000);
        }

        [TestMethod]
        public void WirelessInterfaceResaveWillNotFail()
        {
            EnsureCommandAvailable("/interface/wireless");
            var iface = Connection.LoadAll<InterfaceWireless>().FirstOrDefault(wlan => wlan.Name == TestConstants.WirelessInterface);
            if (iface == null)
                Assert.Inconclusive($"Interface {TestConstants.WirelessInterface} not found on this router.");
            iface.Comment = "test";
            Connection.Save(iface);
        }

        #region LoadListenAsync

        [TestMethod]
        public void LoadListenAsync_DetectsInterfaceChange()
        {
            EnsureCapability(TikConnectionCapability.Listen, "LoadListenAsync");
            string IFACE = TestConstants.Interface;
            const string TEST_COMMENT = "tik4net-listen-test";

            List<Interface> changes = new List<Interface>();
            Exception listenException = null;

            var listenCmd = Connection.LoadListenWithCallback<Interface>(
                iface => { lock (changes) changes.Add(iface); },
                onDeletedCallback: null,
                onExceptionCallback: ex => { listenException = ex; });

            try
            {
                // The change MUST be made over a second connection (P2.14). The CLI family emulates listen
                // by polling a single request/reply terminal, and that worker owns the channel while it
                // runs — a `set` issued on the same connection contends with the poller, which is why this
                // test was a lottery over Telnet/WinBox CLI while passing over the tag-multiplexed API.
                // Driving the trigger harder on the shared connection makes it *worse*, not better:
                // measured 3-of-4 failures with one trigger, 4-of-4 when retried every 500 ms.
                //
                // The retry itself is still needed, because the listen is not necessarily registered when
                // LoadListenAsync returns and a change made before that is simply unobservable. Each round
                // uses a *different* comment — RouterOS emits nothing for a `set` that changes nothing, so
                // re-sending the same value would not give a second chance.
                int round = 0;
                bool detected;
                using (var trigger = OpenSecondaryConnection())
                {
                    detected = WaitUntil(() =>
                    {
                        var setCmd = trigger.CreateCommand("/interface/set");
                        setCmd.AddParameter(TikSpecialProperties.Id, IFACE);
                        setCmd.AddParameter("comment", TEST_COMMENT + "-" + (++round));
                        setCmd.ExecuteNonQuery();

                        Thread.Sleep(1000);   // one CLI listen poll interval before re-triggering
                        if (listenException != null) return true;   // stop early; asserted below
                        lock (changes) return changes.Any(i => i.Name == IFACE);
                    }, timeoutSeconds: 20, pollMs: 0);
                }

                Assert.IsNull(listenException, "Unexpected listen error: " + listenException?.Message);
                Assert.IsTrue(detected,
                    $"Expected at least one change notification for {IFACE} after {round} triggers");
            }
            finally
            {
                listenCmd.CancelAndJoin();

                var cleanCmd = Connection.CreateCommand("/interface/set");
                cleanCmd.AddParameter(TikSpecialProperties.Id, IFACE);
                cleanCmd.AddParameter("comment", "");
                cleanCmd.ExecuteNonQuery();
            }
        }

        [TestMethod]
        public void LoadListenAsync_DetectsDeletedItem()
        {
            EnsureCapability(TikConnectionCapability.Listen, "LoadListenAsync");
            const string TEST_IP = "192.0.2.1/32"; // TEST-NET, safe dummy address
            string TEST_IFACE = TestConstants.Interface;

            // Pre-cleanup: remove any leftover address from a previous crashed run
            try
            {
                var printCmd = Connection.CreateCommand("/ip/address/print");
                printCmd.AddParameter("address", TEST_IP, TikCommandParameterFormat.Filter);
                foreach (var row in printCmd.ExecuteList())
                {
                    var id = row.GetResponseField(TikSpecialProperties.Id);
                    Connection.CreateCommandAndParameters("/ip/address/remove",
                        TikCommandParameterFormat.NameValue,
                        TikSpecialProperties.Id, id).ExecuteNonQuery();
                }
            }
            catch { /* leftover cleanup is best-effort */ }

            List<string> deletedIds = new List<string>();
            List<string> seenIds = new List<string>();
            Exception listenException = null;
            string newId = null;

            var listenCmd = Connection.LoadListenWithCallback<Objects.Ip.IpAddress>(
                addr => { lock (seenIds) seenIds.Add(addr.Id); },
                onDeletedCallback: id => { lock (deletedIds) deletedIds.Add(id); },
                onExceptionCallback: ex => { listenException = ex; });

            try
            {
                // Driven over a second connection for the same reason as
                // LoadListenAsync_DetectsInterfaceChange: on the CLI transports the listen poller owns the
                // terminal, so CRUD on the shared connection contends with it (P2.14).
                //
                // The add/remove pair sits inside the retry because an id can only be deleted once, so each
                // round needs a fresh address — but the two halves must NOT be back-to-back. The CLI listen
                // diffs successive snapshots, so it can only report a deletion for a row it has already
                // seen: an address created and removed inside one poll interval is invisible, and doing
                // exactly that is how this test was made to fail 3-of-3 while its sibling was being fixed.
                // So wait for the listen to actually observe the address (via the change callback) before
                // removing it. That wait is best-effort, not asserted — if the id never shows up, the
                // timeout still leaves several poll intervals, and the outer retry gets another attempt.
                bool detected = false;
                using (var trigger = OpenSecondaryConnection())
                {
                    DateTime deadline = DateTime.UtcNow.AddSeconds(45);
                    while (!detected && listenException == null && DateTime.UtcNow < deadline)
                    {
                        newId = trigger.CreateCommandAndParameters("/ip/address/add",
                            TikCommandParameterFormat.NameValue,
                            "address", TEST_IP,
                            "interface", TEST_IFACE).ExecuteScalar();

                        WaitUntil(() =>
                        {
                            lock (seenIds)
                                return seenIds.Any(s => string.Equals(s, newId, StringComparison.OrdinalIgnoreCase));
                        }, timeoutSeconds: 6);

                        string removed = newId;
                        trigger.CreateCommandAndParameters("/ip/address/remove",
                            TikCommandParameterFormat.NameValue,
                            TikSpecialProperties.Id, removed).ExecuteNonQuery();
                        newId = null;   // gone from the router — nothing for the cleanup below to do

                        detected = WaitUntil(() =>
                        {
                            if (listenException != null) return true;   // stop early; asserted below
                            lock (deletedIds)
                                return deletedIds.Any(d => string.Equals(d, removed, StringComparison.OrdinalIgnoreCase));
                        }, timeoutSeconds: 6);
                    }
                }

                Assert.IsNull(listenException, "Unexpected listen error: " + listenException?.Message);
                Assert.IsTrue(detected, "Expected a deleted-item notification for the removed address");
            }
            finally
            {
                listenCmd.CancelAndJoin();

                // cleanup in case a remove failed part-way through the retry
                if (newId != null)
                {
                    try
                    {
                        Connection.CreateCommandAndParameters("/ip/address/remove",
                            TikCommandParameterFormat.NameValue,
                            TikSpecialProperties.Id, newId).ExecuteNonQuery();
                    }
                    catch { }
                }
            }
        }

        #endregion

        [TestMethod]
        public void ParallelSniffCommandsAreCorrectlyCancelled()
        {
            EnsureCapability(TikConnectionCapability.Listen, "parallel async commands");
            Connection.DebugEnabled = true;

            // Both lists are appended from the reader thread and read from this one. They used to be
            // unsynchronized List<T> — a genuine data race, not just a timing one, and List<T> gives no
            // guarantee at all when Add and Count overlap. Lock both sides.
            var cmdWlan = Connection.CreateCommandAndParameters("/interface/monitor-traffic", "interface", TestConstants.WirelessInterface);
            List<ITikReSentence> responsesWlan = new List<ITikReSentence>();
            cmdWlan.ExecuteWithCallback(re => { lock (responsesWlan) responsesWlan.Add(re); });

            var cmdEth = Connection.CreateCommandAndParameters("/interface/monitor-traffic", "interface", TestConstants.Interface);
            List<ITikReSentence> responsesEth = new List<ITikReSentence>();
            cmdEth.ExecuteWithCallback(re => { lock (responsesEth) responsesEth.Add(re); });

            int EthCount() { lock (responsesEth) return responsesEth.Count; }

            // /interface/monitor-traffic emits about once a second, so the old fixed 1 s waits demanded a
            // row inside exactly one emit interval — no margin on a transport that is slower than the API
            // or a router that is busy (P2.14). Wait for the actual condition instead.
            Assert.IsTrue(WaitUntil(() => EthCount() > 0),
                "the eth monitor produced no rows at all — nothing to test cancellation against");

            cmdWlan.CancelAndJoin();
            int cnt = EthCount();

            // The point of the test: cancelling one async command must not disturb the other.
            Assert.IsTrue(WaitUntil(() => EthCount() > cnt),
                "cancelling the wlan monitor also stopped the eth monitor");
            Assert.IsTrue(cmdEth.IsRunning, "the eth monitor stopped running after the wlan monitor was cancelled");
            cmdEth.CancelAndJoin();
        }
    }
}
