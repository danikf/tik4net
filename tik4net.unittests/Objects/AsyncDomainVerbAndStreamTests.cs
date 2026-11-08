using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface;
using tik4net.Objects.Interface.Ethernet;
using tik4net.Objects.Ip;
using tik4net.Objects.Tool;
using tik4net.Objects.Tool.Romon;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// Review D5: the typed async streams (<c>LoadWithDurationAsync</c> / <c>LoadUntilDoneAsync</c>, net8.0) and the
    /// <c>…Async</c> twins of the verbs that wait for the router's answer. Each twin sends what its sync original
    /// sends and maps the rows the same way.
    /// </summary>
    [TestClass]
    public class AsyncDomainVerbAndStreamTests
    {
        private static TikFakeConnection Answering(string path, params Dictionary<string, string>[] rows)
        {
            var sentences = rows.Select(r => (ITikSentence)new TikFakeReSentence(r))
                .Concat(new ITikSentence[] { new TikFakeDoneSentence() }).ToList();
            return new TikFakeConnection().WithResponse(r => r.First() == path, sentences);
        }

        private static Dictionary<string, string> Row(params string[] namesAndValues)
        {
            var d = new Dictionary<string, string>();
            for (int i = 0; i < namesAndValues.Length; i += 2)
                d[namesAndValues[i]] = namesAndValues[i + 1];
            return d;
        }

        // ── async verbs, both TFMs ──────────────────────────────────────────────────────────────

        [TestMethod]
        public async Task PingAsyncSendsWhatPingSendsAndMapsTheSameRows()
        {
            var fake = Answering("/ping", Row("host", "192.0.2.1", "time", "1ms"), Row("host", "192.0.2.1", "time", "2ms"));
            var sync = fake.Ping("192.0.2.1", 2).ToList();
            var async = await fake.PingAsync("192.0.2.1", 2);

            CollectionAssert.AreEqual(fake.SentCommands[0], fake.SentCommands[1]);
            CollectionAssert.AreEqual(sync.Select(p => p.ToString()).ToList(), async.Select(p => p.ToString()).ToList());
            Assert.AreEqual(2, async.Count);
        }

        [TestMethod]
        public async Task TracerouteAsyncSendsWhatTracerouteSends()
        {
            var fake = Answering("/tool/traceroute", Row("address", "192.0.2.1"));
            fake.Traceroute("192.0.2.1").ToList();
            var hops = await fake.TracerouteAsync("192.0.2.1");

            CollectionAssert.AreEqual(fake.SentCommands[0], fake.SentCommands[1]);
            Assert.AreEqual("192.0.2.1", hops.Single().Address.Value);
        }

        [TestMethod]
        public async Task RomonDiscoverAsyncKeepsTheLatestReportPerNeighbourAsTheSyncOneDoes()
        {
            var fake = Answering("/tool/romon/discover",
                Row("address", "AA:BB:CC:DD:EE:FF", "identity", "first"),
                Row("address", "AA:BB:CC:DD:EE:FE", "identity", "other"),
                Row("address", "AA:BB:CC:DD:EE:FF", "identity", "latest"));
            var sync = fake.RomonDiscover().ToList();
            var async = await fake.RomonDiscoverAsync();

            CollectionAssert.AreEqual(fake.SentCommands[0], fake.SentCommands[1]);
            CollectionAssert.AreEqual(new[] { "latest", "other" }, async.Select(n => n.Identity.Value).ToList());
            CollectionAssert.AreEqual(sync.Select(n => n.Identity.Value).ToList(), async.Select(n => n.Identity.Value).ToList());
        }

        [TestMethod]
        public void TheRomonAsyncVerbsRefuseABadArgumentFromTheCallItself()
        {
            var fake = new TikFakeConnection();
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => { fake.RomonDiscoverAsync(TimeSpan.FromSeconds(1)); });
            Assert.ThrowsException<ArgumentException>(() => { fake.RomonPingAsync(""); });
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => { fake.RomonPingAsync("AA:BB:CC:DD:EE:FF", 0); });
            Assert.AreEqual(0, fake.SentCommands.Count);
        }

        [TestMethod]
        public async Task RomonPingAsyncSendsWhatRomonPingSends()
        {
            var fake = Answering("/tool/romon/ping", Row("time", "1ms"), Row("time", "2ms"));
            fake.RomonPing("AA:BB:CC:DD:EE:FF", 2).ToList();
            var echoes = await fake.RomonPingAsync("AA:BB:CC:DD:EE:FF", 2);

            CollectionAssert.AreEqual(fake.SentCommands[0], fake.SentCommands[1]);
            Assert.AreEqual(2, echoes.Count);
        }

        [TestMethod]
        public async Task TheSnapshotTwinsSendWhatTheSyncSnapshotsSend()
        {
            var traffic = Answering("/interface/monitor-traffic", Row("name", "ether1", "rx-bits-per-second", "1000"));
            traffic.GetInterfaceMonitorTrafficSnapshot("ether1");
            var t = await traffic.GetInterfaceMonitorTrafficSnapshotAsync("ether1");
            CollectionAssert.AreEqual(traffic.SentCommands[0], traffic.SentCommands[1]);
            Assert.AreEqual("1000", t.RxBitsPerSecond.ToString());

            var ethernet = Answering("/interface/ethernet/monitor", Row("name", "ether1"));
            ethernet.GetEthernetMonitorSnapshot("ether1");
            await ethernet.GetEthernetMonitorSnapshotAsync("ether1");
            CollectionAssert.AreEqual(ethernet.SentCommands[0], ethernet.SentCommands[1]);

            var pppoe = Answering("/interface/pppoe-client/monitor", Row("status", "connected"));
            pppoe.GetInterfacePppoeClientMonitorSnapshot("pppoe-out1");
            await pppoe.GetInterfacePppoeClientMonitorSnapshotAsync("pppoe-out1");
            CollectionAssert.AreEqual(pppoe.SentCommands[0], pppoe.SentCommands[1]);
        }

        [TestMethod]
        public async Task AnAsyncVerbIsRefusedWhereTheTransportHasNoAsyncCommands()
        {
            var fake = Answering("/ping", Row("host", "192.0.2.1"));
            fake.Capabilities &= ~TikConnectionCapability.AsyncCommands;

            await Assert.ThrowsExceptionAsync<TikConnectionCapabilityNotSupportedException>(() => fake.PingAsync("192.0.2.1", 1));
            await Assert.ThrowsExceptionAsync<TikConnectionCapabilityNotSupportedException>(
                () => fake.GetEthernetMonitorSnapshotAsync("ether1"));
        }

        [TestMethod]
        public async Task ACancelledTokenStopsAnAsyncVerb()
        {
            var fake = Answering("/ping", Row("host", "192.0.2.1"));
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                try
                {
                    await fake.PingAsync("192.0.2.1", 1, cts.Token);
                    Assert.Fail("a cancelled token did not stop PingAsync");
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

#if NET8_0_OR_GREATER
        // ── typed streams, net8.0 ───────────────────────────────────────────────────────────────

        private static TikFakeConnection TwoAddresses(bool streaming)
        {
            var fake = new TikFakeConnection().WithEntities(
                new IpAddress { Address = "192.0.2.1/24", Interface = "ether1" }.WithId("*1"),
                new IpAddress { Address = "192.0.2.2/24", Interface = "ether2" }.WithId("*2"));
            if (streaming)
                fake.Capabilities |= TikConnectionCapability.Streaming;
            return fake;
        }

        private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
        {
            var list = new List<T>();
            await foreach (var item in source)
                list.Add(item);
            return list;
        }

        [TestMethod]
        public async Task LoadWithDurationAsyncStreamsTheRowsLoadWithDurationReturns()
        {
            var fake = TwoAddresses(streaming: true);
            var sync = fake.LoadWithDuration<IpAddress>(TimeSpan.FromSeconds(1)).ToList();
            var streamed = await ToListAsync(fake.LoadWithDurationAsync<IpAddress>(TimeSpan.FromSeconds(1)));

            CollectionAssert.AreEqual(fake.SentCommands[0], fake.SentCommands[1]);
            CollectionAssert.AreEqual(sync.Select(a => a.Id).ToList(), streamed.Select(a => a.Id).ToList());
            CollectionAssert.AreEqual(new[] { "192.0.2.1/24", "192.0.2.2/24" }, streamed.Select(a => a.Address.Value).ToList());
        }

        [TestMethod]
        public async Task TheCommandLevelStreamsMapRows()
        {
            var fake = TwoAddresses(streaming: true);
            var byDuration = await ToListAsync(fake.CreateCommand("/ip/address/print")
                .LoadWithDurationAsync<IpAddress>(TimeSpan.FromSeconds(1)));
            var untilDone = await ToListAsync(fake.CreateCommand("/ip/address/print").LoadUntilDoneAsync<IpAddress>());

            Assert.AreEqual(2, byDuration.Count);
            CollectionAssert.AreEqual(byDuration.Select(a => a.Id).ToList(), untilDone.Select(a => a.Id).ToList());
        }

        [TestMethod]
        public async Task AStreamedRowIsTrackedSoSaveSendsOnlyWhatChanged()
        {
            var fake = TwoAddresses(streaming: true);
            var metadata = TikEntityMetadataCache.GetMetadata<IpAddress>();
            var first = (await ToListAsync(fake.LoadWithDurationAsync<IpAddress>(TimeSpan.FromSeconds(1)))).First();
            var viaUntilDone = (await ToListAsync(fake.LoadUntilDoneAsync<IpAddress>())).First();

            Assert.IsFalse(fake.ChangeTracker().HasChanges(first, metadata));
            first.Comment = "changed";
            CollectionAssert.AreEqual(new[] { "comment" }, fake.ChangeTracker().GetChanges(first, metadata).Keys.ToList());

            viaUntilDone.Comment = "changed";
            CollectionAssert.AreEqual(new[] { "comment" }, fake.ChangeTracker().GetChanges(viaUntilDone, metadata).Keys.ToList());
        }

        [TestMethod]
        public void LoadWithDurationAsyncIsRefusedAtTheCallWhereTheTransportDoesNotStream()
        {
            var fake = TwoAddresses(streaming: false);

            Assert.ThrowsException<TikConnectionCapabilityNotSupportedException>(
                () => fake.LoadWithDurationAsync<IpAddress>(TimeSpan.FromSeconds(1)));
            Assert.ThrowsException<TikConnectionCapabilityNotSupportedException>(
                () => fake.CreateCommand("/ip/address/print").LoadWithDurationAsync<IpAddress>(TimeSpan.FromSeconds(1)));
            Assert.AreEqual(0, fake.SentCommands.Count);
        }

        [TestMethod]
        public async Task LoadUntilDoneAsyncRunsOnAsyncCommandsAloneAndIsRefusedWithout()
        {
            var fake = TwoAddresses(streaming: false);
            Assert.AreEqual(2, (await ToListAsync(fake.LoadUntilDoneAsync<IpAddress>())).Count);

            fake.Capabilities &= ~TikConnectionCapability.AsyncCommands;
            Assert.ThrowsException<TikConnectionCapabilityNotSupportedException>(() => fake.LoadUntilDoneAsync<IpAddress>());
        }

        [TestMethod]
        public async Task ACancelledStreamStopsAfterTheRowsAlreadyHandedOut()
        {
            var fake = TwoAddresses(streaming: true);
            using (var cts = new CancellationTokenSource())
            {
                var seen = new List<IpAddress>();
                try
                {
                    await foreach (var a in fake.LoadWithDurationAsync<IpAddress>(TimeSpan.FromSeconds(1), cts.Token))
                    {
                        seen.Add(a);
                        cts.Cancel();
                    }
                    Assert.Fail("the stream ran past its cancellation");
                }
                catch (OperationCanceledException)
                {
                }
                Assert.AreEqual(1, seen.Count);
            }

            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                    () => ToListAsync(fake.LoadUntilDoneAsync<IpAddress>(cancellationToken: cancelled.Token)));
            }
        }

        [TestMethod]
        public async Task TheReadIsReportedToTheDiagnosticsWhenTheStreamEnds()
        {
            var fake = TwoAddresses(streaming: true);
            var reports = new List<TikEntityReadReport>();
            fake.SetEntityDiagnostics(r => reports.Add(r));

            await ToListAsync(fake.LoadWithDurationAsync<IpAddress>(TimeSpan.FromSeconds(1)));
            await foreach (var _ in fake.LoadUntilDoneAsync<IpAddress>())
                break;

            // The whole read is complete, so it names the fields no row carried. The stream left after one row is not:
            // it may not claim "absent from every row", and with nothing else to say it reports nothing.
            Assert.AreEqual(1, reports.Count, string.Join("; ", reports));
            Assert.IsTrue(reports[0].IsComplete);
            Assert.AreEqual(2, reports[0].RowCount);
            CollectionAssert.Contains(reports[0].FieldsAbsentFromEveryRow.ToList(), "comment");
        }
#endif
    }
}
