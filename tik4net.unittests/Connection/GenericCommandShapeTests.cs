// GenericCommandShapeTests.cs — what each Execute* method of the transport-neutral command hands the transport.
//
// Characterisation tests: they pin today's behaviour, including the asymmetries the command-layer review
// changes on purpose later (X2, X4, X5, X7, X12). A later fix shows up here as an assertion changed in its own
// commit. The binary API's half of the same method set is ApiCommandWireShapeTests.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;
using tik4net.Connection;

namespace tik4net.unittests.Connection
{
    [TestClass]
    public class GenericCommandShapeTests
    {
        /// <summary>
        /// A connection that records which hook each call reached and with which descriptor, and answers reads
        /// from a scripted queue (one row <c>{.id=*1, name=a}</c> when the queue is empty).
        /// </summary>
        private sealed class RecordingConnection : TikCommandConnectionBase, ITikMonitorTransport
        {
            public readonly List<string> Calls = new List<string>();
            public readonly Queue<List<TikRecordSentence>> Answers = new Queue<List<TikRecordSentence>>();

            public override TikConnectionCapability Capabilities
                => TikConnectionCapability.Crud | TikConnectionCapability.AsyncCommands;

            private void Record(string hook, TikCommandDescriptor d)
                => Calls.Add(hook + " | " + d.CommandText + " | "
                    + string.Join("; ", d.Parameters.Select(p => $"{p.Name}={p.Value} ({p.ParameterFormat})")));

            private IList<TikRecordSentence> NextRows()
                => Answers.Count > 0
                    ? Answers.Dequeue()
                    : new List<TikRecordSentence> { Row(".id", "*1", "name", "a") };

            protected override IList<TikRecordSentence> RunPrint(TikCommandDescriptor descriptor)
            {
                Record("RunPrint", descriptor);
                return NextRows();
            }

            protected override string RunAdd(TikCommandDescriptor descriptor)
            {
                Record("RunAdd", descriptor);
                return "*1";
            }

            protected override void RunNonQuery(TikCommandDescriptor descriptor) => Record("RunNonQuery", descriptor);

            protected override string RunRawText(TikCommandDescriptor descriptor)
            {
                Record("RunRawText", descriptor);
                return "";
            }

            protected override Task<IList<TikRecordSentence>> RunPrintAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
            {
                Record("RunPrintAsync", descriptor);
                return Task.FromResult(NextRows());
            }

            protected override Task<string> RunAddAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
            {
                Record("RunAddAsync", descriptor);
                return Task.FromResult("*1");
            }

            protected override Task RunNonQueryAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
            {
                Record("RunNonQueryAsync", descriptor);
                return Task.CompletedTask;
            }

            protected override Task<string> RunRawTextAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
            {
                Record("RunRawTextAsync", descriptor);
                return Task.FromResult("");
            }

            TikMonitorHandle ITikMonitorTransport.RunMonitorAsync(TikCommandDescriptor descriptor,
                Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
            {
                Record("RunMonitor", descriptor);
                onDone();
                return new TikMonitorHandle();   // no worker thread: Join returns at once
            }

            public override void Open(string host, string user, string password) { }
            public override void Open(string host, int port, string user, string password) { }
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public override void Close() { }
        }

        private static TikRecordSentence Row(params string[] namesAndValues)
        {
            var fields = new Dictionary<string, string>();
            for (int i = 0; i < namesAndValues.Length; i += 2)
                fields[namesAndValues[i]] = namesAndValues[i + 1];
            return new TikRecordSentence(fields);
        }

        private const string Unformatted = "name=a (Default); comment=b (Default)";
        private const string Filters = "name=a (Filter); comment=b (Filter)";

        /// <summary>Runs <paramref name="method"/> on <paramref name="commandText"/> with <c>name=a</c>,
        /// <c>comment=b</c> and returns the one line the connection recorded.</summary>
        private static string RecordOne(string commandText, string method)
        {
            var conn = new RecordingConnection();
            var cmd = conn.CreateCommandAndParameters(commandText, "name", "a", "comment", "b");
            Invoke(cmd, method);
            Assert.AreEqual(1, conn.Calls.Count, string.Join(Environment.NewLine, conn.Calls));
            return conn.Calls[0];
        }

        private static void Invoke(ITikCommand cmd, string method)
        {
            switch (method)
            {
                case "ExecuteNonQuery": cmd.ExecuteNonQuery(); break;
                case "ExecuteScalar": cmd.ExecuteScalar(); break;
                case "ExecuteSingleRow": cmd.ExecuteSingleRow(); break;
                case "ExecuteSingleRowOrDefault": cmd.ExecuteSingleRowOrDefault(); break;
                case "ExecuteList": cmd.ExecuteList().ToList(); break;
                case "ExecuteListUntilDone": cmd.ExecuteListUntilDone(TimeSpan.FromSeconds(1)).ToList(); break;
                case "ExecuteWithCallback": cmd.ExecuteWithCallback(_ => { }); break;
                case "ExecuteNonQueryAsync": cmd.ExecuteNonQueryAsync().GetAwaiter().GetResult(); break;
                case "ExecuteScalarAsync": cmd.ExecuteScalarAsync().GetAwaiter().GetResult(); break;
                case "ExecuteSingleRowAsync": cmd.ExecuteSingleRowAsync().GetAwaiter().GetResult(); break;
                case "ExecuteSingleRowOrDefaultAsync": cmd.ExecuteSingleRowOrDefaultAsync().GetAwaiter().GetResult(); break;
                case "ExecuteListAsync": cmd.ExecuteListAsync().GetAwaiter().GetResult(); break;
                default: throw new ArgumentOutOfRangeException(nameof(method), method, null);
            }
        }

        // ── What each method hands the transport ──────────────────────────────

        [DataTestMethod]
        [DataRow("ExecuteNonQuery", "/ip/address/set", "RunNonQuery", Unformatted)]
        [DataRow("ExecuteScalar", "/ip/address/print", "RunPrint", Filters)]
        [DataRow("ExecuteScalar", "/ip/address/add", "RunAdd", Unformatted)]
        [DataRow("ExecuteScalar", "/system/script/run", "RunPrint", Filters)]           // X12 as it is
        [DataRow("ExecuteSingleRow", "/ip/address/print", "RunPrint", Filters)]
        [DataRow("ExecuteSingleRowOrDefault", "/ip/address/print", "RunPrint", Filters)]
        [DataRow("ExecuteList", "/ip/address/print", "RunPrint", Filters)]
        [DataRow("ExecuteListUntilDone", "/tool/ping", "RunPrint", Filters)]           // X2 as it is: the timeout reaches nothing
        [DataRow("ExecuteWithCallback", "/interface/listen", "RunMonitor", Unformatted)]
        [DataRow("ExecuteNonQueryAsync", "/ip/address/set", "RunNonQueryAsync", Unformatted)]
        [DataRow("ExecuteScalarAsync", "/ip/address/print", "RunPrintAsync", Filters)]
        [DataRow("ExecuteScalarAsync", "/ip/address/add", "RunAddAsync", Unformatted)]
        [DataRow("ExecuteScalarAsync", "/system/script/run", "RunPrintAsync", Filters)]
        [DataRow("ExecuteSingleRowAsync", "/ip/address/print", "RunPrintAsync", Filters)]
        [DataRow("ExecuteSingleRowOrDefaultAsync", "/ip/address/print", "RunPrintAsync", Filters)]
        [DataRow("ExecuteListAsync", "/ip/address/print", "RunPrintAsync", Filters)]
        public void EachMethodReachesItsHookWithTheseFormats(string method, string commandText, string hook, string parameters)
        {
            Assert.AreEqual($"{hook} | {commandText} | {parameters}", RecordOne(commandText, method));
        }

        [TestMethod]
        public void ExecuteListWithFields_SendsOneCommaJoinedProplist()
        {
            // X5 as it is: one .proplist parameter; the binary API sends one word per field.
            var conn = new RecordingConnection();
            conn.CreateCommandAndParameters("/ip/address/print", "name", "a", "comment", "b")
                .ExecuteList("name", "address").ToList();

            Assert.AreEqual("RunPrint | /ip/address/print | " + Filters + "; .proplist=name,address (NameValue)",
                conn.Calls.Single());
        }

        [TestMethod]
        public void ExecuteListAsyncWithFields_SendsTheSameDescriptorAsItsSyncTwin()
        {
            var conn = new RecordingConnection();
            conn.CreateCommandAndParameters("/ip/address/print", "name", "a", "comment", "b")
                .ExecuteListAsync(new[] { "name", "address" }).GetAwaiter().GetResult();

            Assert.AreEqual("RunPrintAsync | /ip/address/print | " + Filters + "; .proplist=name,address (NameValue)",
                conn.Calls.Single());
        }

        [TestMethod]
        public void ExecuteListWithFields_KeepsTheCallersOwnProplist()
        {
            // The caller's .proplist wins and the argument is dropped; the binary API throws instead.
            var conn = new RecordingConnection();
            var cmd = conn.CreateCommand("/ip/address/print");
            cmd.AddParameter(TikSpecialProperties.Proplist, "comment");
            cmd.ExecuteList("name").ToList();

            Assert.AreEqual("RunPrint | /ip/address/print | .proplist=comment (NameValue)", conn.Calls.Single());
        }

        [TestMethod]
        public void TheCommandDefaultBeatsTheReadRule()
        {
            var conn = new RecordingConnection();
            var cmd = conn.CreateCommand("/ip/address/print", TikCommandParameterFormat.NameValue);
            cmd.AddParameter("name", "a");
            cmd.AddParameter("comment", "b");
            cmd.ExecuteList().ToList();

            Assert.AreEqual("RunPrint | /ip/address/print | name=a (NameValue); comment=b (NameValue)",
                conn.Calls.Single());
        }

        // ── What each method makes of the answer ──────────────────────────────

        [TestMethod]
        public void ExecuteScalar_ReadsTheFirstFieldThatIsNotTheId()
        {
            // X4 as it is: the first non-.id field in record order. The binary API throws on a multi-field row.
            var conn = new RecordingConnection();
            conn.Answers.Enqueue(new List<TikRecordSentence> { Row(".id", "*1", "name", "a", "mtu", "1500") });
            conn.Answers.Enqueue(new List<TikRecordSentence> { Row(".id", "*1", "name", "a", "mtu", "1500") });
            var cmd = conn.CreateCommand("/interface/print");

            Assert.AreEqual("a", cmd.ExecuteScalar());
            Assert.AreEqual("1500", cmd.ExecuteScalar("mtu"));
        }

        [TestMethod]
        public void ExecuteScalar_OfAFieldTheRowDoesNotHave_ThrowsSentenceException()
        {
            // E10: the type only. The message changes in the command-layer cleanup.
            var conn = new RecordingConnection();
            Assert.ThrowsException<TikSentenceException>(
                () => conn.CreateCommand("/interface/print").ExecuteScalar("missing"));
        }

        [TestMethod]
        public void AddParameterAndValues_RefusesAnUnpairedLastName()
        {
            // X7: an unpaired name is an error, and nothing of the list is added.
            var conn = new RecordingConnection();
            var cmd = conn.CreateCommand("/ip/address/print");
            var ex = Assert.ThrowsException<ArgumentException>(() => cmd.AddParameterAndValues("name", "a", "comment"));

            StringAssert.Contains(ex.Message, "'comment'");
            Assert.AreEqual(0, cmd.Parameters.Count);
        }

        [TestMethod]
        public void CreateCommandAndParameters_RefusesAnUnpairedLastName()
        {
            var conn = new RecordingConnection();
            var ex = Assert.ThrowsException<ArgumentException>(
                () => conn.CreateCommandAndParameters("/ip/address/print", "name", "a", "comment"));

            StringAssert.Contains(ex.Message, "'comment'");
            Assert.AreEqual(0, conn.Calls.Count);
        }

        [TestMethod]
        public void ExecuteListWithDuration_IsRefusedAsStreaming()
        {
            var conn = new RecordingConnection();
            var ex = Assert.ThrowsException<TikConnectionCapabilityNotSupportedException>(
                () => conn.CreateCommand("/tool/torch").ExecuteListWithDuration(TimeSpan.FromSeconds(1)));

            Assert.AreEqual(TikConnectionCapability.Streaming, ex.Capability);
            Assert.AreEqual(0, conn.Calls.Count);
        }
    }
}
