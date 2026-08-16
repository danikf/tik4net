using System.Linq;
using BenchmarkDotNet.Attributes;
using tik4net.Objects;

namespace tik4net.Benchmarks
{
    /// <summary>
    /// The per-field cost of the accessor, broken down by property SHAPE — 1000 conversions each, so a
    /// column here is directly comparable with one row of <see cref="MapperBenchmarks"/>.
    /// <para>
    /// This exists because the whole-load number cannot say <i>which</i> half is slow, and the two halves
    /// want different fixes: B1 is the accessor call (<c>PropertyInfo</c> vs a bound delegate) and B2 is the
    /// conversion (<c>Enum.GetNames</c> + <c>GetCustomAttribute</c> on every single enum value). A shape that
    /// costs the same as a string is paying for the accessor; one that costs 20× more is paying for the
    /// conversion, and no amount of B1 will touch it.
    /// </para>
    /// </summary>
    [MemoryDiagnoser]
    public class AccessorBenchmarks
    {
        private const int Iterations = 1000;

        /// <summary>Enum with the wire-value attributes a real entity carries.</summary>
        public enum ShapeType
        {
            /// <summary>First member.</summary>
            [TikEnum("accept")] Accept,
            /// <summary>Second member.</summary>
            [TikEnum("drop")] Drop,
            /// <summary>Third member.</summary>
            [TikEnum("passthrough")] Passthrough,
        }

        /// <summary>Flags enum, the shape a `,`-joined router value maps onto.</summary>
        [System.Flags]
        public enum FlagsShapeType
        {
            /// <summary>Nothing set.</summary>
            [TikEnum("")] Empty = 0,
            /// <summary>First flag.</summary>
            [TikEnum("established")] Established = 1,
            /// <summary>Second flag.</summary>
            [TikEnum("related")] Related = 2,
            /// <summary>Third flag.</summary>
            [TikEnum("new")] New = 4,
        }

        [TikEntity("/benchmark/shapes")]
        internal class ShapeEntity
        {
            [TikProperty("text")]
            public string Text { get; set; }

            [TikProperty("number")]
            public int Number { get; set; }

            [TikProperty("flag")]
            public bool? Flag { get; set; }

            [TikProperty("shape")]
            public ShapeType Shape { get; set; }

            [TikProperty("flags-shape")]
            public FlagsShapeType FlagsShape { get; set; }
        }

        private ShapeEntity _entity;
        private TikEntityPropertyAccessor _text, _number, _flag, _shape, _flagsShape;

        /// <summary>Resolves the five accessors once — metadata construction is not what is being measured.</summary>
        [GlobalSetup]
        public void Setup()
        {
            var metadata = TikEntityMetadataCache.GetMetadata<ShapeEntity>();
            _text = metadata.Properties.Single(p => p.PropertyName == "Text");
            _number = metadata.Properties.Single(p => p.PropertyName == "Number");
            _flag = metadata.Properties.Single(p => p.PropertyName == "Flag");
            _shape = metadata.Properties.Single(p => p.PropertyName == "Shape");
            _flagsShape = metadata.Properties.Single(p => p.PropertyName == "FlagsShape");
            _entity = new ShapeEntity();
        }

        /// <summary>string — the accessor call and nothing else.</summary>
        [Benchmark(Baseline = true, Description = "set string ×1000")]
        public void SetString()
        {
            for (int i = 0; i < Iterations; i++)
                _text.SetEntityValue(_entity, "ether1");
        }

        /// <summary>int — accessor call plus a parse.</summary>
        [Benchmark(Description = "set int ×1000")]
        public void SetInt()
        {
            for (int i = 0; i < Iterations; i++)
                _number.SetEntityValue(_entity, "8291");
        }

        /// <summary>bool? — accessor call plus a boxed nullable.</summary>
        [Benchmark(Description = "set bool? ×1000")]
        public void SetNullableBool()
        {
            for (int i = 0; i < Iterations; i++)
                _flag.SetEntityValue(_entity, "yes");
        }

        /// <summary>enum — accessor call plus the reflection scan B2 is about.</summary>
        [Benchmark(Description = "set enum ×1000")]
        public void SetEnum()
        {
            for (int i = 0; i < Iterations; i++)
                _shape.SetEntityValue(_entity, "drop");
        }

        /// <summary>[Flags] enum — the same scan, once per comma-separated part.</summary>
        [Benchmark(Description = "set [Flags] enum ×1000")]
        public void SetFlagsEnum()
        {
            for (int i = 0; i < Iterations; i++)
                _flagsShape.SetEntityValue(_entity, "established,related");
        }

        /// <summary>enum, the other direction — <c>GetRuntimeField</c> + <c>GetCustomAttribute</c> per call.</summary>
        [Benchmark(Description = "get enum ×1000")]
        public int GetEnum()
        {
            int length = 0;
            for (int i = 0; i < Iterations; i++)
                length += _shape.GetEntityValue(_entity).Length;
            return length;
        }
    }
}
