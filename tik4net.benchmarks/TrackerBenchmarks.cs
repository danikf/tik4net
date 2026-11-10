#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;
using tik4net.Objects.Tracking;

namespace tik4net.Benchmarks
{
    /// <summary>The change tracker's snapshot per loaded entity: what <c>LoadAll</c> pays on top of the mapper.</summary>
    [MemoryDiagnoser]
    public class TrackerBenchmarks
    {
        private List<FirewallFilter> _entities = null!;
        private TikEntityMetadata _metadata = null!;
        private TikChangeTracker _tracker = null!;

        [GlobalSetup]
        public void Setup()
        {
            _metadata = TikEntityMetadataCache.GetMetadata<FirewallFilter>();
            var rows = FirewallRows.Rows(1000);
            _entities = rows.Select(r =>
            {
                var e = new FirewallFilter();
                foreach (var p in _metadata.Properties)
                {
                    r.TryGetValue(p.FieldName, out string? v);
                    p.SetEntityValue(e, v!);
                }
                return e;
            }).ToList();
            _tracker = TikChangeTracker.For(new tik4net.Testing.TikFakeConnection());
        }

        [Benchmark(Description = "TakeSnapshot — 1000 FirewallFilter")]
        public int Snapshot()
        {
            foreach (var e in _entities)
                _tracker.TakeSnapshot(e, _metadata);
            return _entities.Count;
        }

        [Benchmark(Description = "GetChanges — 1000 unchanged FirewallFilter")]
        public int Changes()
        {
            int n = 0;
            foreach (var e in _entities)
                n += _tracker.GetChanges(e, _metadata).Count;
            return n;
        }
    }
}
