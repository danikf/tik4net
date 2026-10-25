using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace tik4net.integrationtests
{
    /// <summary>
    /// <c>ExecuteListUntilDone</c> on every transport (command-layer D17): a self-terminating read returns the API's rows
    /// (<c>/ping</c>, the path <c>ToolPing</c> reads),
    /// and its timeout ends a read still running with <see cref="TikCommandAbortException"/>, leaving the connection
    /// usable — or, on a CLI connection set to <see cref="TikCancellationMode.AbandonAndClose"/>, closed.
    /// </summary>
    /// <remarks>
    /// The target is the router's own address (App.config <c>host</c>): a ping that every transport can run and the
    /// router answers at once, one reply a second, so <c>count=10</c> outlasts a 2 s deadline by eight seconds.
    /// </remarks>
    [TestClass]
    [SafeInParallelLegs]
    public class ExecuteListUntilDoneParityTest : TestBase
    {
        private static string RouterAddress => LabConfig.Get("host");

        private static ITikCommand Ping(ITikConnection connection, int count)
            => connection.CreateCommandAndParameters("/ping", TikCommandParameterFormat.NameValue,
                "address", RouterAddress, "count", count.ToString(System.Globalization.CultureInfo.InvariantCulture));

        private static string Describe(ITikReSentence row)
            => string.Join(" ", row.Words.Where(w => w.Key != TikSpecialProperties.Tag).Select(w => w.Key + "=" + w.Value));

        private static List<ITikReSentence> ApiRows(int count)
        {
            using (var api = LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api))
                return Ping(api, count).ExecuteListUntilDone(TimeSpan.FromSeconds(30)).ToList();
        }

        /// <summary>The fields a reply row carries whatever the timing: the sequence, the host, the size.</summary>
        private static string Stable(ITikReSentence row)
            => string.Join(" ", new[] { "seq", "host", "size" }.Select(f => f + "=" + row.GetResponseFieldOrDefault(f, "<none>")));

        [TestMethod]
        public void Ping_UntilDoneAndList_ReturnTheApisRows()
        {
            var api = ApiRows(3);
            var untilDone = Ping(Connection, 3).ExecuteListUntilDone().ToList();
            var list = Ping(Connection, 3).ExecuteList().ToList();

            foreach (var (name, rows) in new[] { ("api", api), ("ExecuteListUntilDone", untilDone), ("ExecuteList", list) })
            {
                Console.WriteLine($"-- {name} ({rows.Count} rows)");
                foreach (var row in rows)
                    Console.WriteLine("   " + Describe(row));
            }

            Assert.AreEqual(3, api.Count, "the API's own ping");
            CollectionAssert.AreEqual(api.Select(Stable).ToList(), untilDone.Select(Stable).ToList(), "ExecuteListUntilDone");
            CollectionAssert.AreEqual(api.Select(Stable).ToList(), list.Select(Stable).ToList(), "ExecuteList");
            // The field sets are reported, not asserted: they differ by transport today (the CLI's `ping as-value`
            // prints no running statistics, WinBox native's ping window prints its own), which is each transport's
            // parity finding for its own review. The rows themselves are the same.
            var apiFields = new HashSet<string>(api[0].Words.Keys.Where(k => k != TikSpecialProperties.Tag));
            var ownFields = new HashSet<string>(untilDone[0].Words.Keys.Where(k => k != TikSpecialProperties.Tag));
            Console.WriteLine("fields the API has and this transport does not: " + string.Join(",", apiFields.Except(ownFields).OrderBy(k => k, StringComparer.Ordinal)));
            Console.WriteLine("fields this transport has and the API does not: " + string.Join(",", ownFields.Except(apiFields).OrderBy(k => k, StringComparer.Ordinal)));
        }

        /// <summary>
        /// A deadline that fires mid-read: <c>count=10</c> under a 2 s timeout. On the test connection's own cancellation
        /// behaviour (the CLI family's default, <see cref="TikCancellationMode.Cooperative"/>, lets the ping run out).
        /// </summary>
        [TestMethod]
        public void Ping_DeadlineMidRead_ThrowsAbortAndTheConnectionAnswersNext()
        {
            bool cli = Connection is ITikCancellationModeConnection;
            var sw = Stopwatch.StartNew();
            var ex = Assert.ThrowsException<TikCommandAbortException>(
                () => Ping(Connection, 10).ExecuteListUntilDone(TimeSpan.FromSeconds(2)).ToList());
            sw.Stop();
            Console.WriteLine($"{ResolveConnectionType()}: aborted after {sw.ElapsedMilliseconds} ms: {ex.Message}");
            StringAssert.Contains(ex.Message, "did not finish within 2 second(s)");
            if (cli)
                Assert.IsTrue(sw.Elapsed < TimeSpan.FromSeconds(20), "a cooperative cancel waits for the ping to end, no longer");
            else
                Assert.IsTrue(sw.Elapsed < TimeSpan.FromSeconds(7), "the deadline ends the read");

            // The next command gets its own answer, not the rest of the ping.
            string name = Connection.CreateCommand("/system/identity/print").ExecuteScalar("name");
            Assert.IsFalse(string.IsNullOrEmpty(name));
            var rows = Ping(Connection, 1).ExecuteListUntilDone(TimeSpan.FromSeconds(10)).ToList();
            Assert.AreEqual(1, rows.Count, string.Join(" | ", rows.Select(Describe)));
        }

        [TestMethod]
        public void Ping_DeadlineMidRead_UnderAbandonAndClose_ClosesTheConnection()
        {
            var type = ResolveConnectionType();
            if (!(Connection is ITikCancellationModeConnection))
                Assert.Inconclusive($"{type} has no cancellation mode to set (it cancels in flight).");

            var setup = LabSetup(type);
            setup.CancellationMode = TikCancellationMode.AbandonAndClose;
            using (var connection = setup.Create(type))
            {
                var sw = Stopwatch.StartNew();
                Assert.ThrowsException<TikCommandAbortException>(
                    () => Ping(connection, 10).ExecuteListUntilDone(TimeSpan.FromSeconds(2)).ToList());
                sw.Stop();
                Console.WriteLine($"{type} AbandonAndClose: aborted after {sw.ElapsedMilliseconds} ms; IsOpened={connection.IsOpened}");
                Assert.IsTrue(sw.Elapsed < TimeSpan.FromSeconds(7), "abandoning does not wait for the ping");
                Assert.IsFalse(connection.IsOpened, "an abandoned read closes the connection");
            }
        }
    }
}
