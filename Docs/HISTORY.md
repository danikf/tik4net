# Project history — the index

The entry point for **narrative about the past**: diagnoses that turned out wrong, incidents, and
measurements pinned to a moment in time.

Everywhere else — [AGENTS.md](../AGENTS.md), [ARCHITECTURE.md](../ARCHITECTURE.md), the skills, and the
protocol findings in this directory — describes **how things work now**, in the present tense. When a
document would otherwise say "this used to be broken because…", the fact moves here and the document
keeps only the current behaviour.

This file holds the **lesson**; the full account lives in the `-history.md` sibling next to the
document it came from. An entry being here does not make it current: it records what was observed at
that time, on that RouterOS version, against that code.

| Full account | Covers |
|---|---|
| [`findings-cli-history.md`](findings-cli-history.md) | CLI / PTY transports |
| [`findings-winbox-history.md`](findings-winbox-history.md) | WinBox transport and session layer |
| [`findings-rest-api-history.md`](findings-rest-api-history.md) | REST transport |
| [`winbox-native-m2-protocol-history.md`](winbox-native-m2-protocol-history.md) | WinBox native (structured M2) protocol and field vocabulary |
| [`winbox-m2-multiplexing-design-history.md`](winbox-m2-multiplexing-design-history.md) | M2 channel and request/reply correlation |
| [`findings-mepty-byte-ack-history.md`](findings-mepty-byte-ack-history.md) | WinBox CLI `mepty` byte-acknowledgement |

## Why this file exists

Reference documentation that carries its own changelog is expensive to read and easy to misread. A
reader cannot tell, at a glance, whether "✅ FIXED" means the current code is correct or that a fix is
pending, and every superseded diagnosis left in place is a plausible-looking wrong answer sitting next
to the right one. Keeping history in its own files makes the reference documents short and
unambiguous, and makes the history itself easier to search.

---

# The lessons

These are the transferable ones — the reasoning failures worth recognising again, rather than the
individual bugs.

## Ask what result would have disproved the hypothesis

The throughput-ceiling finding first concluded the slowdown was *per-session state on the router*. It
is aggregate and router-wide. The conclusion rested on one comparison: a fresh connection was fast
while an aged one was slow — but the fresh connection made 24 requests against a knee that needs ~200,
and made them while the aged connection was paused. **The experiment could not have come out any other
way.**

Before trusting a confirming measurement, ask what result would have disproved the hypothesis. If the
design admits only one outcome, it has measured nothing.
→ [`findings-router-throughput-ceiling.md`](findings-router-throughput-ceiling.md)

## Test a hypothesis in the regime where the effect actually occurs

The `mepty` counter is a cumulative **byte** acknowledgement. That idea was raised early and dismissed
on *correct evidence reasoned backwards*: a 35-byte keystroke frame moved the counter by only +1. But
the counter acknowledges bytes **received**, not **sent**, so a keystroke frame should not move it by
35. Every trace informing that dismissal was of small output, where the window never closes — they
could not have separated the hypotheses whatever they showed.

The wrong theory ("the terminal degrades after ~10 commands") was a symptom measured in the wrong
unit: ~800 B per command against an 8192 B budget.
→ [`findings-mepty-byte-ack-history.md`](findings-mepty-byte-ack-history.md)

## A trace timestamp records when *you* read, not when it arrived

SESSIONSTART acknowledgement latency measured as "81–108 ms" in every MAC session — because the client
slept 80 ms before reading, not because the router was slow. The same class of error left a wrong
measurement standing for nine hours during a WinBox session-wedge investigation.
→ [`findings-mactelnet.md`](findings-mactelnet.md), [`findings-winbox-history.md`](findings-winbox-history.md)

## Untagged diagnostics produce confident nonsense

One of two MAC RECV parsers omitted the session tag. Replaying a **green** run through a per-session
reconstruction attributed those lines to whichever session opened last and reported **311 stream holes
that never happened**.
→ [`findings-mactelnet.md`](findings-mactelnet.md)

## A gap in one transport is our bug until the router refuses

`/tool/wol` was recorded as a probable REST gap. The builder was posting `/tool/wol/print` and had
never asked for `/tool/wol` at all. This is the origin of the feature-parity rule in
[AGENTS.md](../AGENTS.md).

Related: "list/array field writes are not encodable over M2" was filed as a protocol limit. The wire
format supports array writes — it was an unimplemented encoder on our side.
→ [`findings-rest-api-history.md`](findings-rest-api-history.md)

Related, and the same shape a third time: `add` of an interface subtype over WinBox native was recorded
as refused by the router itself (`unsupported device type`), and eleven integration tests skipped on it.
The refusal was correct — the request never said what to create. The generic `[20,0]` handler's type
discriminator is the same field on a read and on an add, and native was only sending it on reads. The
eleven skips had been hiding three further defects underneath.
→ [`winbox-native-m2-protocol-history.md`](winbox-native-m2-protocol-history.md)

## A skip is not a pass — a capability gate can hide a whole path

Gating synchronous monitor tests on `Streaming` made them Inconclusive on 10 of 11 transports, hiding
three real defects. A capability gate must be tied to an actual refusal by the router, not to a
plausible-sounding flag.

The same shape recurs in test guards: a known-gap entry makes an audit *skip* a path, so a fix that
lands afterwards is never verified.

## Read the request, not the reply

"No address was specified" is usually a correct complaint about a malformed request. The codec was
sending `addr` fields as bare strings and only understood IPv4.
→ [`winbox-native-m2-protocol-history.md`](winbox-native-m2-protocol-history.md)

## A PoC that exercises the easy path proves less than it appears to

MAC-Telnet's ACK rule was wrong from the proof of concept onward, but `/interface print` — short
tabular lines — tolerates retransmission. Only the longer, time-sensitive terminal negotiation exposed
it. A latent defect inherited from a PoC, not a porting regression.
→ [`findings-mactelnet.md`](findings-mactelnet.md)

## A prompt inside the echo, and an empty window taken for evidence (2026-09-24)

On RouterOS 6.49.13 the WinBox terminal read about a quarter of all entities without their `#n=` count — logged
as open problems "the `/system/clock` read never finishes" and "an add answers without its `.id`", both WinBox
CLI only, with a trace comparison against Telnet as the planned next step. The trace answered it: 6.x repaints
the whole command line, prompt included, after every typed character, so the echo is full of prompts. A frame
could end on one of them after the echo key was on screen; the read took it for the completion prompt, stopped
pulling mepty, the terminal went quiet, and the read settled with nothing. Telnet hid the same flaw because TCP
keeps delivering without being asked. The completion prompt now has to start a line of its own.

A sweep of every entity over one long-lived connection then found a second, older defect: `:serialize` support
was concluded from any JSON window that returned — including an empty one, which never runs its print. One
free-text menu with no rows marked 6.49.13 as supporting `:serialize`, and the next one with rows failed with
`bad command name serialize`. Per-entity connections never showed it, which is why earlier 6.x runs were clean.
The transferable lesson: **a probe that cannot fail is not evidence** — and a sweep over one shared session
finds state leaking between reads that a fresh connection per read hides.
→ [`findings-cli.md`](findings-cli.md) §4, [`findings-routeros-6.md`](findings-routeros-6.md)

## Two properties for one renamed field, and a test the default fill passed (2026-09-23)

RouterOS 7 renamed `/ip/service address` to `available-from` and `/tool/e-mail address` to `server`. The
first fix mapped both names as separate properties and told callers to read "whichever is not null". None
ever is: the mapper fills a field the row does not carry with the property's default, and for a `string?`
that default is `""` — so `AvailableFrom ?? Address` stopped at an invented `""`. Its integration tests
asserted "one of the two is not null", which the fill satisfies on every router. Replaced the same day by one
property with `TikPropertyAttribute.AlternateNames`, tested with a value that is not the default.

Writing that value back on RouterOS 6.49.13 then failed on every transport, and exposed an older defect: a
**singleton** `Save` never used the change tracker and sent every writable field — including the defaults the
load had filled in for fields 6.x does not have (`/tool/e-mail` `tls`, `certificate-verification`, `vrf`), which
the router refuses as `unknown parameter`. The exclusion dated from 2015, when the diff needed `LoadById` and a
singleton has no id; the wiki had since described singletons as diffed. They are now.
The transferable lesson: **an assertion the default could satisfy measures nothing** — test a version-specific
read with a value that is not the default.
→ [`findings-routeros-6.md`](findings-routeros-6.md)

## A version comparison where the catalog already knew the answer (2026-09-22)

Three fields RouterOS renamed between 6 and 7 (`/ip/service` `address`/`available-from`, `/tool/e-mail`
`address`/`server`, the OSPF area's `invalid`/`inactive`), plus `syslog-severity=auto`, were briefly
answered by reading the router's version at open and switching tables on it — the one thing the
version-support rule says not to do. It also read the version from the wrong key: `0x16` of the
system-info singleton `[13,4]` is the WinBox PROTOCOL version (`3.30` on 6.49.13, `3.42rc1` on 7.24.4),
so every router parsed as major 3 and the 6.x names were applied to 7.24 until the audit caught it.
Replaced the same day by two mechanisms that ask the version-matched `.jg` instead: reporting both of
RouterOS's words for such a field, and decoding a `numflag`'s members — which also closed the route's
`connect`/`static`, since each catalog declares the origins its version has.

A review the next day corrected two parts of that replacement. `syslog-severity` was never a version
difference: 7.24 prints `auto` too, once the action's log format is syslog — it hides the field under a
WinBox condition that 6.49.13's API ignores. And the `numflag` decode ran on every path, where not every
`numflag` is API flags: it would have reported 7.24's route Contribution members (`unreachable`,
`filtered`, which the API never prints), a script job's kind as `api-login=true` (the API prints
`type=api-login`), and route origins no API row had shown. None of these was visible to the path-map
audit, which reports only the names native lacks, never the ones it adds; a side-by-side dump caught them.
Decoding is now opt-in per path by the member names measured on the API, and the route's origin and
`active` flags lost their hand-written enum table (which answered `false` where the API prints nothing).
→ [`findings-routeros-6.md`](findings-routeros-6.md), [`jg-catalog-format.md`](jg-catalog-format.md)

## A staleness indicator that named the wrong file (2026-08-25)

Every MCP answer carried the build timestamp of the assembly that produced it, so that a rebuild could
be confirmed to have taken effect before the answer was believed. It named `tik4net.mcp.dll` — the thin
tool wrapper — while essentially every change under diagnosis lands in `tik4net.dll`, and the two move
independently: `dotnet build tik4net.sln` after a library-only edit refreshes the library in the output
directory and leaves the wrapper alone, because the wrapper's own compile is up to date and MSBuild
skips it. Measured that day: solution build, `tik4net.dll` 09:44:56, `tik4net.mcp.dll` 09:44:29, and a
staging then in live use whose two files were 38 minutes apart, library newer.

The failure is one-directional and therefore quiet. The stamp can only lag the library, never lead it,
so it never claims freshness it does not have — it just reads "stale" when the answers are current. A
correct field mapping was written off on that basis the day before, and the check that was supposed to
protect against believing old output instead produced a reason to disbelieve new output.

The stamp now reports both assemblies, and `run-dev.ps1` passes the directory it staged from in
`TIK4NET_MCP_SOURCE_DIR` so the server re-checks it per call and says `STALE` itself. Rebuilding is
still not sufficient — the process runs from a frozen copy and has to be reconnected — but it no longer
has to be remembered.
→ [`Tools/tik4net.mcp/README.md`](../Tools/tik4net.mcp/README.md)

## One name for two failures, and the reproduction pinned neither (2026-08-24)

The write audit was recorded as having found a RouterOS defect: *moving a `/routing/rule` over a CLI
transport wedges 7.24's routing process* — reproduced three times, each costing a reboot, and the path
was excluded from the audit's ordered tables on that evidence.

Two unrelated failures had been rolled into one sentence, and the `move` caused neither.

**One was self-inflicted, and not a wedge at all.** The audit's fixture row was an *unconditional*
`/routing/rule`, and its toggle map rewrote that row's `action` to `drop` — a rule matching everything,
told to discard it, sitting in the path of the connection running the audit. Every IP transport went
silent at once, which reads exactly like a dead router. It was not: one MAC-layer read answered
instantly, and a single `remove` over that L2 path brought IP back with the uptime unbroken. Three
reboots were spent not fixing anything.

**The other was real, and turned out to be a race.** With the drop rule gone the audit ran to completion
for the first time, and its report named the surviving failure precisely: the first timeout in the whole
run was the `/routing/rule` move. Reordering a routing rule *in the same second its rows were added*
stops RouterOS 7.24's routing process answering its management interface — the move returns and the
router logs it as applied, and from then on every `/routing/*` menu and `/ip/route` times out on every
transport and in the router's own shell. Nothing is logged, not even under `error`, CPU stays idle, and
forwarding and every other menu are unaffected; only a reboot clears it.

Getting there meant excluding, each by its own measurement rather than by argument: OSPF and BGP (every
one of their operations passed earlier in the same run, and the wedge reproduces with no OSPF on the
router at all), `disabled=yes`, the two-, three- and four-rule shapes, the `src-address=""` an unset
leaves behind, the internal `.id` spelling, and the exact CLI line the code synthesises — which goes
through on its own and leaves the router healthy. What survived was the one thing the probe does that
none of those did: it moves rows that are a fraction of a second old. The controlled pair, on an empty
rule table with one variable: no pause wedges (92 s), a pause is clean. Walking the boundary down on a
healthy router put it between 100 ms and 200 ms — 3 s, 1 s, 500 ms and 200 ms all clean, 100 ms wedges —
so the audit waits one second, about five times the boundary.

The reasoning failures worth keeping:

- **Two failures under one heading defeat every attempt to reason about either.** "The move wedges the
  routing process, and the router goes off the network" was one true clause and one false one welded
  together; each round of evidence confirmed half and was read as confirming the whole. Splitting them
  took one measurement — a MAC-layer read — that had been available all along.
- **A failed reproduction on a simplified setup is not a disproof.** The move on two rules by hand is
  clean, which was briefly read as clearing the move entirely — twice, because typing it by hand and
  sending it through the library both take longer than the effect survives. A negative result bounds the
  trigger; it does not remove it.
- **Two candidates that move together measure nothing.** The first clean run had both a settle and a
  smaller table, so it could not say which mattered. The control that separated them — no settle, empty
  table — cost one more reboot and was the only run in the sequence that proved anything on its own.
- **The diagnosis was never falsified, because every probe used the broken channel.** Over IP, a wedged
  router and an unreachable one are the same observation. The transport that could tell them apart —
  L2, which no routing rule touches — was never tried.
- **"The router needs a reboot" ended the enquiry** before it distinguished the two failures, and merged
  them under one heading.

The instrument is fixed for both: probe rows that sit in a traffic path are created `disabled=yes`, a
rule's toggle is a match condition, which can only narrow it, and every move waits a second for its rows
to settle. `/routing/rule` is back in the ordered tables, and `RoutingRuleMoveWedgeRepro` keeps the
90-second reproduction for whenever the RouterOS side is worth revisiting.
→ [`../.claude/skills/mikrotik-tests/SKILL.md`](../.claude/skills/mikrotik-tests/SKILL.md)

## Silent success is the worst failure mode

`/file/print` over CLI has worn three faces: an empty read (the test passes vacuously), a thrown
`Missing field 'name'`, and its current one — the call succeeds and returns 1 of 27 rows. Only the
middle is detectable without checking a row count.

Likewise `generate-key name=X key-size=2048` over WinBox native produced an unnamed 1024-bit key and
reported success, because the action dispatcher dropped every caller-supplied argument.

And a CLI `remove [find where .id=*N]` against a row that no longer existed removed nothing, printed
nothing, and returned normally — for years, in the most-exercised write path in the library, because no
test had ever asked what happens when the row is gone. Details:
[`findings-cli-history.md`](findings-cli-history.md).

## A change on every command's path needs a full leg, not the smoke subset

Switching the CLI record selector to `numbers=` broke `/system/script/run`, which is an action verb and
rejects it. Unit tests were green — they had been updated to the new expectation — and the smoke subset
(connect, clock, interface list, route CRUD) would have been green too, because it never runs a script.
One test out of 548 on a full Telnet leg caught it. The smoke subset is sized for "does this transport
still connect and do ordinary CRUD"; it is not sized for "did I change how every command is spelled".
Corollary: when a change is per-verb, prefer an allow-list, so an unmeasured verb keeps the behaviour
that already works.

---

# Incidents

## The lab router's admin password was changed

A recycle cascade fed a desynchronised terminal into RouterOS's `new password>` nag; VT100 cursor
reports were typed into the prompt and two matching entries set a new password. There was no second
account, so recovery needed an out-of-band restore.

**Consequence:** no byte except Ctrl-C is ever sent while a password prompt is on screen, and the test
router is provisioned with a second full-privilege recovery account.
→ [`findings-mepty-byte-ack-history.md`](findings-mepty-byte-ack-history.md)

## MAC transports dead while IP transports worked

MAC-Telnet and WinBox-MAC stopped reaching the router while every IP transport, and MNDP discovery,
kept working. The cause was host-side: broadcast leaving a stale deprecated NIC. MNDP continuing to
work is what made it misleading — it is also broadcast, but was answered over a different path.

The proof-of-concept-era hypotheses (a CHR limitation, a source-port rule, Windows Firewall) were all
wrong.
→ [`findings-winbox-terminal.md`](findings-winbox-terminal.md) §7

---

# Measurements pinned to a moment

True when measured, not maintained. Re-measure rather than citing them.

| Measured | What | Value |
|---|---|---|
| 2026-09-16 | Enum vocabulary sweep: all 161 enum-typed `[TikProperty]` fields over 69 menus, Tab-completed on RouterOS 7.24.3 | 14 menus accepted a value no entity could read (4x `arp=local-proxy-arp`, `bridge/nat action=mark-packet`, `l2tp-server use-ipsec=required`, `my-id=dn`, `hash-algorithm=sha384`, proxy `redirect`/`url-append`, walled-garden `reject`, `routing/rule action=mangle`, logging `target=script`, `leds interface-speed-2.5G`, wifi provisioning `use-network-config`, lte `sms-protocol at`/`mbim`); 8 fields not measurable by completion |
| 2026-09-16 | 4.0.0-beta4 full matrix on RouterOS 7.24.3 — see the table below | 0 failures on all eleven transports, 1672 mangle rules in place |
| 2026-09-15/16 | 4.0.0-beta3 gating matrix on RouterOS 7.24.3 — see the table below | 10 of 11 transports green; `RestSsl` failed `ConcurrentCommandsTest` (and plain `Rest` in 2 of 3 reruns): .NET Framework pipelined requests onto a busy HTTP connection, which RouterOS never answers. Green on both REST legs after the fix |
| 2026-09-15 | RouterOS 7.24.3 web server, raw sockets: 3 GETs pipelined on one keep-alive connection / 6 parallel connections × 20 sequential GETs | 1 of 3 answered, 10 of 10 rounds, on `www` and `www-ssl` alike / 240 of 240 answered with their own body |
| 2026-09-11 | 4.0.0-beta3 gating matrix — see the table below | 0 failures on all eleven transports, on the 2-vCPU lab **with** the 1672 mangle rules in place |
| 2026-09-11 | Sliced CLI read of `/ip firewall connection` under churn (~5000 rows turning over every 30 s; `ConntrackChurnPagedReadProbe`, Telnet, 100-row slices) | Without retry 6 of 20 reads complete — 1.4–2.4 % of slices answered only `interrupted`; with a slice retried, 20 of 20 (30 retries). Whole-table prints under the same churn: 12 of 12, every `#n=` exact |
| 2026-09-11 | Large reads at 2 vCPUs (`MacLayerLargeReadProbe`): `/queue/tree` 681 rows (detail + stats) / `/ip firewall mangle` 1672 rows | Telnet 2.6 s / 3.6–3.7 s; SSH 2.6 s / 3.7 s; WinboxCli 2.7 s / 3.8 s, except 2 of 40 commands with 77 and 94 unanswered mepty pulls (10.3 s, 12.5 s); MacTelnet, sliced, 2.8–3.0 s / 7.2–7.4 s; WinboxCliMac, sliced, 3.8–3.9 s / 8.7–8.8 s |
| 2026-09-07 | The same 1672-row mangle table after the lab VM went from 16 to 2 vCPUs | WinBox native over TCP: 50 of 50 reads, worst socket gap 712 ms (at 16 vCPUs: 6 stalls in 48, pauses of 21.8 s and 52.8 s). Binary API, five connection-state arms: 0 stalls in 60 reads, median 5.5–6.0 s |
| 2026-09-06/07 | Full 11-transport matrix, 7.24.2, 16 vCPUs, **1672 mangle rules restored** | 12 failures (night of 09-06) and 7 on the rerun (morning of 09-07), mostly `TikConnectionReceiveTimeoutException` on large reads and `FormatException` from spliced CLI output — the findings that became 4.0.0-beta3 |
| 2026-09-06 | Full 11-transport matrix, 7.24.2, 16 vCPUs, mangle table **empty** | 6039 results: 4938 passed, 1101 skipped, 0 failed. Green because the condition behind the 2026-09-03 timeouts had been removed, not because they were fixed — see the two rows above |
| 2026-08-29 | Full integration run, RouterOS 7.24, 541 tests, nine transports | See the table below — 0 failures everywhere, 3–9 minutes per transport |
| 2026-08-29 | `tik4net.unittests` | 915 tests, 0 skipped |
| 2026-08-29 | `TransportPathMapAuditTest` against the binary API, transport `WinboxNative` | `OK=154 KNOWN-GAP=1 MISMATCH=0 VALUE-DIFF=0 VALUES-UNCOMPARED=1 UNMAPPED=0 ROUTER-N/A=7`; field-name shortfall 96/1342 (7%), and 105/1345 measured on the same fixtures one commit earlier |
| 2026-07-26 | Full integration run, RouterOS 7.23.2, 390 tests | Api/ApiSsl ~5 min; Rest/RestSsl ~3 min; Telnet/Ssh ~7 min; MacTelnet ~13 min; WinboxNative ~5–8 min; WinboxCli ~7 min; WinboxCliMac ~1 h 20 min |
| 2026-07-26 | Same-version reinstall of the MCP global tool | Verified to deliver new code: a marker in `Program.cs` changed the installed assembly hash |

## Full matrix for 4.0.0-beta4, 2026-09-16 — RouterOS 7.24.3, 565 tests

Run after the beta4 tag, on the same 2-vCPU lab with 1672 rules in `/ip firewall mangle`. Against beta3 every
transport has one more pass (`TheRouterOs7BridgeFieldsCanBeWrittenAndReadBack`) and one more skip
(`EnumVocabularySweepProbe`, `[Ignore]`d). Unit tests: 1145 on net8.0, 1141 on net48 (3 skipped). No residue
on the router afterwards.

| Transport | Passed | Skipped | Failed | Wall clock |
|---|---:|---:|---:|---:|
| `Api` | 462 | 103 | 0 | 3.7 min |
| `ApiSsl` | 462 | 103 | 0 | 3.5 min |
| `Rest` | 448 | 117 | 0 | 3.7 min |
| `RestSsl` | 448 | 117 | 0 | 3.8 min |
| `WinboxNative` | 445 | 120 | 0 | 4.8 min |
| `WinboxNativeMac` | 445 | 120 | 0 | 7.7 min |
| `MacTelnet` | 459 | 106 | 0 | 8.1 min |
| `Telnet` | 459 | 106 | 0 | 8.4 min |
| `Ssh` | 458 | 107 | 0 | 7.9 min |
| `WinboxCliMac` | 459 | 106 | 0 | 9.0 min |
| `WinboxCli` | 459 | 106 | 0 | 9.4 min |

The wall clocks are 20–60 % shorter than beta3's with the same rule count; not investigated.

## Gating run for 4.0.0-beta3, 2026-09-15/16 — RouterOS 7.24.3, 563 tests

The same lab as the 7.24.2 run below: 2 vCPUs, 1672 rules in `/ip firewall mangle`. The full matrix failed one
test, `ConcurrentCommandsTest` on `RestSsl`; the REST rows are the rerun on the fix (`RestConnection` caps its
in-flight requests and raises .NET Framework's per-host connection limit), the other nine the full run.
Unit tests on the fix: 1133 on net8.0, 1129 on net48 (3 skipped).

| Transport | Passed | Skipped | Failed | Wall clock |
|---|---:|---:|---:|---:|
| `Api` | 461 | 102 | 0 | 5.9 min |
| `ApiSsl` | 461 | 102 | 0 | 4.8 min |
| `Rest` | 447 | 116 | 0 | 4.8 min |
| `RestSsl` | 447 | 116 | 0 | 5.0 min |
| `WinboxNative` | 444 | 119 | 0 | 7.4 min |
| `WinboxNativeMac` | 444 | 119 | 0 | 19.0 min |
| `MacTelnet` | 458 | 105 | 0 | 11.4 min |
| `Telnet` | 458 | 105 | 0 | 10.7 min |
| `Ssh` | 457 | 106 | 0 | 9.7 min |
| `WinboxCliMac` | 458 | 105 | 0 | 12.4 min |
| `WinboxCli` | 458 | 105 | 0 | 11.5 min |

Before the fix, `ConcurrentCommandsTest` failed on `RestSsl` in 5 of 5 runs and on `Rest` in 2 of 3, always after
the 30 s receive timeout; with the per-host limit raised in the test process alone it passed 6 of 6.

## Gating run for 4.0.0-beta3, 2026-09-11 — RouterOS 7.24.2, 559 tests

On the lab CHR at 2 vCPUs, carrying the 1672-rule `/ip firewall mangle` table that the 2026-09-06/07
failures were measured against — the stronger result, since the empty-table run of 2026-09-06 was green only
because that condition had been removed. The full matrix found one failure, on `winboxcli`: a mid-frame
deadline of 5 s against a router that pauses its terminal output for 10–12 s (findings-winbox.md §20). The two
WinBox terminal legs below are the rerun on the fix; the other nine are from the full run.

Counted from `<UnitTestResult outcome=…>` elements. The TRX `<ResultSummary>` also carries an `outcome`
attribute, so a plain `grep -c 'outcome="Failed"'` counts one extra per red leg.

| Transport | Passed | Skipped | Failed | Wall clock |
|---|---:|---:|---:|---:|
| `Api` | 457 | 102 | 0 | 3.2 min |
| `ApiSsl` | 457 | 102 | 0 | 3.4 min |
| `Rest` | 443 | 116 | 0 | 3.5 min |
| `RestSsl` | 443 | 116 | 0 | 3.5 min |
| `WinboxNative` | 440 | 119 | 0 | 4.4 min |
| `WinboxNativeMac` | 440 | 119 | 0 | 7.1 min |
| `MacTelnet` | 454 | 105 | 0 | 7.8 min |
| `Telnet` | 454 | 105 | 0 | 8.2 min |
| `Ssh` | 453 | 106 | 0 | 8.4 min |
| `WinboxCliMac` | 454 | 105 | 0 | 8.7 min |
| `WinboxCli` | 454 | 105 | 0 | 9.0 min |

`Ssh` skips one more than the other CLI transports: the lab account has no password, and RouterOS accepts SSH
auth method `none` for such an account, so a rejected password cannot be provoked.

## Full run, 2026-08-29 — RouterOS 7.24, 541 tests

Read from the TRX files, counting `outcome="NotExecuted"` rather than the `ResultSummary` counters,
which report zero skips on a run that skipped a hundred.

| Transport | Passed | Skipped | Failed | Wall clock |
|---|---:|---:|---:|---:|
| `Api` | 449 | 92 | 0 | 2.9 min |
| `WinboxNative` | 433 | 108 | 0 | 3.4 min |
| `WinboxNativeMac` | 433 | 108 | 0 | 3.8 min |
| `Rest` | 435 | 106 | 0 | 4.3 min |
| `MacTelnet` | 446 | 95 | 0 | 7.2 min |
| `Telnet` | 446 | 95 | 0 | 7.7 min |
| `Ssh` | 445 | 96 | 0 | 8.1 min |
| `WinboxCliMac` | 446 | 95 | 0 | 8.4 min |
| `WinboxCli` | 446 | 95 | 0 | 9.0 min |

`ApiSsl` and `RestSsl` were not run in full that day. `Api`, `Rest` and `WinboxNative` were measured one
commit before the others, on a change confined to the CLI `add` path, which none of the three reaches.

Most of what these runs skip is not a capability skip, so the difference between `Api` (92) and
`WinboxNative` (108) is much smaller than the totals suggest — the `mikrotik-tests` skill states the rule
and the breakdown.

**The MAC-layer penalty in the 2026-07-26 row is gone.** That run measured `WinboxCliMac` at about
1 h 20 min and read it as a property of the layer: roughly 5 s per command against ~200 ms over TCP. On
7.24 the same transport finishes in 8.4 minutes — *faster* than `WinboxCli` over TCP — and `MacTelnet` is
the quickest of the five CLI transports. Whatever the older figure measured, it was not a floor imposed
by addressing the router at L2. Budget minutes for a MAC-transport run, and do not cite the old number.

---

# Phasing

`AsyncCommands` and `CancelInFlight` were not added to every transport at once: REST first, then the
whole CLI family, then the binary API, then WinBox native, each over its own awaited I/O. All in-tree
transports now declare both. The binary API's per-connection reader — dispatching each sentence to the
tag that asked for it, so an async command holds a registration rather than a thread — arrived in the
same phase, after `AsyncCommands` had already shipped for REST and the CLI family.

---

# Superseded artifacts

- **`tik4net.console`, `tik4net.torch`, `tik4net.coreconsole`** — three separate demo projects, replaced
  in 4.0 by the single [`samples/tik4net.samples`](../samples/tik4net.samples/README.md) app and its
  `console` / `torch` / `crud` subcommands. `tik4net.coreconsole` existed only to demonstrate that the
  library works on .NET Core, a question the `netstandard2.0` target settles on its own. Each shipped its
  own `App.config` carrying a lab router address; the sample takes coordinates as arguments instead.

- **`TestResults/test-failures-report.md`** (2026-06-20) — a baseline failure catalog with categories
  A–K. The directory is git-ignored and the file no longer exists; most of its entries did not
  reproduce when re-verified, having been orphan contamination or flaky timing rather than defects.
  Named here only so references to it in older material resolve. Do not reconstruct it: regenerate
  counts from TRX files after a clean run (`Tools/probes/parse-trx.ps1`).
