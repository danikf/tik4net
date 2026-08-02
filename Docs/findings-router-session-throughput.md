# A router session slows down ~30× under sustained load, on every transport

Measured 2026-08-02 against the lab CHR (RouterOS 7.23.2, Hyper-V) while chasing P2.46, which was
filed as "WinBox native over TCP intermittently stalls under concurrent commands" and attributed to
the P2.1 multiplexer. **It is not the multiplexer, and it is not WinBox-specific.** The binary API
and the MAC carrier reproduce the same curve on the same router, so the write-up lives here rather
than in a per-transport findings file.

## What happens

A connection that has been issuing back-to-back request/response commands degrades from a **0.45 ms**
wire round trip to **15 ms** — sharply, at a knee, and then stably. It does not recover on its own
while the load continues. It recovers **completely** after roughly five seconds of idle.

Wire round trip on WinBox native over TCP, measured on the reader thread (`wbxtcp.frame` trace,
`Send` emit → matching `Recv` emit), one connection, serial `getall /interface`:

| phase | n | median | max |
|---|---|---|---|
| fresh session | 192 | **0.45 ms** | 0.87 ms |
| after ~190 getalls | 259 | **15.19 ms** | 19.41 ms |
| after a 5 s pause | 48 | **0.43 ms** | 0.87 ms |

## It is per-session state on the router

Four measurements, each ruling something out:

1. **A connection opened *during* the degradation is immediately fast.** 24 reads took 29 ms on a
   fresh connection while the aged one needed 209 ms for the identical 24 reads, at the same moment.
   That rules out a router-wide condition, host-wide state, CPU saturation and the network.
2. **Zero TCP retransmissions** across a run that stalled (host `netstat -s` delta). Nothing is
   being lost or backed off.
3. **Five seconds of idle restores it fully** (209 ms → 27 ms for the same 24 reads on the *same*
   connection). So nothing is accumulating without bound — it is a load-dependent condition.
4. **Every transport shows it**, including one that is not TCP at all:

   | transport | carrier | fresh | degraded | after 5 s idle |
   |---|---|---|---|---|
   | `winboxnative` | TCP 8291 | 25–29 ms / 24 reads | 209 ms + multi-second cliffs | 27 ms |
   | `api` | TCP 8728 | 25 ms / 24 reads | 199 ms | 76 ms |
   | `winboxnativemac` | MAC/UDP | 93–115 ms / round | ~1100 ms / round | 116 ms |

   Three different socket types, two entirely independent client implementations (`ApiConnection`
   predates the WinBox work by years), one shared curve.

## The knee tracks reply bytes, not request count

Same connection, same command, different tables:

| operation | reply size | requests before the knee | ≈ bytes at the knee |
|---|---|---|---|
| `getall /interface` | 1186 B | ~190 | ~225 kB |
| `getall /ip/address` | 194 B | ~1080 | ~210 kB |
| `get /system/identity` (singleton) | ~50 B | none in 960 | — |

And after the knee the added latency scales with the reply too: on the degraded session an
interface getall costs ~15 ms while an identity get costs ~1.7 ms. So the cost is per byte
delivered, not per request served — which is also why the singleton read never trips it.

## Why it looked like a WinBox-TCP stall

Because TCP has head-of-line blocking and the MAC carrier does not. On WinBox native over TCP the
degradation surfaces as a cliff: six concurrent requests go out, the reader sees **nothing for
seven seconds**, and then all six replies land within 1.2 ms of each other. One delayed reply holds
every reply queued behind it in the byte stream. Worst observed: three requests timing out together
at the 30 s per-request deadline.

The MAC carrier acknowledges each message independently, so the same condition shows up there as a
smooth 10× slowdown (100 ms → 1100 ms per round) with no cliff and no timeout. That asymmetry is
what made the original P2.46 note conclude the MAC transport was unaffected — it is affected, it
just cannot produce a cliff.

## What this means for tik4net

- **There is nothing to fix in the multiplexer.** Request/reply correlation was correct throughout:
  every reply that arrived went to the caller that asked for it, and the timeouts were genuine
  absence of a reply, not misrouting.
- **A hot loop on one long-lived connection is the worst way to drive a router.** Consumers doing
  bulk work should expect the 30× cliff, and either pace themselves or spread work over
  connections. A fresh connection is fast immediately.
- **The per-request timeout has to survive a multi-second stall.** 30 s is not generous here; it was
  exceeded during this investigation.
- `WinboxTcpTransport` sets `NoDelay` (as `TelnetClient` always has). An A/B over six runs and ~950
  round trips found **no significant difference** — it is protocol hygiene for a request/response
  channel, not a fix for any of the above.

## Reproducing

`tik4net.integrationtests/P246StallProbe.cs`, which is skipped unless `TIK_PROBE=1` is set. It
drives the workload, records a merged trace of the reader thread (`ITikWireTraceSink`, true arrival)
and the calling threads (`OnReadRow`/`OnWriteRow`, hand-off) and writes it to `TIK_PROBE_DIR`.
Keeping both clocks is the point: the row hooks fire on the caller's thread after it wakes, so on
their own they cannot tell a slow router from a client that was not scheduled.

Knobs: `TIK_PROBE_SERIAL=1` (one worker, same request count), `TIK_PROBE_OP=identity|ipaddress`
(reply size), `TIK_PROBE_ITERS=n`. Run it under any transport's `.runsettings` — comparing them is
how this was settled.

## Not established

Which router-side mechanism this is. It is per-session, load-dependent, byte-proportional and
clears on idle; that is as far as black-box measurement from the client goes. Confirming the
mechanism needs router-side profiling or an admin-privileged packet capture, neither of which was
available. Whether it is specific to CHR under Hyper-V is likewise untested — there is one router
in the lab.
