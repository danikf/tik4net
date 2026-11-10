#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;

namespace tik4net.Benchmarks
{
    /// <summary>
    /// The binary API's whole client-side read path over a real loopback socket: framing, word decode, the reader
    /// thread, the tag dispatcher, sentence construction and (for the load) the mapper and the change tracker.
    /// The peer is <see cref="LoopbackApiRouter"/>, which answers from pre-encoded bytes.
    /// </summary>
    [MemoryDiagnoser]
    public class ApiSocketBenchmarks
    {
        [Params(1000, 10000)]
        public int RowCount;

        private LoopbackApiRouter _router = null!;
        private ITikConnection _connection = null!;

        [GlobalSetup]
        public void Setup()
        {
            _router = new LoopbackApiRouter(FirewallRows.Rows(RowCount));
            _connection = ConnectionFactory.OpenConnection(TikConnectionType.Api, "127.0.0.1", _router.Port, "admin", "");
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _connection.Dispose();
            _router.Dispose();
        }

        /// <summary>Framing, reader, dispatcher and sentences only: no mapper.</summary>
        [Benchmark(Description = "API ExecuteList (raw sentences)")]
        public int ExecuteList()
            => _connection.CreateCommand("/ip/firewall/filter/print").ExecuteList().Count();

        [Benchmark(Description = "API LoadAll<FirewallFilter>")]
        public int LoadAll()
            => _connection.LoadAll<FirewallFilter>().Count();

        [Benchmark(Description = "API LoadAllAsync<FirewallFilter>")]
        public async Task<int> LoadAllAsync()
            => (await _connection.LoadAllAsync<FirewallFilter>(default)).Count();
    }
}
