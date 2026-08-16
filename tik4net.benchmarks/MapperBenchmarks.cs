using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;
using tik4net.Testing;

namespace tik4net.Benchmarks
{
    /// <summary>
    /// The O/R mapper's per-row cost, measured without a router.
    /// <para>
    /// The subject is <see cref="FirewallFilter"/> because it is the shape the mapper is slowest on and the
    /// one a real caller loads in bulk: ~50 mapped properties, a plain enum, a <c>[Flags]</c> enum, nullable
    /// bools, <c>long</c> counters, and an <c>.id</c> with a <b>private setter</b> — which is the case a
    /// compiled accessor has to keep working, not just keep fast.
    /// </para>
    /// <para>
    /// Both directions are measured because they use different halves of the accessor:
    /// <see cref="LoadAll_1000Rows"/> is <c>SetEntityValue</c> + <c>ConvertFromString</c>,
    /// <see cref="Serialize_1000Entities"/> is <c>GetEntityValue</c> + <c>ConvertToString</c>. A change that
    /// speeds up only one of them would otherwise look like a win twice its size.
    /// </para>
    /// </summary>
    [MemoryDiagnoser]
    public class MapperBenchmarks
    {
        private const int RowCount = 1000;

        private TikFakeConnection _connection;
        private List<FirewallFilter> _entities;
        private List<Dictionary<string, string>> _rows;
        private TikEntityMetadata _metadata;

        /// <summary>
        /// Builds the 1000 fake <c>!re</c> rows ONCE, outside the measured region.
        /// <para>
        /// The rows are registered through <c>WithResponse</c> with an already-built sentence list rather than
        /// through <c>WithEntities</c>, which re-serializes the entities on every call — that would put the
        /// write path inside the read benchmark and hide half of any regression.
        /// </para>
        /// </summary>
        [GlobalSetup]
        public void Setup()
        {
            _metadata = TikEntityMetadataCache.GetMetadata<FirewallFilter>();

            var prototype = new FirewallFilter
            {
                Action = FirewallFilter.ActionType.Drop,
                Chain = "forward",
                Comment = "benchmark rule",
                ConnectionState = FirewallFilter.ConnectionStateType.Established | FirewallFilter.ConnectionStateType.Related,
                ConnectionLimit = 32,
                DstAddress = "10.0.0.0/8",
                DstPort = "443",
                InInterface = "ether1",
                Protocol = "tcp",
                SrcAddress = "192.168.88.0/24",
            };

            // A null property has no wire form — the field is simply absent, which is what the router does too.
            // Writing "" instead would hand the parser an empty string for an int? and measure an exception.
            var words = _metadata.Properties
                .Select(p => new { p.FieldName, Value = p.GetEntityValue(prototype) })
                .Where(p => p.Value != null)
                .ToDictionary(p => p.FieldName, p => p.Value);

            var sentences = new List<ITikSentence>(RowCount + 1);
            _rows = new List<Dictionary<string, string>>(RowCount);
            for (int i = 0; i < RowCount; i++)
            {
                var row = new Dictionary<string, string>(words);
                row[TikSpecialProperties.Id] = "*" + i.ToString("X");
                _rows.Add(row);
                sentences.Add(new TikFakeReSentence(row));
            }
            sentences.Add(new TikFakeDoneSentence());

            string loadCommand = _metadata.EntityPath + _metadata.LoadCommand;
            _connection = new TikFakeConnection()
                .WithResponse(rows => rows.First() == loadCommand, sentences);

            _entities = _connection.LoadAll<FirewallFilter>().ToList();
        }

        /// <summary>Read path: 1000 rows materialized into entities, the way <c>LoadAll</c> does it.</summary>
        [Benchmark(Description = "LoadAll<FirewallFilter> — 1000 rows")]
        public int LoadAll_1000Rows()
            => _connection.LoadAll<FirewallFilter>().Count();

        /// <summary>
        /// The same 1000 rows written into entities with the surrounding machinery removed — no command, no
        /// sentence iteration, no list.
        /// <para>
        /// It mirrors <c>CreateObject</c> exactly, including converting a property the row does NOT carry
        /// from its <c>DefaultValue</c>: skipping those would make this a subset of a load rather than its
        /// mapper half, and on an entity with enum defaults that difference is most of the cost being
        /// measured. Because it mirrors it, <see cref="LoadAll_1000Rows"/> minus this one is the part of a
        /// load that is NOT the mapper — unchanged by any work in this track, and therefore usable as a
        /// control when comparing two builds on a machine whose speed drifts between runs.
        /// </para>
        /// </summary>
        [Benchmark(Description = "SetEntityValue — 1000 rows × all properties")]
        public int Materialize_1000Rows()
        {
            int count = 0;
            foreach (var row in _rows)
            {
                var entity = new FirewallFilter();
                foreach (var property in _metadata.Properties)
                {
                    string value;
                    if (!row.TryGetValue(property.FieldName, out value))
                        value = property.DefaultValue;
                    property.SetEntityValue(entity, value);
                }
                count++;
            }
            return count;
        }

        /// <summary>
        /// Write path: every mapped property of 1000 entities read back out, which is what <c>Save</c>,
        /// the change tracker and <c>SaveListDifferences</c> each do per entity.
        /// </summary>
        [Benchmark(Description = "GetEntityValue — 1000 entities × all properties")]
        public int Serialize_1000Entities()
        {
            int length = 0;
            foreach (var entity in _entities)
            {
                foreach (var property in _metadata.Properties)
                {
                    string value = property.GetEntityValue(entity);
                    if (value != null)
                        length += value.Length;
                }
            }
            return length;
        }
    }
}
