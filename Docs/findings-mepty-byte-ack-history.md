# WinBox CLI `mepty` byte-ACK — investigation history

The end-to-end story behind [findings-mepty-byte-ack.md](findings-mepty-byte-ack.md), which holds the
current contract. This file is history: it records what was believed, what it cost, and why the wrong
answer looked right. Nothing here describes how the code behaves today.

Summarised in [HISTORY.md](HISTORY.md); the full account is here because the investigation ran across
two sessions, produced a useful set of negative results, and included an incident.

## The theory that was wrong

The first session concluded that **"the terminal degrades after ~10 commands and must be recycled"**,
and built machinery to act on it: `RecycleTerminal`, `QuitSession`, `_commandsSinceReopen`,
`RecycleAfterCommands`.

It was a symptom, measured in the wrong unit. The soak loop that produced "~10 commands" averaged about
800 B of output per command, and 8192 / 800 ≈ 10. The number was never a command count.

Once the counter is understood as a cumulative **byte** acknowledgement, every observation falls out of
one equation — `bytes delivered ≈ 8192 + (frames sent)`:

| Observation | Explanation |
|---|---|
| "Degrades after ~10 commands" | ~800 B per command against an 8192 B budget |
| Wedge counter varied 24 / 131 / 380 | Different command mixes → different bytes per frame → different frame counts at the ceiling |
| A single large command hangs on a *fresh* session | `/log print` is ~61 KB; it dies inside the first command |
| Failures only mid-suite | Earlier tests had already spent the session's byte budget |
| Pull cadence made no difference | More pulls = more frames = a slightly wider window, never enough to matter |

### The measurements that settled it

Recycling disabled, one terminal session, `ReceiveTimeout = 8 s`:

| Probe | Wedge point | Bytes received at wedge |
|---|---|---|
| `/system/clock/print` ×N (~120 B each) | command **#26** | **8361 B** (8192 + ~169 frames) |
| `/log/print` (~61 KB, single command) | command **#1** | **8349 B** (8192 + ~157 frames) |
| Telnet, same `/log/print` | never | 1001 rows in **162 ms** |

The two mepty numbers agree to within the frame count, and the Telnet row is the parity argument: the
router streams 61 KB over a PTY without difficulty, so an 8 KB ceiling was **ours**.

After the fix:

| Probe | Result |
|---|---|
| `/log/print` over winboxcli | **999 rows in ~530 ms** (was 71 rows, then a 30 s timeout) |
| `/system/clock/print` ×300, one session | **no wedge, 88 KB delivered, 0 recycles** |
| 4-command soak ×100 (400 commands) | **83 s, 0 timeouts** (was 124 s *with* recycling) |

## Falsified hypotheses — do not re-explore

| Hypothesis | Test performed | Result |
|---|---|---|
| Counter value too high | observed wedge counter across runs | varied 24 / 131 / 380 ✗ |
| Empty-pull flood saturates the router | pull cadence 20 / 120 / 1000 ms | all wedge at the same *byte* count ✗ |
| Counter must not advance on empty pulls | `SendPull` without `++` | still wedges ✗ |
| Rapid-fire commands | 300 ms delay between commands | still wedges ✗ |
| VT100 cursor drift | recycling fixed it without touching the VT100 state | ✗ |
| VT100 `ESC[6n` unanswered mid-command | traces show no `[6n` during output | ✗ |
| "Degrades after ~10 commands per session" | byte-budget probe: 26 small commands vs 1 large one, **same byte total** | ✗ — this was the first session's conclusion |

### The inference that was backwards

The cumulative-bytes idea was raised in the first session and **dismissed on correct evidence,
reasoned about the wrong way round**: a 35-byte keystroke frame advanced the counter by only +1, which
seemed to rule out a byte counter.

The observation was accurate; the inference was inverted. The counter acknowledges bytes **received**,
not bytes **sent**, so a keystroke frame *should not* move it by 35.

The generalisable lesson: **test a flow-control hypothesis in the regime where the window actually
closes.** Every early trace was of small output, where nothing is ever throttled — so the traces could
not have distinguished the hypotheses, whatever they showed.

## The incident — the lab router's admin password was changed

During a `p213c3` cascade in the first session, a buggy recycle left a dead session id. Every command
then failed, so the caller tore down and re-opened the connection each time — a reconnect storm of
EC-SRP5 logins. Each new terminal lands on RouterOS's `new password>` nag when the password is empty.
On desynchronised terminals the nag detection missed, and other bytes being sent — VT100 cursor
reports — were typed into the prompt. Two matching entries **set a new admin password**.

There was no second account on the router, so recovery required an out-of-band restore and the
investigation stalled.

Two defences came out of it: no byte except Ctrl-C is ever sent while a password prompt is on screen,
and removing the recycle cut mepty logins per suite from ~100 back to one per connection. The test
router is now provisioned with a second full-privilege recovery account.

The router was re-provisioned after the restore following the `chr-test-router-init` skill. Note that a
RouterOS version bump invalidates version-pinned material — skip counts and offline `.jg` copies both
drift.

## Integration-run history

`winboxcli.runsettings`, 375 tests, passed / failed / elapsed:

| Run | Variant | Result | Notes |
|---|---|---|---|
| baseline | pre-fix | 288 / **14** / 52 min | |
| p213c2 | recycle, close-old, throw+retry | 297 / 5 / ~15 min | best of the recycle variants (on 7.21.4) |
| p213c3 | recycle, `/quit` before open → dead session | 262 / **50** / 36 min | cascade → **router lockout** |
| p213c4 | recycle + session filter + settle-on-quiet | 294 / 8 / 23 min | wedge gone; residue traced to the ~8 KB cap |
| **ack** | **byte-ack, recycling removed** | **299 / 3 / 12 min** | best result; all originally-failing tests pass |

The three residual failures were pre-existing and unrelated to mepty. Post-fix smoke subset, all green:
winboxclimac 20/20, winboxnative 19/20 (1 skip), winboxnativemac 19/20 (1 skip), api 20/20,
telnet 20/20 — the native transports mattered because `M2Message.ParseSessionId` was refactored
underneath them.

## A related wrong diagnosis, in the same direction

`Winbox_FetchJgGz_ViaMproxy_Works` was recorded here as "the mproxy `.jg.gz` fetch times out on
7.23.2" — blaming the router, as almost everything else in this investigation initially did.

The fetch was asking for a **hardcoded** filename. Plugin names must be resolved from the mproxy `list`
catalog (`unique` = the version-stamped on-disk name); a bare name opens, reports the right size, then
never answers and takes the channel down with it. The test was rewritten as `WinboxJgFetchTest`.
