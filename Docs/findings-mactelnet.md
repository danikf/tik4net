# MAC-Telnet (UDP 20561) — transport behaviour

How the MAC-Telnet transport works and why it is built the way it is. Complements the wire-protocol
reference [mactelnet-protocol.md](mactelnet-protocol.md) and the shared CLI findings
[findings-cli.md](findings-cli.md).

> **Principle:** MAC-Telnet is a **CLI transport** (like Telnet/SSH), **not** an API protocol. After
> EC-SRP5 authentication it streams a **raw VT100 terminal** over UDP (unencrypted). CRUD goes through
> `CliConnectionBase` → `:put [/path print … as-value]` and text parsing, **identically to Telnet**.
> No binary `!re` sentences; `.id` comes from `as-value` output, not from the API.

---

## Architecture

```
tik4net/MacTelnet/
├── MacLayerTransport.cs    public abstract — UDP 22-byte framing, EC-SRP5 auth, ACK/dedup, MNDP
├── MacTelnetUdpClient.cs   internal sealed — VT100 terminal: login + commands (fully synchronous)
└── MacTelnetConnection.cs  public sealed  — CliConnectionBase wrapper (ITikConnection)
```

- `MacTelnetConnection : CliConnectionBase` — `ExecuteCliCommandCoreAsync` delegates to
  `MacTelnetUdpClient.SendCommandAndReadAsync`. The `RouterMac` property bypasses MNDP (otherwise
  ~5 s of discovery).
- Crypto lives in `tik4net/Crypto/` (`EcSrp5`, `WinboxStreamCrypto`); VT100 and stripping live in
  `tik4net/Cli/` (`Vt100State`, `VtStripper`, `RouterOsCliLogin`, `CliOutputHelper`, `CliOutputParser`).
- `MacLayerTransport` is **public** on purpose — `WinboxMacClient` in another assembly also derives
  from it.
- `MacTelnetUdpClient` is **fully synchronous** (`UdpClient.ReceiveTimeout` plus a blocking `Receive`
  on a 500 ms poll), wrapped in `Task.Run`. Mixing sync and async `Receive` on .NET Framework 4.8
  breaks `SO_RCVTIMEO`, so the receive path must not be made async piecemeal.

---

## 1. The counter is a cumulative byte offset — ACK past the packet

**The counter in a MAC-Telnet packet is a cumulative byte offset into the stream, not a packet
sequence number.** After receiving a DATA packet you must acknowledge the offset **past** it:

```
ACK.counter = pkt.counter + payload.Length
```

Live sniff showing the rule: `ctr=0(len58) → 58 → 58(len9) → 67 → 67(len24) → 91 → 91(len39) → 130`.
Each counter equals the previous counter plus the previous length.

```csharp
protected bool AckData(uint counter, int payloadLen)
{
    SendAck(counter + (uint)payloadLen);        // confirm the offset PAST the packet
    if (counter < _inCounter) return false;     // retransmission → do NOT reprocess
    _inCounter = counter + (uint)payloadLen;
    return true;
}
```

- `_inCounter` (cumulative bytes received) is reset in `BaseConnect`.
- **Deduplication is mandatory.** Without it a retransmitted packet is fed into `Vt100State` twice
  (throwing off the cursor) and appended to the output buffer twice (duplicate or corrupted records).
  The protocol requires it: the client ignores packets with counter ≤ incounter
  ([mactelnet-protocol.md](mactelnet-protocol.md)).
- `AckData` is used in **every** receive loop: `Authenticate`/`FinishAuth`, `WaitForPromptSync`,
  `ReadCommandResponseSync`, `DrainSync`.

**Acknowledging the bare counter has a far-reaching failure mode**, which is why this is stated first.
RouterOS treats the packet as undelivered and retransmits it indefinitely *while coalescing*
subsequent terminal updates into one packet. That derails the cursor-probe width negotiation (§2), so
the router measures a terminal about 3 columns wide and inserts `\r\n` roughly every 3 characters
(`you\r\nr p\r\nass\r\nwor\r\nd …`). Prompt and nag detection then never match, and the visible symptom
is a login that times out waiting for a shell prompt — several layers away from the cause.

---

## 2. VT100 cursor-probe width negotiation is mandatory, and the width must be large

After authentication RouterOS **measures the terminal** with a sequence of cursor moves plus `ESC[6n`
(DSR). The client must answer with the cursor position `ESC[row;colR` (handled by `Vt100State`).
Without answers the router assumes 1×1 and renders nothing.

| Sequence | Meaning | Our response |
|---|---|---|
| `ESC Z` | DECID | `ESC[?1;0c` |
| `ESC[6n` | DSR (position query) | `ESC[{Row};{Col}R` |
| `ESC[H` … `ESC[9999C` … `ESC[6n` | **width measurement** (go as far right as possible, where are you?) | `ESC[1;{min(Width,10000)}R` |
| `ESC[9999B` / `ESC[9999A` / `ESC D` / `ESC[r` | height / scroll-region measurement | tracking Row |
| `ESC[H ě H ESC[6n` | UTF-8 test (multi-byte character = 1 column) | `ESC[1;3R` |

**The measuring probe is `ESC[9999C`**, so the reported column is `min(Vt100State.Width, ~1+9999)`.
`Width` must therefore be **≥ 10000**, or `Vt100State` truncates its own answer, the router measures a
narrow terminal, long `as-value` lines wrap, and `\r\n` lands inside the data. Production uses
`Vt100State(65535, 25)`. See [findings-cli.md](findings-cli.md) §6.

`CTRL_TERM_WIDTH` in the auth exchange (`(ushort)80`, little-endian) is **ignored** by RouterOS once
the cursor-probe has run — it uses the measured width. It does not block login and is left at 80.

---

## 3. Keep the receive loop cheap

Cursor-probe responses are time-sensitive: the receive loop must stay free of expensive work. Per-packet
logging with hex formatting, `StripAnsi` over a growing buffer, and substring operations is enough to
delay responses, so production code carries no verbose diagnostics in the loop.

**To debug without touching production code**, subclass the public `MacLayerTransport` in a test project
and collect an in-memory hex dump there. Both test assemblies can see `internal` types
(`InternalsVisibleTo("tik4net.integrationtests")` and `("tik4net.unittests")`, in
`tik4net/Properties/AssemblyInfo.cs`). Delete the debug subclass when finished. For byte-level tracing
that ships, use `TikWireTrace` and the `mactelnet.udp` channel instead.

---

## 4. Login sequence (`MacTelnetUdpClient.LoginAsync`)

```
BaseConnect(host, CLIENT_TYPE=0x0015)   // NIC selection, MNDP/MAC override, socket bind
StartSession()                          // SESSIONSTART, acknowledged (§9.1)
Authenticate(user, pass)                // EC-SRP5, ends with END_AUTH
WaitForPromptSync()                     // answers cursor probes; Ctrl-C on nag; waits for "] >"
DrainSync(250)                          // drain leftover redraw so it can't leak into the 1st command
```

- **Change-password nag**: a router with an empty password shows `new password>`. Answer with
  **Ctrl-C (0x03)**, clear the buffer, continue. Detected by `RouterOsCliLogin.IsChangePasswordNag`
  (substring `password>`). See [findings-cli.md](findings-cli.md) §4.
- **Prompt**: `RouterOsCliLogin.IsShellPrompt` = `TrimEnd().EndsWith("] >")`.
- **`END_AUTH` does not mean the login succeeded** — see §8.

## 5. Executing a command

```
cmd = CliOutputHelper.InjectWithoutPaging(command)   // "without-paging" after "print"
Send(PKT_DATA, cmd + "\r")
raw = ReadCommandResponseSync()                      // prompt + 150 ms silence (settle), as Telnet
return CliOutputHelper.CleanOutput(VtStripper.StripAnsi(raw), cmd)  // strip echo + trailing prompt
```

The settle logic matches `TelnetClient.ReadCommandResponseAsync`: prompt at the end plus `SettleMs` of
silence, with a prompt redraw before the output resetting the settle window. See
[findings-cli.md](findings-cli.md) §7.

## 6. MAC-Telnet echoes a command twice

Lines are terminated with a bare `\r`, and a typed command comes back **twice**: once as the character
echo on its own line, then again as a line-editor redraw in the form `<prompt> <command>`. Telnet
(`\r\n`) produces only one echo.

```
:put [/interface print … as-value]\r[admin@MikroTik] > :put [/interface print … as-value]\r\n
.id=*2;…;name=ether1;…;.id=*1;…;name=lo;…\r\n
\r\r\r[admin@MikroTik] >
```

`CliOutputHelper.CleanOutput` therefore removes **all** leading blank and echo lines, not just the
first. A line counts as echo or noise when it either (a) contains the prompt `] >`, or (b) is a
fragment of the sent command (`cmdCore.Contains(line)` / `cmdCore.StartsWith(line)`). A data line
(`.id=…`, a bare `*N`, or an error) matches neither, so the loop stops there.

Rule (b) also covers **multi-line commands**: `/system/script/add` with a `source` containing `\n` is
echoed across several lines, and a leftover fragment would otherwise merge into the first record or be
returned as the new row's id from `RunAdd`. Removing only the first echo line leaves
`[admin@…] > :put […]` to merge into the first record inside `ParseAsValue` (which normalises `\r\n` to
`;`), and the record then has no `.id`. The behaviour is shared with Telnet and safe there.

## 7. Timeouts

Login (`WaitForPromptSync`) has its own timeout, `MacTelnetConnection.ConnectTimeout` (default
**15,000 ms**), **separate** from the per-command `ReceiveTimeout` (30,000 ms). The split is
deliberate: under load — hundreds of MAC-Telnet sessions in quick succession during a full suite run —
a login occasionally fails to reach the prompt. If login could block for the full 30 s, the caller's
connect-retry loop (`TestBase`: 1 s attempts over a 20 s window) would have no time for a second
attempt. A shorter login timeout means the retry actually happens and a flaky session recovers.

`TikConnectionSetup.ConnectTimeout` (default 15 s) propagates to `MacTelnetConnection.ConnectTimeout`
in `CreateMacTelnetConnection[Async]`, and can also be set directly:
`new MacTelnetConnection { ConnectTimeout = 10000 }`.

## 8. The router can refuse a login *after* reporting the handshake successful

**`CTRL_END_AUTH` is not proof of a successful login.** The router can accept the EC-SRP5 exchange and
then refuse the session about a second later, in plain text on the terminal stream:

```
| +0.0 s | RECV 0x01 counter=58 × 3     | CTRL_END_AUTH — authentication reported success
| +1.1 s | RECV 0x01 counter=67 len=46  | Login failed, incorrect username or password
| +1.1 s | RECV 0xff × n                | PKT_END — the router tears the session down
```

This is the MAC-Telnet face of the transient refusal also measured on the WinBox handshake
([findings-winbox.md](findings-winbox.md) §13): **the same login, retried immediately, is accepted.**
The router logs it as `login failure for user <user> from <mac> via mac-telnet`.

Three consequences for the code:

1. **The refusal cannot be detected inside the EC-SRP5 exchange**, which correctly ends at
   `CTRL_END_AUTH`. It has to be recognised by the code waiting for the prompt.
2. **`PKT_END` is handled in the login wait only.** That is the one place it has been observed — across
   two full traced runs it appears exactly six times, all six in a refused session. RouterOS does *not*
   send it when logging an idle console out, so teaching the general pump about it would be inventing a
   contract that does not exist.
3. **A refusal says nothing about the credentials.** It is transient.

`TikConnectionLoginRefusedException` is thrown as soon as the line appears or the router hangs up, and
`MacTelnetConnection.Open`/`OpenAsync` are wrapped in `Winbox.RouterLoginRetry`, as the three WinBox
connections are. A refused login fails in about 1 s rather than 15, and is retried up to three times
before being reported.

> **One exception type, not one per transport.** `TikConnectionLoginRefusedException :
> TikConnectionLoginException` is public, carrying the router's verbatim text on `RouterMessage` and the
> handshake on `Transport`. Public because it already reached callers as an `InnerException` — visible
> in the message, impossible to catch by type — and derived from `TikConnectionLoginException` so
> existing catch blocks are unaffected.

> **It is deliberately not called "transient".** The retry is internal and bounded, so by the time this
> exception reaches a caller it is precisely the refusal that did **not** clear. Calling it temporary
> would invite a retry loop exactly where retrying is known not to help.

> **The match is deliberately narrow.** `RouterOsCliLogin.IsLoginFailure` is **not** used here: it
> matches `login failure`, the wording of the router's own log lines, which are echoed onto any console
> at any moment. Matching that during login would turn an unrelated background event into a refused
> login.

### 8.1 It can also answer nothing at all

The router can open the session — acknowledging SESSIONSTART — and then answer NOTHING to the
`CTRL_BEGINAUTH`/`CTRL_PASSSALT` that follows it, for the whole 10 s authentication deadline. Seen on 7.24
once in a run of ~500 tests, and only when suites run back to back: three of four consecutive full runs
failed on the same MAC-only login while the slowest of the four (8 m 20 s, so the longest gaps between
sessions) passed.

It is the same class of behaviour as the refusal above and clears the same way, but it is not the same
thing and must not be reported as one: the router said nothing, so there is nothing to quote.
`TikConnectionLoginNoAnswerException : TikConnectionLoginException` carries what the session had done
instead — whether our handshake packet was ever taken, how many resends were spent on it, which packet
types did arrive — because "timed out waiting for expected MAC-layer packet" cannot tell a router that
never took our bytes from one that took them and said nothing, and those are different faults.

**Raised only once the session has been acknowledged.** Before that the silence is about reachability and
the bare `TimeoutException` is the truer answer — and it keeps `RouterLoginRetry` from turning one clear
10 s failure against an unreachable router into three.

## 9. Reliability of the MAC layer

### 9.1 SESSIONSTART is the one packet that cannot be resent by the normal path

`MacLayerTransport.StartSession()` sends SESSIONSTART, waits for the router's ACK, resends every 300 ms,
and gives up after 2 s — letting authentication fail on its own deadline, since a second error there
would only hide the first.

It needs its own retry because it falls outside every other mechanism:

- it is **not** DATA, so it never enters the unacknowledged queue (§9.2), and
- that queue is inert until the first ACK arrives (`_haveAck`) — which is exactly the ACK SESSIONSTART
  is waiting for.

It is also the **only packet sent to the subnet broadcast**, making it the most likely to be dropped on
the way out; see the NIC-selection comment in `BaseConnect` for a measured case where every SESSIONSTART
left via a disconnected adapter and vanished.

On a healthy link this costs nothing: measured across five traced suite runs on 7.23.2, 76 of 76
SESSIONSTARTs were acknowledged, every one before a blind 80 ms wait would have elapsed.

> When timing this from a trace, remember the ACK is timestamped **when we read it**. A client that
> sleeps before reading will measure its own sleep, not the router's latency.

### 9.2 Unacknowledged packets are a queue, not a single slot

Because the counter is a cumulative byte offset (§1), acknowledgement is cumulative too — so a single
"last packet sent" slot is not enough once more than one request can be in flight (`WinboxNativeMac`;
see [winbox-m2-multiplexing-design.md](winbox-m2-multiplexing-design.md) §4.5). With A (offset 0–99)
and B (100–199) sent and A lost: the router receives B but cannot acknowledge **anything**, because
there is a hole in the stream, so its ACK stays at 0. The packet needing resend is A, and a single slot
holds only B. The session is then stuck for good, not merely slow.

- `SendCore` **appends** to the queue, never overwriting another unacknowledged packet.
- `NoteAck(counter)` discards everything with `End <= counter` (one ACK can retire several packets) and
  resets the retransmit budget — that budget belongs to the packet at the head, and the head has just
  changed. It runs under `SendGate`, because it is called from the receive side, which on a multiplexed
  channel is a different thread from the sender.
- `RetransmitIfUnacked` resends the **oldest** unacknowledged packet: with cumulative ACKs the hole is
  always at the front. With one request in flight this is the same packet a single slot would have held.
- The queue is bounded (`MaxUnackedTracked = 256`), so a caller writing into a dead session cannot grow
  it without limit.

**The rate limit applies to the head packet's own age**, not to elapsed idle time:
`RetransmitIfUnacked` requires the head to have been *unanswered* for `MinRetransmitIntervalMs`. An ACK
that covers less than the head means "not yet", not "lost" — and a check keyed on idle time alone
spends a retransmission on a packet that is still in flight, at the very start of every session.

All writes (`Send`, `SendAck`, `SendPong`, `RetransmitIfUnacked`) go through `SendGate`.

### 9.3 The two unrecoverable states are traced

A MAC session has exactly two dead ends. From either, every subsequent read fails on its own deadline,
far from the cause — so both are traced explicitly:

| State | Meaning | Trace line |
|---|---|---|
| inbound hole (`counter > _inCounter`) | we dropped a packet and re-ACKed the low-water mark; recovery is entirely up to the **router** resending | `HOLE counter=… expected=… missing=…` |
| retransmit budget spent | the router never took our bytes; the stream is blocked for good | `RETRANSMIT BUDGET SPENT end=… highestAck=…` |

`MacTelnetUdpClient.WaitForPromptSync` reports the elapsed time, the packet and character counts,
whether the nag was dismissed, and the tail of the screen — enough to tell "the router said nothing at
all" apart from "we are mid-negotiation and one packet went missing".

### 9.4 Every trace line carries its session tag

Both parsers that emit RECV lines — `MacTelnetUdpClient.TryParsePacket` and `ReceiveOne` — tag them with
the session key, so a trace can be split by `key=` and read one session at a time. This is not cosmetic:
untagged inbound lines get attributed to whichever session opened last, and a per-session reconstruction
of a **green** run then reports hundreds of stream holes that never happened.

## 10. Router-side prerequisites

- `/tool mac-server set allowed-interface-list=all` (or a specific allowed interface) — otherwise
  MAC-Telnet does not respond.
- MNDP (UDP 5678) enabled, unless the `RouterMac` override is used.
- Hyper-V: SESSIONSTART goes to the **subnet broadcast** (`192.168.x.255`), not `255.255.255.255`;
  DATA and ACK go to the router's **unicast** IP. Prefer a NIC on the same subnet — a MAC transport that
  is dead while IP transports work is usually broadcast leaving the wrong adapter.

## 11. Tests

`tik4net.unittests/MacTelnet/MacLayerRetransmitTests.cs` covers the reliability rules over loopback UDP
with no router: the stream hole, partial ACK, exhausted budget, and concurrent send/ACK cases. These
cannot be reproduced against a live router, which does not drop packets on demand, so this is their only
coverage.

## Settled questions — do not re-investigate

- **A previous session holding the local UDP port 20561.** It cannot happen: we bind to an **ephemeral**
  local port (`Bind(new IPEndPoint(nic.LocalIp, 0))`, plus `ReuseAddress`). 20561 is the *router's* port,
  never ours; consecutive sessions show `local=…:49931`, `local=…:49932`. Two MAC sessions never contend
  for a local endpoint.
