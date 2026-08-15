# winbox-m2-multiplexing-design.md — investigation history

Superseded diagnoses, incidents and pinned measurements for the M2 channel and request/reply correlation model.
The current behaviour is in [winbox-m2-multiplexing-design.md](winbox-m2-multiplexing-design.md); this file is history and describes
nothing about how the code works today.

Indexed from [HISTORY.md](HISTORY.md), which carries the transferable lessons.

---

## The MAC transport's expected obstacle to multiplexing was not the real one

**Design-time worry:** `MacLayerTransport` sends ACKs and PONGs from inside its own receive path.
The original design reasoned that giving the MAC channel a background reader loop would therefore
have the transport writing from two threads at once — send-side application traffic and
receive-side ACK/PONG replies — and treated that as the reason to ship TCP multiplexing first and
defer the MAC variant to a separate follow-up with its own live verification.

**What the follow-up actually found:** the write side needed no change at all. Every send (`Send`,
`SendAck`, `SendPong`, `RetransmitIfUnacked`) already went through `MacLayerTransport.SendGate`,
which MAC-Telnet had needed since it grew its own background receive pump — a reader loop for WinBox
native M2 added no writer that gate did not already cover.

**The real blocker was one layer below, in the retransmit buffer.** `MacLayerTransport` held exactly
one outstanding DATA packet (`_lastDataPacket`), which is sufficient only while every caller is
lockstep. The MAC-layer counter is a cumulative byte offset, so with two requests in flight and the
first one lost, the router can acknowledge neither — the missing packet is precisely the one a
single slot has already overwritten with the second. Not a slow round trip: a permanent stall. The
fix replaced the single slot with a queue of unacknowledged packets, retransmitting the oldest (the
hole, by construction) and retiring everything a cumulative ACK covers; with only one request in
flight the behaviour is unchanged from before.

This is now the current design, documented as such in `findings-mactelnet.md` §9.2 and in
`winbox-m2-multiplexing-design.md` §11. It is recorded here as a diagnosis worth remembering: the
worry that looked most obviously true (concurrent writers) was already handled, and the actual
failure mode was structurally different — a resource sized for one caller, discovered by reasoning
about what a lost packet plus a second in-flight request implies, not by reproducing it against a
live router (the lab router does not drop packets on demand; deterministic coverage for the queue
rules lives in `tik4net.unittests/MacTelnet/MacLayerRetransmitTests.cs` over loopback UDP for
exactly that reason).

## The design's placement of the reader loop was wrong on the first pass

The design originally specified "`WinboxM2Session` → reader loop + `_pending` registry", i.e.
building the multiplexer directly into the shared session classes. That would have broken the
WinBox-CLI transports: `WinboxM2Session` and `WinboxMacM2Session` are shared by two consumers with
incompatible read models — native M2 (every reply carries a request id) and the mepty terminal
(`WinboxCliClient`, which polls for and reads *unsolicited* terminal-output frames carrying no
request id at all). A reader loop installed inside the session would have consumed those terminal
frames and handed them to the unmatched-frame path — silently swallowing all CLI output.

The corrected design put multiplexing one layer above the channel instead: a new
`WinboxM2Multiplexer` wraps an `IWinboxM2Channel` and is constructed only by
`WinboxNativeM2Operations`. `WinboxCliClient` keeps reading the channel directly, unchanged — zero
risk added to the CLI transports, which matter more here than usual because they are
reverse-engineered code with no deterministic coverage of their own. This is the shape that shipped
and is documented in the current file.

## The crypto risk was the one that mattered, and it came back clean

Before any implementation work, three facts were checked live rather than assumed, because any one
of them failing would have closed off multiplexing entirely. The correlation field (`0xFF0006`) and
the "no unsolicited frames today" property were expected results, confirmed as expected. The crypto
was the one real unknown: had `WinboxStreamCrypto` turned out to be a genuine running stream cipher
(cross-frame state, a counter, a replay window) rather than what its name suggested, multiplexing
would have required redesigning the crypto layer first. Live inspection showed a fresh random IV per
frame and no cross-frame state at all — frames decrypt and complete independently regardless of send
order. This is the finding the rest of the design rests on, and it is now stated directly as the
current design's invariant rather than as a risk that got resolved.

## Test-harness decision: build `FakeWinboxServer` first

The design's open question — build a loopback `FakeWinboxServer` before writing the multiplexing
code, or verify live-only — was resolved in favour of building it first, and the harness landed the
same day the question was raised. It turned out cheaper than the design's original proposal (a
"fixed post-handshake key pair" test seam): answering the EC-SRP5 hello with a non-`0x06` frame tag
makes the real client fall back to legacy MD5 auth on its own, which is scriptable with no crypto
material and no production code changes — and it incidentally covered the auth-fallback path, which
had had zero tests before. The known gap at the time (the legacy path is unencrypted, so the AES
frame path itself was still uncovered by the harness) was closed later: `WinboxM2MultiplexerTests`
and `WinboxM2PaginationTests` both drive real encrypted sessions through `FakeWinboxServer`.

## Open questions, as resolved

- **`FakeWinboxServer` first, or live-only?** Built first (see above); `WinboxM2SessionProtocolTests`
  landed alongside it the same day.
- **Does `0xFE0019` mean "more frames follow"?** Resolved: no. Reading the router's own webfig
  client source showed the field stored into an `objCount` that is never read for flow control
  anywhere; the completion rule (one request → one reply frame, matched by `0xFF0006`) needed no
  change. A stricter read of the same source later corrected an incomplete transcription: webfig
  also continues on a second, message-array-typed continuation token (`mfe0015`) alongside the
  scalar one (`ufe0003`) already handled — a real client-side gap (no known live handler paginates
  that way, so it was invisible without scripting it), closed by `WinboxM2PaginationTests`.
- **Should `CancelInFlight` ship with multiplexing or after?** Shipped together. M2 has no cancel
  verb for an arbitrary in-flight ordinary round trip, but abandoning a local registration is safe
  precisely because id dispatch means the late reply is identifiable and droppable — the same
  guarantee the rest of the design exists to provide, so there was no reason to gate the capability
  flag on a separate change.

## Status note (superseded)

The document originally carried a preamble marking it "Status: proposal", written against a
specific commit, with a stated prerequisite (the async query-path conversion landing first, since
there is nothing to multiplex while every call is a blocking lockstep exchange). All of that
prerequisite work has since landed, the multiplexer is implemented and tested as described above,
and the design document was converted from a proposal to a description of the shipped behaviour.
