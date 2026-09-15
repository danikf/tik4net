# Sustained load hits an aggregate throughput ceiling, and every connection pays

Measured against the lab CHR (RouterOS 7.23.2, virtualized). The ceiling is a property of the
**router**, not of any one transport or of our multiplexer: the binary API and the MAC carrier
reproduce it on the same box, and it is not WinBox-specific.

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

**For the single-large-read stall it was established, and it was the hypervisor**: the lab VM had 16
vCPUs on a laptop host, and two removed the stall outright — see
[It was the lab VM's vCPU count](#it-was-the-lab-vms-vcpu-count) below. The sustained-load clamp
measured here has **not** been re-measured since that change, so whether it is the same cause is open;
the shape fits. Whether a non-virtualized router behaves the same is likewise untested; there
is one router in the lab.

## Why it surfaced as a WinBox-native-over-TCP stall

TCP has head-of-line blocking and the MAC carrier does not. Once service time is clamped, WinBox
native over TCP goes lumpy rather than merely slow: six concurrent requests go out, the reader sees
**nothing for seven seconds**, and then all six replies land within 1.2 ms. One delayed reply holds
every reply queued behind it in the byte stream. Worst observed: three requests timing out together
at the 30 s per-request deadline.

The MAC carrier acknowledges each message independently, so the same clamp shows up there as a
smooth 10× slowdown with no cliff and no timeout. That asymmetry makes the MAC transport look
unaffected unless it is measured against a request count rather than a wall-clock deadline.

## A single large read stalls the same way, and the session does not come back

The API shows the clamp's other face: not sustained load, but **one** big read. Measured on the same
CHR against `/queue/tree` (681 rows, ~260 KB, ~2500 sentences), four arms of 6-8 reads each —
`tik4net.integrationtests/ApiLargeReadStallProbe.cs`.

- A read that succeeds takes **~2000 ms**, remarkably constant across arms and runs. The occasional
  read takes **55-90 ms** — the same rows, the same table, 30x faster. Both are common; there is no
  third speed.
- Roughly one read in six to eight **stops mid-answer**: sentences arrive, then nothing at all for
  30 s, and the caller times out. Where it stops varies (15 to 605 sentences in).
- The socket is not the explanation. At the timeout the OS buffer holds **0 unread bytes** and the
  byte counter has not moved for the full 30 s, so the reader is not sitting on data it failed to
  collect; a reader stuck mid-word would still show a climbing byte total, because partial word
  bodies are counted as they are read.
- **The session keeps hearing us.** A `/system/identity/set` issued on the stalled connection times
  out like everything else, and the router applies it and logs it. A second, fresh connection reads
  the same table in 55-90 ms throughout.
- **The session usually does not recover.** In most runs nothing further ever arrives, through a
  second 30 s window and beyond, and the connection has to be discarded.

### It is a pause, not a death

One run's abandoned read **resumed**: 77 further sentences of the timed-out tag's reply turned up
later and sat unclaimed while the next command waited. 605 + 77 sentences is the complete answer, so
the router had gone quiet for more than 30 s in the middle of a reply and then finished it.

That single observation constrains everything above. Sentences cannot arrive on a session the router
has stopped serving, so "the session is dead" is at best not always true, and a 30 s deadline is
sometimes what turns a long pause into a failure. **The stall is a delivery gap of unbounded length,
not a proven death.**

### It was the lab VM's vCPU count

**Sixteen vCPUs on a laptop host. Dropping the CHR to two removed the stall.** 40 reads across all five
arms, zero stalls, where the same probe had been wedging about one read in six to eight — a ~0.2 %
outcome if nothing had changed.

The mechanism did not disappear, it shrank: one read in the clean run took **18.3 s** and completed.
That is the same pause, now landing inside the 30 s budget instead of past it. A wide VM is harder for
the hypervisor to place, and while it waits, nothing inside it runs — including the timers that would
retransmit.

**The vCPU count alone, not the VM image.** The two-vCPU lab is a copy of the original VM, so the first
result changed both at once. Setting that same copy back to sixteen vCPUs and running 20 WinBox M2 reads
of the 1672-row table (`Probe_MangleRead_TcpSocketLevel`) brings the stall back. The first nine reads
after boot are clean (1.5–3.6 s). From the tenth on, socket-level silences of 27 s, 47 s and 52 s appear,
and one read times out at 120 s with no byte arriving. Back at two vCPUs, the same image is clean.

Ruled out along the way, each by measurement rather than argument:

- **Not the network path.** Moving the whole conversation onto an internal vSwitch — host to VM, no
  bridge, no Wi-Fi, no USB NIC — reproduced the stall on the second read, and stalled the diagnosis's
  *second, fresh* connection at the same moment. Two sessions going quiet together is a router-wide
  pause, not a lost packet.
- **Not tags, the dispatcher, or connection state.** See the arms above.
- **The lost segment was a symptom.** The capture below is real, but a stack that is not being run for
  tens of seconds drops packets; the loss does not explain the pause, the pause explains the loss.

The slow read speed is the router's per-field formatting, not client work. A raw Python API client,
with no tik4net involved, reads the 1672-row mangle table in 4.1–5.1 s (2.5–3.0 ms per row). That is the
same slope tik4net shows. Restricted with `.proplist=.id`, the same read takes 0.4 s, and with
`.proplist=chain,action` 0.8–1.5 s. Rows arrive in batches: gaps are ~10 µs at the median, with pauses of
20–80 ms at p99. Cost grows with the number of fields per row, so `.proplist`, which the O/R mapper
already sends, is the lever a caller has.

### What the router's own byte counters do and do not say

The router's `/ip/firewall/connection` row for the stalled session (matched by the `address:port`
the exception now names) counted **1,577,714** bytes in the reply direction where the client socket
had received **1,457,297** — and the router-side count did not move over the following 5 s.

That 120 KB gap is **not** evidence of loss, tempting as it looks. Conntrack counts whole frames and
the client counts payload, and the API writes a packet per sentence: ~3000 packets x 40 bytes of
IP+TCP header is the entire difference. Read it as "the two agree", and note that it also means the
router was not retransmitting during the silence. A host-side `netstat -s` delta across an earlier
stalled run likewise showed **zero TCP retransmissions**.

That left router-side starvation and loss-on-the-way as the two candidates, which the vCPU result
above then settled in favour of the first. The packet capture is what made the second testable, and
`ApiLargeReadStallProbe` still runs it: the router's own sniffer, headers only, stopped at the first
stall and dumped around the **longest silence** on each session, so the stalled session identifies
itself.

Two things are ruled out. **Not tag collision or misrouting**: tags are unique per process and per
run (`prefix-pid-stamp-counter`), each connection reads only its own socket, and at the stall no
sentences are held unclaimed for any tag. **Not connection state**: fresh, reused, 300-commands-old
and monitor-carrying connections all stall, at rates that do not separate.

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

`tik4net.integrationtests/ApiLargeReadStallProbe.cs`, same gate, is the single-large-read half:
four connection-state arms plus one with a 300 s budget, `TIK4NET_STALL_READS` reads each
(default 12) over `TIK4NET_STALL_PATH` (default `/queue/tree` — it needs a big table). On the first
stall it diagnoses the dead connection in place: a fresh connection reading the same table, a write
issued on the stalled session, and the router's `/ip/firewall/connection` rows sampled twice five
seconds apart.

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
