# P2.13c — WinboxCli mepty hangs: the mepty counter is a **byte acknowledgement**

> Updated 2026-07-25 (second session). **The "terminal degrades after ~10 commands / recycle the terminal"
> theory recorded in the first session was WRONG** — it was a symptom, measured in the wrong unit. The real
> defect is a protocol misreading, and the fix is a few lines. The recycle machinery has been removed.
> The first session's dead ends are kept below, because several of them are still valuable *negative*
> results and one of them cost us the lab router.

## Where this sits in the roadmap

- Roadmap: [`_notes/Reviews/ARCHITECTUREIMPROVEMENTPLAN.md`](../Reviews/ARCHITECTUREIMPROVEMENTPLAN.md) — item **P2.13c**.
- Predecessor: **P2.13** (large-output mepty pull) — [`findings-cli.md` §12](findings-cli.md). That fix added
  the fire-on-idle `SendPull`, which was *half* the contract. P2.13c supplies the other half.
- Memory note: `project_winboxcli_mepty_recycle.md` (superseded by this doc — rewrite it).

## ROOT CAUSE (confirmed live)

The mepty `Data` frame's user key 3 — `WinboxM2Protocol.Mepty.Key.Counter`, which we had documented as a
"monotonic data counter" — is a **cumulative acknowledgement of terminal-output bytes consumed by the
client**. RouterOS runs a send window on top of it and **will not let unacknowledged output exceed ~8 KB**.

We were sending a message counter (`_counter++`, +1 per frame we sent). RouterOS read that as "the client
has consumed N bytes", so the window crept open one byte per frame we transmitted. The result is a hard
ceiling on the output any one terminal session can ever deliver:

```
bytes delivered  ≈  8192  +  (number of Data frames the client sent)
```

Every symptom in this item falls out of that one equation:

| Symptom | Explanation |
|---|---|
| "Terminal degrades after ~10 commands" | The soak loop averaged ~800 B of output per command. 8192 B / 800 B ≈ 10. **It was never a command count.** |
| Wedge counter varied 24 / 131 / 380 | Different command mixes → different bytes per frame → different frame counts at the ceiling. |
| A single big command hangs on a *fresh* session | `/log print` is ~61 KB. It dies inside the first command. |
| Failures only mid-suite | Earlier tests had already spent the session's byte budget. |
| Pull cadence made no difference | More pulls = more frames = a slightly wider window (a few hundred bytes), never enough to matter. |

### Measurements that pin it down

Recycle disabled, one terminal session, `ReceiveTimeout = 8 s`:

| Probe | Wedge point | Bytes received at wedge |
|---|---|---|
| `/system/clock/print` ×N (small, ~120 B each) | command **#26** | **8361 B** (8192 + ~169 frames) |
| `/log/print` (~61 KB, single command) | command **#1** | **8349 B** (8192 + ~157 frames) |
| Telnet, same `/log/print` | never | 1001 rows in **162 ms** |

The two mepty numbers agree to within the frame count, and the Telnet column is the parity argument: the
router will happily stream 61 KB over a PTY, so an 8 KB ceiling was **our** bug, exactly as the CLAUDE.md
"assume feature parity" rule says to assume.

**After the fix** (`Counter` = cumulative received payload bytes):

| Probe | Result |
|---|---|
| `/log/print` over winboxcli | **999 rows in ~530 ms** (was 71 rows / 30 s timeout) |
| `/system/clock/print` ×300, one session | **no wedge, 88 KB delivered, 0 recycles** |
| 4-command soak ×100 (400 commands) | **83 s, 0 timeouts** (was 124 s *with* recycling) |

## THE FIX

[`tik4net/WinboxCli/WinboxCliClient.cs`](../../tik4net/WinboxCli/WinboxCliClient.cs):

1. **`_ackBytes`** — running total of terminal payload bytes received, incremented in
   `ReceiveTerminalChunk`. Both `SendPull` and `SendInput` now put **that** in the `Counter` field instead
   of `_counter++`. `SendTerminalReady` already sent 0, which is the correct initial ack.
   Deliberately `int`: the wire field is u32 and `U32User` casts, so unchecked wraparound still encodes the
   right modulo-2³² value.
2. **Frames from another mepty session are dropped** (`M2Message.TryParseSessionId`, new non-throwing
   sibling of `ParseSessionId`). Their bytes must not enter the buffer *and must not be acknowledged* —
   the ack is per-session.
3. **Settle on quiet, not on "the prompt is still last"** in `ReadCommandResponseSync`. Once the completion
   prompt has been seen, the read returns after `SettleMs` of silence. Previously a trailer arriving after
   the prompt (a repaint, or `/system/script/run`'s asynchronous output) left the buffer no longer *ending*
   at a prompt and the read ran to the full receive timeout.
4. **The change-password nag can no longer be typed into** (see the incident below). While
   `new password>` / `repeat new password>` is on screen, the only byte we ever send is the Ctrl-C that
   skips it — VT100 cursor-probe answers are suppressed, because they are keystrokes like any other and two
   matching entries *set a password*. Bounded at `MaxNagRounds = 3`, then a loud failure.

**Removed:** `RecycleTerminal`, `QuitSession`, `_commandsSinceReopen`, `RecycleAfterCommands`, `_password`,
`_counter`. With the ack correct the session never wedges, so re-opening the terminal every 8 commands was
treating the symptom — and it was the most dangerous code in the file (~100 extra mepty logins per suite,
each landing on the change-password nag; a failed recycle cascade is what changed the router's password).

## Falsified hypotheses (DEAD ENDS — do not re-explore)

| Hypothesis | Test performed | Result |
|---|---|---|
| Counter value too high | observed wedge counter across runs | varied 24 / 131 / 380 ✗ |
| Empty-pull **flood** saturates router | pull cadence 20 / 120 / 1000 ms | all wedge at the same *byte* count ✗ |
| Counter must not advance on empty pulls | `SendPull` without `++` | still wedges ✗ |
| Rapid-fire commands | 300 ms delay between commands | still wedges ✗ |
| `_vt100` cursor drift | recycle fixed it without touching `_vt100` | ✗ |
| VT100 `ESC[6n` unanswered mid-command | traces show no `[6n` during output | ✗ |
| **"Degrades after ~10 commands per session"** | byte-budget probe: 26 small commands vs 1 large one, **same byte total** | ✗ **— this was the first session's conclusion, and it was wrong** |

Note how the first session's "cumulative-bytes ACK" idea was dismissed on the evidence that a 35 B keystroke
frame advanced the counter by only +1. That observation was correct and the inference was backwards: the
counter is an ack of bytes **received**, not of bytes **sent**, so a keystroke frame *shouldn't* move it by
35. The lesson is to test a flow-control hypothesis in the regime where the window actually closes — every
early trace was of small output, where nothing is ever throttled.

## ⚠️ THE INCIDENT — router admin password changed (resolved, and now defended against)

During the first session's `p213c3` cascade, a buggy recycle left a dead session id, so every command
failed, so the caller tore down and re-opened the connection each time — a reconnect storm of EC-SRP5
logins. Each new terminal lands on RouterOS's `new password>` nag when the password is empty. On desynced
terminals the nag detection missed, and other bytes we send (VT100 cursor reports) were typed into the
prompt; two matching entries **set a new admin password**. The lab router had to be restored out of band.

Defence now in place: fix #4 above (no byte except Ctrl-C is ever sent while a password prompt is on
screen) plus the removal of the recycle, which cut mepty logins per suite from ~100 back to 1 per
connection. **Treat the change-password path as security-sensitive, not merely as noise to skip.**

Router state after the restore (2026-07-25): RouterOS **7.23.2** (was 7.21.4), CHR x86_64, IP
coordinates per `tik4net.integrationtests/App.config`, `admin`/empty unchanged, full package set, NTP + `Europe/Prague`,
self-signed `ca-tik4net` → `server-tik4net` on api-ssl and www-ssl. A `test`/`test` account also exists as a
fallback (visible in `/log`). ⚠️ The `mikrotik-tests` baseline catalogue and the offline `.jg` copies in
`_notes/WinboxMessage/7.21.4-http/` are both from 7.21.4 — expect drift.

## Integration-run history (winboxcli.runsettings, 375 tests)

| Run | Variant | Result | Notes |
|---|---|---|---|
| `results_winboxcli_clean.trx` | pre-fix baseline | 288 / **14** / 52 min | 9 P2.13c + 4 P2.14 + 1 P2.12 |
| p213c2 | recycle, close-old, throw+retry | 297 / 5 / ~15 min | best of the recycle variants (on 7.21.4) |
| p213c3 | recycle, `/quit` before open → dead session | 262 / **50** / 36 min | cascade → **router lockout** |
| p213c4 | recycle + session filter + settle-on-quiet | 294 / 8 / 23 min | wedge gone; residue traced to the ~8 KB cap |
| **ack** | **byte-ack, recycle removed** | **299 / 3 / 12 min** | best ever; all 9 original P2.13c tests pass |

The 3 residual are all pre-existing catalogued items, none in the mepty class:
`LoadListenAsync_DetectsInterfaceChange` (P2.14, passes in isolation),
`SafeMode_DisconnectWithoutRelease_RollsBack` (red in the original baseline),
`WolWithInvalidInterfaceWillFail` (P2.12).

Smoke subset (`ConnectionTest`/`SystemClockTest`/`InterfaceListTest`/`IpRouteTest`) after the fix, all green:
winboxclimac 20/20, winboxnative 19/20 (1 skip), winboxnativemac 19/20 (1 skip), api 20/20, telnet 20/20.
winboxnative/winboxnativemac matter here because `M2Message.ParseSessionId` was refactored under them.

### winboxclimac — improved, still broken (roadmap **P2.19**)

`WinboxCliClient` is shared, so the MAC-layer sibling got the same fix. It helps but does not cure:

| byte-budget probe (`/system/clock/print`, one session) | wedge at | bytes delivered |
|---|---|---|
| MAC, pre-fix (`f77f0d1` files checked back in) | command #12 | 4102 B |
| MAC, post-fix | command #19 | 6233 B |
| TCP, post-fix | never (300 commands) | 88 KB |

So **not a regression** — it was already wedging and now wedges later — but MAC still stops *below* the
8192 B window, and each command costs ~5 s there vs ~200 ms on TCP. Full winboxclimac suite on the fixed
build: **288 / 14 / 1 h 23 m**; 8 of the 14 are the async/listen family, the rest are receive-timeouts
returning empty (the wedge signature). Leading hypothesis: MAC transport is UDP and lossy, so a dropped
terminal frame is never counted into `_ackBytes`, our ack permanently trails what RouterOS sent, and the
window never fully reopens. Confirm by diffing router-emitted bytes against acked bytes in a
`traceLevel=bytes` capture over `mactelnet.udp` + `wbxcli.mepty`.

## Still open after this item (each needs its own entry)

1. **`/file/print` `contents` silently shreds the CLI as-value parse** (roadmap **P2.17**) — measured on
   7.23.2: **27 files over API, 1 row over winboxcli *and* telnet**; with `.proplist=.id,name,size` (no
   `contents`) winboxcli returns all **27 correctly**. Same parser on both CLI transports, so not a
   transport issue. Watch the failure mode: the ~8 KB cap used to make the read empty (test passed
   vacuously), then it threw `Missing field 'name'`, and now it **passes while returning 1 of 27 rows** —
   silent data loss. RouterOS as-value has no escaping, so drop `contents` from the CLI proplist.
2. **P2.12** `CliErrorParser` — `WolWithInvalidInterfaceWillFail`.
3. **P2.14** flaky async/listen — `LoadListenAsync_*` (passes in isolation).
4. **`SafeMode_DisconnectWithoutRelease_RollsBack`** — red in the original baseline, orthogonal to mepty.
5. ~~**`Winbox_FetchJgGz_ViaMproxy_Works`**~~ **RESOLVED as P2.18** — the diagnosis recorded here ("the
   mproxy `.jg.gz` fetch times out on 7.23.2") was wrong, and wrong in the same direction as everything else
   in this document: it blamed the router. The fetch was asking for a **hardcoded** filename. Plugin names
   must be resolved from the mproxy `list` catalog (`unique` = the version-stamped on-disk name); the bare
   name opens, reports the right size, then never answers and takes the channel with it. Test rewritten as
   `WinboxJgFetchTest`; see roadmap P2.18.

## Reproducing / probing

The scratch soak harness (net8.0 console, `ProjectReference` to `tik4net.csproj`, installs a P2.15
`ITikWireTraceSink` into a ring buffer) has these modes; recreate it if it is gone:

```csharp
// soak <iters> [delayMs]        — 4-command loop, flags the first wedge and dumps the byte trace
// budget <command> [maxCmds]    — repeats one command, reports CUMULATIVE received bytes at the wedge
//                                 (this is the mode that distinguishes bytes from command count)
// logprint <n> [transport] [cmd]— repeats one command on any transport, reports rows + ms
var conn = ConnectionFactory.CreateConnection(TikConnectionType.WinboxCli);
conn.ReceiveTimeout = 8000;    // set BEFORE Open so a wedge shows in ~8 s, not 30 s
conn.Open(host, user, pass);
```

Count `TikWireDir.Recv` payload bytes on channel `wbxcli.mepty` in the sink — that total is the number that
matters, and comparing it against the same command over `Telnet` is the fastest parity check.
