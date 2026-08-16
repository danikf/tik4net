# tik4net.benchmarks

BenchmarkDotNet harness for the O/R mapper's per-row cost. **Not shipped** (`IsPackable=false`), not run by
CI, and it needs no router — every row is a fake sentence built in `[GlobalSetup]`.

It exists so that a performance claim about the mapper is a measurement rather than an argument. Run it
**before** the change as well as after: a number without a matched baseline from the same machine says
nothing, and the noise between two runs is larger than several of the wins being chased.

```bash
dotnet run -c Release --project tik4net.benchmarks -- --filter "*"
```

Release is mandatory — BenchmarkDotNet refuses to run a Debug build. Add `--job short` for a quick look
while iterating, but **do not quote a ShortRun number**: three iterations put the error bar above the effect
being measured (a ShortRun of the load benchmark has been seen at both 20 ms and 41 ms for identical code).

## What is measured

| Class | Question it answers |
|---|---|
| `MapperBenchmarks` | What a caller pays. `LoadAll<FirewallFilter>` over 1000 rows (read path), the same 1000 rows through `SetEntityValue` alone (the mapper's half of that load, mirroring `CreateObject` field for field), and `GetEntityValue` over 1000 entities × every property (write path). The gap between the first two is the part of a load that is not the mapper. |
| `AccessorBenchmarks` | Where that cost is. The same accessor call per property *shape* — string, int, `bool?`, enum, `[Flags]` enum — 1000 conversions each, ratioed against string. |

The split matters because the whole-load number cannot say which half is slow, and the two halves want
different fixes: the accessor **call** (B1, a bound delegate instead of `PropertyInfo`) and the **conversion**
(B2, caching the enum maps). `AccessorBenchmarks` is what tells them apart — a shape costing about what a
string costs is paying for the accessor, and one costing hundreds of times more is paying for the conversion.

## Measured so far

`FirewallFilter` is the subject because it is the shape the mapper is slowest on and the one a caller loads
in bulk: ~50 mapped properties, a plain enum, a `[Flags]` enum, nullable bools, `long` counters, and an `.id`
with a private setter.

**B1 (compiled accessors) + B2 (cached enum tables), 2026-08-16, net8.0, default job.** All three builds
measured back to back on one machine with this harness:

| | pre-B1 | after B1 | after B2 | total |
|---|---:|---:|---:|---:|
| `LoadAll` — 1000 rows | 58.26 ms | 57.11 ms | **10.75 ms** | **5.4×** |
| ↳ of which the mapper (`SetEntityValue`) | 37.95 ms | 37.33 ms | **3.04 ms** | **12.5×** |
| ↳ of which everything else | 20.3 ms | 19.8 ms | 7.7 ms | — |
| `GetEntityValue` — 1000 entities | 12.88 ms | 12.06 ms | **1.36 ms** | **9.5×** |
| allocated per load | 14.76 MB | 14.72 MB | **6.38 MB** | −57% |

**B1 on its own is worth ~2%.** B2 is the whole win, and `AccessorBenchmarks` is what said so in advance —
per 1000 conversions, before and after, each ratio taken **within** its own run:

| Shape | before | after |
|---|---:|---:|
| set string | 1.0× | 1.0× |
| set `bool?` | 1.9× | 2.8× |
| get enum | **168×** | 3.5× |
| set enum | **425×** | 3.6× |
| set `[Flags]` enum | **495×** | 23× |

Every enum conversion used to run `Enum.GetNames` plus a `GetRuntimeField` + `GetCustomAttribute` for
**every member on every value converted** — `Enum.GetNames` alone allocating a fresh array each time — and
the `[Flags]` path did it once per comma-separated part. B1 had replaced the 1.0× column.

The load did not improve as far as the mapper did because **7.7 ms of it is not the mapper**: command
dispatch, sentence lookups, list building. Nothing in Track B reaches that, and it is now 72% of a load.

### Two cautions this exercise earned

- **Absolute numbers from different harness versions are not comparable.** The first B1 measurements read
  21.33 → 20.76 ms, which looks nothing like the 58.26 → 57.11 above — same code, same conclusion (~2–3%),
  different harness: `Materialize_1000Rows` was added in between and retains the 1000 source rows, so every
  GC during the load has more live data to trace. That penalizes the allocation-heavy old code far more than
  the new one. Always re-measure the baseline with the harness you are going to quote.
- **This machine drifts between runs by well over the effect size of a small change** — the same benchmark
  has read 7.95 µs and 12.98 µs for identical code minutes apart. Run the builds being compared back to back,
  and prefer a ratio taken inside a single run to a difference taken across two.
