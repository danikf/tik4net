# winbox-native-m2-protocol.md — investigation history

Superseded diagnoses, incidents and pinned measurements for the WinBox native (structured M2) protocol.
The current behaviour is in [winbox-native-m2-protocol.md](winbox-native-m2-protocol.md); this file is history and describes
nothing about how the code works today.

Indexed from [HISTORY.md](HISTORY.md), which carries the transferable lessons.

---

## Diagnoses that were wrong

### Wire type inferred from the `.jg` prefix letter alone

Early notes proposed a "prefix → TLV type" table read straight off the `.jg` id's letter (`u`=u32→
`0x08`, `s`=string→`0x21`, `b`=bool→`0x00`/`0x01`, `r`=raw→`0x31`, uppercase=array→`0x88`/`0xA0`),
explicitly marked "hypothesis, to be verified in Phase 2." The real formula, read later from
`master*.js`, is `ftype << 3 | size-flags`: string is `0x20` not `0x21`, bool has no separate
`0x01` variant, and "array" is a fixed `+0x80` over every scalar ftype rather than a distinct
per-type array code. Several individual bytes in the hypothesis table happened to be right, which
made the wrong underlying model look confirmed.

### `getall` assumed to be a small per-handler command number

Early probing on handler `[20,0]` swept `cmd=1..8` (with and without an id) and concluded `cmd=3`
(no id) was "getall-ids," labelled `0xFE0000–0xFE0016` as "system-level, not CRUD," and left "how to
get the full record" as an open question, with candidate references to investigate
(tenable/routeros `bytheway/src/main.cpp`, an unnamed "Make It Rain" article,
subixonfire/winbox-terminal-protocol). Reading webfig's `master.js` showed the opposite: `cmd=3` on
a handler returns its *type registry*, not instances, and the real CRUD verbs sit inside the very
range that had been ruled out — `0xFE0004`=getall, `0xFE0002`=get-one, `0xFE0003`=set,
`0xFE0005`=add, `0xFE0006`=remove. `.jg` never lists these for `type:'map'` because they are the
generic defaults, which is exactly what made them look like something else.

### Message-array records had no parser case, so getall rows silently vanished

Before the parser recognised wire type `0x28`/`0xA8` (message / message-array), an unrecognised type
fell through to `SkipTypeBytes`'s default of zero length, so parsing stopped advancing and never
reached the actual records under key `0xFE0002`. A getall replied with status 0 and produced zero
rows, with nothing to distinguish "we don't understand this reply" from "the object doesn't exist."
The same class of bug (a missing `SkipTypeBytes` case silently misaligning the rest of the message)
recurred twice more: the `0xA0` string-array type, and later `FT_ADDR6` (`0x18`, see below).

### Streaming assumed to require a server-push reader

The initial plan for continuous monitors (torch, ip-scan, ping) assumed RouterOS pushes updated rows
asynchronously, driven by `.jg`'s `autorefresh` hint, and scoped the implementation around an async
reader that dispatches unsolicited frames by request-id or subscription-id. Reading webfig's
`ObjectQuery`/`ObjectAction` showed the opposite: the client re-polls on a timer over the ordinary
synchronous request/reply channel, and the router never sends a monitor row unrequested. This
removed the assumed blocker entirely — a worker thread doing start → poll → cancel over the existing
session turned out to be sufficient, confirmed live with `WinboxNativeM2Test.Native_MonitorCycle_Profile`
(profile handler `[49]`, an `[Ignore]`-gated PoC run manually): start (`0xFE000F`, request
`u1=0xFFFFFFFD`="total") → reply id `0xFFFFFFFD` in `.id`; poll (`0xFE0004` + `.id` + flags
`0x10000005`) → 1–2 rows per pass every 1000 ms; cancel (`0xFE0011`) → status 0; 4 rows across 3
passes total. `subscribe`/`0xFE0012` is a real mechanism, but for config-table change push, not
monitor windows — out of scope for this work.

### A query-window poll treated as a bounded, paged snapshot

`PollMonitor` (the poll loop) originally ran under a fixed 4-second / 256-round budget, on the
assumption that a poll pass was a paginated but finite snapshot, the same shape as an ordinary
getall's pagination. In fact a `type:'query'` window has no `pollcmd` at all — every poll is a plain
`getall` on the monitor id, and the router blocks each continuation request until the next row
exists, so a pass on a long-running command (`/ping count=30`) legitimately takes 30 seconds. When
the fixed budget expired mid-pass, the continuation token was discarded; the next poll sent a
token-less `getall`, which the router answered with `0xFE0004` (ObjectNonexistent — its own "no more
rows" signal). The monitor then went silent: no error, no onDone, 5 rows and done, with rows
arriving in a 4-second batch rather than continuously — a shape that looked like a short, successful
command rather than a truncated long one. The fix (`PollMonitorRound` doing exactly one
request/reply round, with the pass driven by `MonitorLoop` and no time/round cap) is documented in
§21 of the reference file.

### A synchronous read of a monitor window fell through to a plain getall

Before this was special-cased, `ExecuteList`/`LoadList` against a `type:'query'` or `action`+
`pollcmd` window issued an ordinary `getall` on the window's handler:

```
/ping =address=127.0.0.1 =count=2  (WinboxNative, before the fix)
  >> M2 0xFF0001=u32[]:[22] 0xFE000C=u32:268435463        (getall on handler [22])
  << M2 (no 0xFE0002 records)
  → caller sees "OK (no data returned)"                    ← silent failure
```

A monitor window's rows only exist once a client runs the cycle — it is not a table — so the router
answered truthfully (no records) to a request that made a false assumption about the window. The
fix (`RunMonitorWindowSync` running start → poll → cancel synchronously) is in §22 of the reference
file.

### `addr` fields other than plain IPv4 sent as a bare string

The codec's fallback for any `addr`-typed value it didn't specifically handle (a hostname, an IPv6
address) wrote it as a string on the field's own wire key. The router doesn't read `addr` in that
shape — it behaves as if the field never arrived — so pinging a hostname or an IPv6 address failed
with "no address was specified," a message that reads as a complaint about the *value* rather than
about the malformed shape of the request:

```
/ping address=127.0.0.1    >> 0x16=msg:{0xFEFF20=16777343}         ← works
/ping address=example.com  >> 0x16=str:example.com                  ← router: "no address was specified"
/ping address=2001:db8::1  >> 0x16=str:2001:db8::1                  ← same
```

The diagnosis only moved forward once the *requests*, not the replies, were read: the router wasn't
malfunctioning, it was correctly rejecting a malformed query. The real encoding is a compound — each
address shape rides on its own dedicated sub-key inside a nested message (§23.1 of the reference
file).

### IPv6 addresses encoded as the generic `raw` wire type

`FT_ADDR6` (type byte `0x18`) is 16 bytes with no length prefix — unlike every other
variable-width-looking type in the table. Encoding an IPv6 address as `raw` puts a length byte where
the address itself begins, so the router silently drops the field. The parser had the mirror
problem: lacking a `0x18` case, it defaulted to zero length and misread the following bytes as the
next field's key and type, scrambling the rest of the message. The specific symptom this produced —
an apparently empty `0x1=[{}]` on a traceroute reply — looked like "the router returned nothing" but
was actually "our parser desynchronised two fields earlier": a traceroute hop is `union{ip6addr a1
allowipv4, string s2}` inside a `multi`, and the parser was skipping the ip6addr's 16 bytes as zero.

### A traffic-rate field misattributed by GUI label

`.jg` labels both a live *rate* column and a persistent *counter* "Rx" in different windows. The
label-based normalizer mapped `'Rx'` straight to the API name `rx-byte` (a counter), so a native read
of ether1 reported `rx-byte=5536` (the live rate) where the API reported `rx-byte=76024833` (the
actual counter) for the same record at the same moment — the right field name carrying the wrong
value, five orders of magnitude off, with no type error to flag it. The fix (mapping the whole
interface traffic block by wire key instead of by label, §23.3 of the reference file) also fixed
`/interface/monitor-traffic`, which turned out not to be a monitor window at all.

### A read-only list column silently ate a same-named write argument

`secure.jg`'s IPsec 'Keys' window `[85,5]` declares a handler-wide field map (used to decode getall
rows) and, on the same handler, a `doit` action with its own arguments — both containing a field
labelled 'Key Size', one read-only (the list column) and one writable (the action argument). A
catalog built as one field-per-label map for the whole handler kept only the first registration, so
the read-only column shadowed the writable argument; since the encoder drops read-only fields
outright, the argument then encoded to zero bytes. `/ip/ipsec/key/rsa/generate-key name=X
key-size=2048` produced an unnamed, silently-defaulted 1024-bit key and reported success — this was
compounded by a second, independent bug: the dispatcher (`DispatchActionVerb`) wasn't forwarding the
caller's arguments to the router at all, so even before the label collision, the request went out
argument-less. Both are fixed in §25 of the reference file (fields attributed to the action
specifically, and the dispatcher forwarding fields).

### A deck pane's field assumed unique on its handler

`type:'deck'` windows (queue types, logging actions, IPsec identities, …) put several
mutually-exclusive "kinds" behind one selector field, each with its own child fields — several of
which reuse a label across kinds (`'Limit'` in both codel and fq-codel, `'Stop on Full'` in both
memory and disk logging). A catalog keyed strictly by label kept only the first kind registered
under a shared name, so every later kind's same-named field was unreachable, for both reading and
writing, under any name. `QueueTypeTest` was red on four methods before the fix, including one
whose complaint was "Missing field 'name'" — the window actually labels that field 'Type Name'.
Fixed by filing a pane field under both its plain label and a kind-prefixed label (§27.1), with a
separate, non-derivable per-path table for which spelling a *read* should report (§27.2), since two
similarly-shaped catalogs (`/queue/type` and `/system/logging/action`) disagree on whether the API
prefixes the field at all.

---

## Measurements pinned to a moment

| Measured | What | Value |
|---|---|---|
| RouterOS 7.21.4 | `WinboxNativeGetallTest`: getall on `[20,0]` interfaces, `[20,1]` IP addresses, get/set/restore ether1 comment via native | 3/3 passing, values matching the binary API |
| RouterOS 7.23.2 | `/ping` handler `[22]`, `count=30`, poll-continuation trace | one record + token per round, next round blocks ~1000 ms, final round carries `bfe000b=True` |
| — | Full `winboxnative` integration suite after the streaming-monitor + listen + async-list work landed | 163 pass / 0 fail / 81 skip |
| 7.23.2 catalog | Labels ending in a parenthesised number that duplicates the enum key (e.g. `modp1024 (2)`) | 12 labels, all Diffie-Hellman groups |
| 7.23.2 catalog | `deck` windows | ~70 across the catalog; ground truth for the read-spelling table (§27.2) exists for 2 (`/queue/type`, `/system/logging/action`) |
| 7.x catalog scan (`jg_analyze.py`, 18 plugins / 805 windows) | `type:'query'` windows carrying a `pollcmd` | 0 — confirms §21's "query windows never have a pollcmd" rule |

### Test-count evidence for since-fixed bugs (RED before, GREEN after)

- `IpsecKeyTest.GenerateAndDeleteIpsecKeyWillNotFail` — red on `winboxnative` pre-fix (created a
  1024-bit key instead of 2048); `WinboxJgActionFieldTests` pin the catalog scoping router-free.
- `IpProxyTest`, `IpSshTest`, `IpsecProposalTest`, `SystemLoggingActionTest` — red on `winboxnative`
  pre-fix (enum/unset decoding); `WinboxEnumAndUnsetDecodeTests` — nine cases failed against the old
  catalog, two are regression guards for values that must NOT be dropped.
- `QueueTypeTest` (four methods), `SystemLoggingActionTest`, `IpsecIdentityTest` — red pre-fix (deck
  pane field collisions); `WinboxDeckPaneTests` — ten cases failed against the old catalog, two guard
  what must not change.
- `VerbMatrixTest.CrossTransport_AddressValues_MatchTheBinaryApi` — red pre-fix (range/opt-flag
  bugs); `WinboxAddressRangeTests` and `WinboxJgFieldFlagTests` pin the rules router-free.

## Field-vocabulary gaps closed against the binary API

Moved here from `protocol-coverage.md`, which had recorded these as open gaps.

### WinBox native — three "field vocabulary" gaps recorded as pending, all since fixed

`Docs/protocol-coverage.md` used to carry a four-row table of WinBox-native field-vocabulary gaps
against the binary API, dated 2026-08-15. Three of the four rows are now fixed; only the
kind-scoped-parameter class (partially) and two unrelated single-path gaps remain open. Recording
the fixed three here so the diagnoses aren't lost:

**Enum/set/sentinel values reached the caller as raw wire form instead of the API's value**
(`/ip/proxy` port printed as `[8080]`, `/ip/ssh` ciphers as `[0]`, `/ip/ipsec/proposal` pfs-group as
the bare `2`, `/system/logging/action` syslog-severity as `4294967295`, `/ip/proxy/access` method as
`''`). Six live failures, all the same shape. Cause: RouterOS wraps a field's static enum map in
`enumfilter` (board-gated membership), `defenum` (a sentinel id/name in front of the list), and
`pair` (a static list beside a dynamic table), and nests them — an IPsec proposal's PFS group is
`enumfilter -> defenum -> static`. Reading only the top level left the field with no map in either
direction. Fixed by walking the whole chain (deliberately not walking the *runtime-computed*
wrappers `queryenum`/`offsetenum`/`slotenum`/`remapenum`, whose members come from a live query, not
a static list). Two more causes bundled into the same fix: a `multinumber` of *literal* values (not
references) — `/ip/proxy` port `[8080]`, `/ip/ssh` ciphers `[0]` — had only its reference flavour
decoded, so it round-tripped as the array's bracket text instead of per-element values; and "not
set" is an *absent* field on the wire (an opt-wrapped flag left down, or the catalog's declared
`0xFFFFFFFF` marker), not an empty one — `/ip/proxy/access` with only `dst-host`/`action` set still
carries `method`/`src-address`/`dst-port`/`path` keys over M2 with their flags down, and the API
prints none of them; the client was printing `method=''`. Fixed by treating only the marker as
unset — a proposal's `def:1800` lifetime prints as a value, and a *named* def
(`max-cache-size=unlimited` is `0xFFFFFFFF` on the wire, but the catalog names it) stays a value
too. Verified live on RouterOS 7.23.2: native failures 14 → 8 (remaining 8 are write-side, a
different class); WinboxNativeMac identical; API unaffected at 361/361.

**Kind-scoped parameters — a `queue/type`/`system/logging/action`-shaped pane collision, and a
correctness bug, not just a naming mismatch.** WinBox hides per-kind settings behind a `type:'deck'`
UI (a pane chosen by another field); RouterOS has no panes — one flat record, parameters prefixed by
kind. Because panes reuse labels and the `.jg` catalog is keyed by label with first-wins, a whole
kind's fields could be **silently dropped from the map** if an earlier pane's label collided — e.g.
`codel`'s Limit/Interval/Target/ECN/CE-Threshold survived and `fq-codel`'s five same-named fields
were dropped entirely, unwritable; same for `disk`'s "Stop on Full" losing to `memory`'s. Fixed by
filing a pane field under both `<kind>-<label>` and its plain label (kind read live from the
selector's own enum map), and by making a decoded record report only its own kind's pane instead of
every pane's keys (which is also what fixes `/ip/ipsec/identity`, whose "My ID" pane covers three
types). What spelling a **read** should report is not mechanically derivable from the label alone —
`/queue/type` prefixes every pane; `/system/logging/action` prefixes memory/disk/email but calls the
remote pane's fields `src-address`/`syslog-facility` — so the correct read spelling for those two
paths is now a shipped, verified per-path table; every other deck window (there are roughly 70 in
the 7.23.2 catalog) still decodes by the derived rule with no ground truth checked against the live
router. `/queue/type` previously had an O/R-mapper entity and no test, which is how a native read
could report a queue's depth as `queue-size` and a write of `pcq-rate` could silently do nothing
without any test going red. Verified live on 7.23.2: native failures 8 → 6; WinboxNativeMac the
same.

**Action-window field collision — `/ip/ipsec/key/rsa` generate-key silently used the wrong
arguments.** `generate-key name=X key-size=2048` over the native transport produced an unnamed
1024-bit key and reported success. Two silent causes stacked: the action dispatcher invoked the
`.jg` `SYS_CMD` with `fields: null`, so every caller-supplied argument was dropped before the
request was built, and the router took a bare "generate a key" at face value; and even if the
arguments had been passed, `secure.jg` labels a read-only "Key Size" list column and a writable
"Key Size" enum with the *same* label on one handler — merged into one per-handler field map by
label, the read-only column won, and the encoder drops read-only fields. Fixed by attributing a
`doit`/action window's fields to the action as well as to its handler, with the action's field
having the last word on an invocation, while a `getall` row still decodes against the handler's map
(the one map a standalone action window with no backing list, e.g. Wake-on-LAN, ever had). Same
shape as the earlier interface-subtype fix: fields belong to a *window*, not to its handler.
Verified live on 7.23.2 with `IpsecKeyTest.GenerateAndDeleteIpsecKey` (red before, green after).

### WinBox native field encode/decode — reference lookups moved off the awaited thread

Field encoding/decoding for a referenced record (translating a name to/from its numeric id via a
`getall`) originally ran that lookup synchronously on the thread awaiting the command. It no longer
does: the encoder now runs in two passes (a discard pass that only records which lookups it needs,
then one awaited batch of `getall`s, then a real pass against the answers) and the decoder runs a
collecting pass first to learn which tables a row will need before fetching them. An earlier version
of the prefetch tried to *predict* which tables a row would need straight from the `.jg` field map
instead of asking the decoder — that prediction could diverge from what decode actually consults,
and because the id → name map is cached for the connection's lifetime, one such wrong prefetch
poisoned every record decoded afterwards, rendering it as a bare numeric id instead of a name.
Printing `/interface/list` (whose own rows reference interface lists) was enough to trigger it. Fixed
by asking the decoder itself, in the same collecting mode, rather than re-deriving the answer.
