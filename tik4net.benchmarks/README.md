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
| `MapperBenchmarks` | What a caller pays. `LoadAll<FirewallFilter>` over 1000 rows (read path: `SetEntityValue` + `ConvertFromString`) and `GetEntityValue` over 1000 entities × every property (write path). |
| `AccessorBenchmarks` | Where that cost is. The same accessor call per property *shape* — string, int, `bool?`, enum, `[Flags]` enum — 1000 conversions each, ratioed against string. |

The split matters because the whole-load number cannot say which half is slow, and the two halves want
different fixes: the accessor **call** (B1, a bound delegate instead of `PropertyInfo`) and the **conversion**
(B2, caching the enum maps). `AccessorBenchmarks` is what tells them apart — a shape costing about what a
string costs is paying for the accessor, and one costing hundreds of times more is paying for the conversion.

## Measured so far

`FirewallFilter` is the subject because it is the shape the mapper is slowest on and the one a caller loads
in bulk: ~50 mapped properties, a plain enum, a `[Flags]` enum, nullable bools, `long` counters, and an `.id`
with a private setter.

**B1 — compiled accessors (2026-08-16, net8.0, default job).** Load of 1000 rows 21.33 ms → 20.76 ms,
serialize 4.71 ms → 4.40 ms. Real, and small: **~3% and ~7%**, nowhere near the ≥5× the plan targets.

`AccessorBenchmarks` says why, and it is not subtle — per 1000 conversions:

| Shape | Mean | vs string |
|---|---:|---:|
| set string | 7.95 µs | 1.0× |
| set int | 13.15 µs | 1.7× |
| set `bool?` | 15.07 µs | 1.9× |
| get enum | 1,336 µs | **168×** |
| set enum | 3,380 µs | **425×** |
| set `[Flags]` enum | 3,938 µs | **495×** |

An enum conversion runs `Enum.GetNames` plus a `GetRuntimeField` + `GetCustomAttribute<TikEnumAttribute>`
for **every member on every value converted**, and the `[Flags]` path does it once per comma-separated part.
That is the mapper's cost, and no amount of B1 touches it — the accessor call it replaces is the 8 µs column.
**The ≥5× target belongs to B2.**
