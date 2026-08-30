# WinBox CLI `mepty` — the terminal byte-acknowledgement contract

How the WinBox terminal channel (`mepty`) flow-controls terminal output, and what a client must do to
keep a session usable. For how this was worked out — including the theory that was wrong and the
incident it caused — see [findings-mepty-byte-ack-history.md](findings-mepty-byte-ack-history.md).

## The contract

The mepty `Data` frame's user key 3 — `WinboxM2Protocol.Mepty.Key.Counter` — is a **cumulative
acknowledgement of terminal-output bytes consumed by the client**. It is not a message counter.

RouterOS runs a send window on top of it and **will not let unacknowledged output exceed ~8 KB**. A
client that sends anything other than its running total of received payload bytes therefore caps the
output any one terminal session can ever deliver:

```
bytes delivered  ≈  8192  +  (whatever the client's counter has reached)
```

Sending a per-frame message counter makes that window creep open one byte per frame transmitted, which
is why the resulting ceiling looks like a *command count* rather than a byte budget — a session doing
~800 B per command dies at roughly command 10, while one large command dies inside its first.

## What the client must do

Implemented in [`../tik4net/WinboxCli/WinboxCliClient.cs`](../tik4net/WinboxCli/WinboxCliClient.cs):

1. **Acknowledge received bytes.** `_ackBytes` is the running total of terminal payload bytes received,
   incremented in `ReceiveTerminalChunk`. Both `SendPull` and `SendInput` put **that** value in the
   `Counter` field. `SendTerminalReady` sends 0, which is the correct initial acknowledgement.
   `_ackBytes` is deliberately `int`: the wire field is u32 and `U32User` casts, so unchecked wraparound
   still encodes the right modulo-2³² value.
2. **Drop frames belonging to another mepty session**, via `M2Message.TryParseSessionId` (the
   non-throwing sibling of `ParseSessionId`). Their bytes must not enter the buffer **and must not be
   acknowledged** — the acknowledgement is per-session.
3. **Settle on quiet, not on "the prompt is still last."** In `ReadCommandResponseSync`, once the
   completion prompt has been seen the read returns after `SettleMs` of silence. Keying on the buffer
   still *ending* at a prompt fails whenever a trailer arrives afterwards — a repaint, or the
   asynchronous output of `/system/script/run` — and the read then runs to the full receive timeout.
4. **Never type into the change-password nag.** While `new password>` or `repeat new password>` is on
   screen, the only byte ever sent is the Ctrl-C that dismisses it. VT100 cursor-probe answers are
   suppressed there, because they are keystrokes like any other and two matching entries **set a
   password**. Bounded by `MaxNagRounds = 3`, then a loud failure.

> **Treat the change-password path as security-sensitive, not as noise to be skipped.** Rule 4 exists
> because bytes meant for the shell, arriving while that prompt is up, once changed the lab router's
> admin password and locked everyone out.

With the acknowledgement correct the session does not wedge, so there is no need to re-open the terminal
periodically. Session recycling is not a mitigation for this — it treats the symptom, and it multiplies
the number of logins landing on the change-password nag.

## Verifying it

Count `TikWireDir.Recv` payload bytes on channel `wbxcli.mepty`: that cumulative total is the number
that matters, and comparing the same command over `Telnet` is the fastest parity check — the router will
stream tens of KB over a PTY without complaint, so any ceiling well below that is a client-side defect.

Set `ReceiveTimeout` **before** `Open` when probing, so a wedge surfaces in seconds rather than at the
30 s default:

```csharp
var setup = new TikConnectionSetup(host, user, pass) { ReceiveTimeout = TimeSpan.FromSeconds(8) };
var conn = setup.Create(TikConnectionType.WinboxCli);
```

A probe harness for this wants three modes, the middle one being the important one: a soak loop that
flags the first wedge and dumps the byte trace; a **budget** mode that repeats one command and reports
*cumulative received bytes* at the wedge — this is what distinguishes a byte budget from a command
count; and a mode that repeats a command on any transport and reports rows plus elapsed time for
cross-transport comparison.

## Current limitations

### `WinboxCliMac` still wedges, below the window

`WinboxCliClient` is shared, so the MAC-layer sibling gets the same acknowledgement handling. It helps
but does not cure:

| Byte-budget probe (`/system/clock/print`, one session) | Wedge at | Bytes delivered |
|---|---|---|
| MAC | command #19 | 6233 B |
| TCP | never (300 commands) | 88 KB |

MAC stops *below* the 8192 B window, so something other than the acknowledgement rule is at work.
Leading hypothesis: the MAC transport is UDP and lossy, so a dropped terminal frame is never counted
into `_ackBytes`, the acknowledgement permanently trails what RouterOS sent, and the window never fully
reopens. To confirm, diff router-emitted bytes against acknowledged bytes in a `traceLevel=bytes`
capture over the `mactelnet.udp` and `wbxcli.mepty` channels.

Each command also costs ~5 s over MAC against ~200 ms over TCP, which is a property of the MAC layer
rather than of the terminal.

### `/file/print` `contents` silently truncates the CLI parse

Measured on 7.23.2: **27 files over the binary API, 1 row over `winboxcli` and over `telnet`**. With
`.proplist=.id,name,size` (no `contents`) `winboxcli` returns all 27 correctly. The same parser serves
both CLI transports, so this is not transport-specific — RouterOS `as-value` output has no escaping, and
file contents containing `;` and `=` shred the record boundaries.

**The failure mode is silent, which is the dangerous part.** This defect has worn three faces — an empty
read (the test passes vacuously), a thrown `Missing field 'name'`, and its current one: **the call
succeeds and returns 1 of 27 rows**. Only the middle one is detectable without checking the row count,
so do not treat a green `/file/print` over CLI as evidence the parse is intact. Drop `contents` from the
CLI proplist.

## Related

- [findings-cli.md](findings-cli.md) — the CLI/PTY layer generally, including the large-output mepty
  pull and the fire-on-idle `SendPull` that is the other half of this contract.
- [findings-winbox-terminal.md](findings-winbox-terminal.md) — WinBox terminal-over-M2 behaviour.
- [findings-mepty-byte-ack-history.md](findings-mepty-byte-ack-history.md) — how this was arrived at.
