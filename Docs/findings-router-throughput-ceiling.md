# Sustained load hits an aggregate throughput ceiling, and every connection pays

Measured 2026-08-02 against the lab CHR (RouterOS 7.23.2, virtualized) while chasing P2.46, which
was filed as "WinBox native over TCP intermittently stalls under concurrent commands" and attributed
to the P2.1 multiplexer. It is not the multiplexer, and it is not WinBox-specific — the binary API
and the MAC carrier reproduce it on the same router.

> **Correction, same day.** The first version of this document concluded the condition was
> *per-session state on the router*. It is not. That rested on one badly designed measurement — a
> "fresh connection is fast while the aged one is slow" comparison in which the fresh connection did
> only 24 requests, nowhere near the ~200 needed to trip the knee, and did them while the aged
> connection was paused. It could not have come out any other way. The controlled version below says
> the opposite.

## What happens

Requests run at about a **1 ms** round trip until a knee, then jump to **20 ms and worse**, and stay
there while the load continues. Everything recovers fully once the load stops.

## The knee is aggregate, not per connection

Same workload (`getall /interface`, ~1186 B replies), driven over 1, 2 and 4 connections in
parallel, each connection on its own thread:

| connections | own requests before the knee | **combined** requests before the knee | elapsed before the knee | latency after |
|---|---|---|---|---|
| 1 | 216 | **216** | 446 ms | mild, transient |
| 2 | ~66 | **~133** | ~102 ms | 20.1 / 20.1 ms |
| 4 | 31 | **124** | 34 ms | 20.0 / 20.3 / 29.7 / 40.0 ms |

Own request count falls 7×, elapsed time falls 13×, and the **combined count stays in the same
120–220 band**. Adding connections does not buy throughput; it reaches the same wall sooner.

Two further observations point the same way:

- **They cross the knee together.** With two connections both sat at 0.99 ms through request #50 and
  both were at ~20 ms by #75, within the same 50 ms window.
- **One recovers when the other stops.** Connection A returned to 1.45 ms at its request #350 —
  which is where B finished its run. Nothing about A changed.

So "a fresh connection is fast" is only true while the load is off. Spreading bulk work over more
connections makes it worse, not better.

## What it is not

1. **Not the multiplexer, and not correlation.** Every reply that arrived reached the caller that
   asked for it. Timeouts were genuine absence of a reply.
2. **Not the carrier.** `api` (TCP 8728) and `winboxnativemac` (MAC/UDP) show the same curve as
   `winboxnative` (TCP 8291) — three socket types, two independent client implementations.
3. **Not packet loss.** Zero TCP retransmissions across a run that stalled (host `netstat -s` delta).
4. **Not the client.** Our own process burns 5–37 % of a core during the degraded phase; it is
   waiting, not working. (It does hit ~93 % briefly during the fast burst *before* the knee.)

## What it looks like

The shape is a **burst allowance followed by a hard clamp, with full recovery after idle**: a fixed
budget of roughly 120–220 requests drains at whatever rate you ask for, then service time snaps to a
remarkably constant ~20 ms and aggregate throughput collapses from ~1000 req/s to ~130 req/s. That
is the signature of a token-bucket or credit-based limiter, not of anything in the protocol.

A hypervisor CPU limit or reservation on the router VM fits it precisely, and the router's own
`cpu-load` reading does **not** argue against it: it reported `0` throughout, including while
serving ~1000 requests/s, and a guest cannot see time its hypervisor took away from it. RouterOS
queueing or a rate limiter on the management path would fit the same shape.

**Which one it is has not been established** — that needs the hypervisor's view of the VM, or
router-side profiling. Whether a non-virtualized router behaves the same is likewise untested; there
is one router in the lab.

## Why it surfaced as a WinBox-native-over-TCP stall

TCP has head-of-line blocking and the MAC carrier does not. Once service time is clamped, WinBox
native over TCP goes lumpy rather than merely slow: six concurrent requests go out, the reader sees
**nothing for seven seconds**, and then all six replies land within 1.2 ms. One delayed reply holds
every reply queued behind it in the byte stream. Worst observed: three requests timing out together
at the 30 s per-request deadline.

The MAC carrier acknowledges each message independently, so the same clamp shows up there as a
smooth 10× slowdown with no cliff and no timeout. That asymmetry is what made the original P2.46
note record the MAC transport as unaffected.

## What this means for tik4net

- **Nothing to fix in the multiplexer.**
- **Pace bulk work; do not parallelize it.** More connections reach the ceiling sooner and then
  queue behind each other. A pause lets the budget refill.
- **The per-request timeout has to survive a multi-second stall.** 30 s is not generous here; it was
  exceeded during this investigation.
- `WinboxTcpTransport` sets `NoDelay` (as `TelnetClient` always has). An A/B over six runs and ~950
  round trips found **no significant difference** — protocol hygiene, not a fix for any of this.

## Reproducing

`tik4net.integrationtests/P246StallProbe.cs`, skipped unless `TIK_PROBE=1`.

- `Probe_TwoConnections_WhereDoesEachKneeFall` — the aggregate-vs-per-session measurement.
  `TIK_PROBE_CONNS=n` sets the parallel connection count; a separate sampler connection records the
  router's `cpu-load`/`free-memory` and our own process's CPU alongside every request's latency.
- `Probe_SustainedLoad_MeasureRoundTripDegradation` — the single-connection workload, with a merged
  trace of the reader thread (`ITikWireTraceSink`, true arrival) and the calling threads
  (`OnReadRow`/`OnWriteRow`, hand-off). Keeping both clocks is the point: the row hooks fire on the
  caller's thread after it wakes, so on their own they cannot tell a slow router from a client that
  was not scheduled. Knobs: `TIK_PROBE_SERIAL=1`, `TIK_PROBE_OP=identity|ipaddress`,
  `TIK_PROBE_ITERS=n`.

Run either under any transport's `.runsettings` — comparing transports is how the carrier was ruled
out.

## A note on method

Both wrong turns here were measurement design, not reasoning about the router. The first trace dated
replies on the caller's thread, which cannot distinguish a slow router from an unscheduled client.
The first "per-session" test compared a connection past its knee against one that had not reached
it. Neither error was visible in its own output — both produced clean numbers that supported a wrong
conclusion. When a measurement confirms the hypothesis, the thing to check is whether it could have
come out otherwise.
