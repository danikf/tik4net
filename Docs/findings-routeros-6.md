# RouterOS 6 — findings and open problems

What RouterOS 6 does differently from 7, as measured on CHR 6.49.13 (long-term, x86_64) — the lab's second
router, CHR2 (`tik4net.integrationtests/README.md`, *The lab routers*). The first half is ground truth and what
tik4net does about it; the second is the list of problems still open, to be taken one at a time.

## 1. Where each transport stands

The smoke subset (`ConnectionTest`, `SystemClockTest`, `InterfaceListTest`, `IpRouteTest`) against 6.49.13:

| Transport | Result | What fails |
|---|---|---|
| `Api`, `ApiSsl` | all pass | — |
| `Telnet`, `Ssh`, `MacTelnet` | all pass | — |
| `WinboxCli`, `WinboxCliMac` | all pass | — |
| `WinboxNative`, `WinboxNativeMac` | 20 of 21 | route `scope` is not in the record; the path-map audit finds more — open problem 1 |
| `Rest`, `RestSsl` | cannot work | RouterOS 6 has no REST API (§3) |

Beyond the smoke subset, the path-map audit has been run over every transport 6.x has (open problems 1 and 4),
and `CliFlagFieldsTest` over every CLI transport (open problem 4).

## 2. `print` has no `proplist=`

RouterOS 6 does not know the argument at all: `:put [/ip route print as-value proplist=active]` answers
`expected end of command (line 1 column 32)`, the column `proplist=` starts on. The same command without it is
accepted. As on 7.x before 7.20, `print as-value` carries no flag fields (`findings-cli.md`), so the flags cannot be
read by name either.

A flag is readable as the ids it is set on: `:put [/ip route find (active=yes)]` answers `*30000001;*4206a543`,
and nothing when no row has it. The parenthesised clause is the `where` grammar, so a boolean is `yes`/`no`.
Verified for `/ip route active`, `/interface running`, `disabled` and `dynamic`.

tik4net's read path: once per connection, and only on a router that already leaves its flags out of
`as-value`, `CliConnectionBase` asks `:put [/ip service print as-value proplist=name where false]`
(`CliCommandBuilder.ProplistSupportProbe`). An `expected end of command` answer means no `proplist=`; the
flags an entity maps are then read with one `find (<flag>=yes)` per flag and every row is given each one
explicitly, merged by `.id`. A flag the menu does not have is refused, asked once, and left out.

The one exception is the route's origin. The binary API sends every other mapped flag as an explicit
`true`/`false` or not at all (every mapped menu with rows, 6.49.13), but a route's origin is one numbered field
(§4), and the API names only the member the row is: the connected route carries `connect=true` and no `static`,
the DHCP-installed one the reverse. The id-list read leaves the other members absent in the same way
(`CliConnectionBase.FlagsSentOnlyWhenSet`: `connect`, `static`, `rip`, `ospf`, `mme`, `bgp`).

What counts as a flag is the entity's declaration: `disabled` and every read-only `bool`. A flag declared as a
writable setting is never asked for, so it reads absent over CLI. `default` on `/queue/type` and
`/routing/bgp/instance` is read-only for that reason. Neither menu's `set` completes it, and the API prints it on
every row.

**The binary API's `.proplist` stops at the first unknown name.** RouterOS 7 ignores a name the menu does not have.
6.49.13 drops every field listed after it: `/ip/route/print =.proplist=.id,connect,static` returns the origin flags,
`=.proplist=.id,dhcp,connect,static` (6.x has no `dhcp` origin) returns `.id` alone, and any unknown name does the
same (`t4n-nothing`). The library builds no `.proplist` of its own for an entity; a caller's is sent as written, so a
list meant for both versions puts the names only the newer one has last.

## 3. No REST API

`/rest` arrived in 7.1. On 6.49.13 the `www` service serves webfig only, and every REST request gets its HTML
404 page. `RestConnection` refuses that as `TikNoSuchCommandException` naming the version floor, rather than
reading a 404 as "no such item" — which for a print looks exactly like an empty table.

## 4. WinBox native: the catalog, and the windows that moved

The `.jg` catalog downloads from 6.49.13 exactly as from 7.x: the `list` names 12 plugins (`roteros`,
`roting4`, `ipv6`, `dhcp`, `ppp`, `secure`, `mpls`, `hotspot`, `wlan6`, `ups`, `advtool`, `dude`) and all 12
arrive as `<unique>.gz` over the static-file open (mproxy `[2,2]`, command 7). The plain name under `/var/pckg`
(command 3) is refused — by 7.x CHRs too — so a client that takes that route sees none of them.

What differs is where some windows sit. Routes are WinBox's *Route List* (IP menu; tabs Routes, Nexthops,
Rules, VRF): the window 'Route' on `[44,1]`, IPv4 only, deriving to the menu-label path `/ip/routes/route`.
IPv6 routes are their own window on `[44,12]` (`/ipv6/routes/ipv6-route`). 7.x has one routes table `[44,21]`
under the 'Routes' windows instead, and its hidden 'All Routes' window derives to the same
`/ip/routes/route` — so the 6.x label is right only where the 7.x one is absent. `WinboxHandlerMap` keeps
such labels as a fallback (`OlderCatalogAlias`), tried only when the router's catalog lacks the primary
alias target.

The other windows that moved: **CAPsMAN** is a top-level menu (`/capsman/caps-*`, where 7.x has
`/wireless/capsman/caps-*`), the **wireless** tables sit one level up (`/wireless/<leaf>`, 7.x
`/wireless/wireless/<leaf>`), BGP is the 6.x model (`/routing/bgp/bgp-instance`, `-peer`, `-network`), IP
accounting is `/ip/accounting/traffic-accounting` and `-web-access` (7 removed the menu), and the NTP client is
the SNTP client (`/system/sntp-client/sntp-client`). The two interface lists are subtypes of the generic
interface table on both versions, with the same discriminator: *Wireless* is subtype 35
(`/wireless/wireless`, 7.x `/wireless/wireless/wireless`) and *CAP Interface* subtype 61
(`/capsman/cap-interface`). They derive only once `roteros.jg`, which declares the generic interface window, has
been parsed — `WinboxJgCatalog` parses it first for that reason; a catalog built from the plugins in `list`
order has no key for either. WinBox has no window for `/ip/accounting/uncounted`: the word is in none of the
twelve plugins.

**A singleton window can name its own commands.** *Traffic Accounting* on `[46]` declares `getcmd:2,
setcmd:1`, *Web Access* on `[50]` `getcmd:1, setcmd:2`; get-singleton on either is refused with `0xFE0003`.
webfig reads with `getcmd || 0xfe000d` and writes with `setcmd || 0xfe000e`, the message otherwise the
standard one (`ObjectHolder`, 6.49.13 `master-min.js`), and `WinboxJgCatalog` records both per window —
per window, because `[46]` also hosts the snapshot list. On 7.x only windows the library does not map name a
`getcmd` of their own.

**A route record leaves Scope and Target Scope out.** The window declares both (`uf` `def:30`, `u10` `def:10`),
but neither key arrives in any row, with or without the statistics flag. It is not the default standing in
for itself: the API prints `scope=30` for the default route and `scope=10` for the connected one, while the
record lacks the key on both — so filling in `def` would make the second row wrong.

**Some fields carry another label, and some keys no label.** The address list's own key is 'Name' where 7.x
says 'List', the OSPF area's name is 'Area Name', and the BGP instance's router id is 'Router ID' where 7.x
has 'IP'. A shipped field alias names the label, and where a catalog lacks the aliased label but has the API
name itself, the API name is used (`WinboxFieldResolver.AliasToJg`) — so one alias serves both versions
without falling through to a generic key. Other keys the 6.x windows do not declare at all, yet the router
sends them under the numbers 7.x declares: `default-name` at `0x10031` on `/interface`, and conntrack
`total-entries` at `0xF` (enabling tracking moved it from 0 to 9 with the API). Both are shipped as
synthetic fields identical to the 7.x declaration.

**The API renamed some fields in 7, over the same key and label.** `/ip/service` prints `address` where 7
prints `available-from` (`0x6`), `/tool/e-mail` `address` where 7 prints `server` (`0x1`), and the OSPF area's
state flag `0xFE0008` is `invalid` where 7 says `inactive`. Nothing in either catalog distinguishes these, so
native reports **both words** for the field (`FieldAliasSet.AlsoKnownAs`): the read is a superset of that
router's API by exactly the other version's spelling, and the mapper takes the name its entity declares. The
write side needs no second word — the alias maps it onto the label, which both catalogs have. No version is
asked for or compared, per the version-support rule.

**A logging action's Syslog Severity is `auto` when unset, on both versions.** The stock remote action carries
`4294967295` and the API prints `syslog-severity=auto` for it — 6.49.13 always, 7.24 once
`remote-log-format=syslog`. Before that, 7.24's API hides the whole syslog group (facility, severity, time
format): the window marks them `on:'bsd'`, a condition 7.24's API applies and 6.49.13's does not
(`bsd-syslog=false` there, and the fields still print). Native applies it to none of the remote pane's fields,
so it reports `syslog-facility` and `syslog-severity=auto` on a 7.24 remote action whose API row has neither
(`WinboxRecordCodec.SentinelSpelledAsWord`).

**The route's origin is a `numflag`, and each catalog names the flags its version has.** RouterOS 6 declares
`{numflag,id:'u7',c:{2:['connected','C'],3:['static','S'],4:['RIP','r'],6:['OSPF','o'],7:['MME','m'],
8:['BGP','b']}}`; 7.24 declares the same shape at `u112`, spelling the first member 'connect' and adding
`DHCP`, `VPN`, `SLAAC`, `ISIS` and more. The row is the member its value names and the API prints that one,
so the decode emits it and nothing for the others — measured on 6.49.13 beside the API: the connected route
carried `0x7=2` with `connect=true`, the DHCP-installed default route `0x7=3` with `static=true` (RouterOS 6
calls the DHCP client's route static and has no `dhcp` origin at all — and cannot read one, because its
catalog declares no such member). Only the members a path names as API flags are decoded
(`FieldAliasSet.NumFlagMembers`): the route's `connect`, `static`, `dhcp` and `active`, and history's
`undoable` — measured on both versions; the unmeasured origins (`bgp`, `ospf`, …) and 7.24's Contribution
members `filtered`/`unreachable`, which its API does not print, are not. `numflag` is documented in
[jg-catalog-format.md](jg-catalog-format.md#key-namespace).

**The RouterOS version is not read, but the key that looks like it is a trap.** Key `0x16` of the system-info
singleton `[13,4]` is the **WinBox protocol's** version — `3.30` on 6.49.13, `3.42rc1` on 7.24.4 — so anything
parsing it as RouterOS's sees major 3 on every router. The RouterOS version is `s16` of the board-info
singleton `[24,2]`, which is where webfig's `fetchBoardInfo` reads it
(`WinboxNativeM2Operations.GetRouterVersion`, which nothing calls).

## 5. Entities: renamed fields, fields 6.x does not have

**A renamed field is one property with several names.** `IpService.Address` reads `address` (6.x) or
`available-from` (7.x), `ToolEmail.Server` reads `server` (7.x) or `address` (6.x)
(`TikPropertyAttribute.AlternateNames`), and saves under the name it was read under. Each version refuses the
other's name on a write — 6.49.13 with a bare `unknown parameter` that names no field — except that 7.24 still
accepts `/ip/service address`, which is why that entity declares `address` first. A filter spelled with the 7.x
name silently matches nothing on 6.x, over the API and the CLI alike; 7.24 accepts the old name in a filter.

**A field 6.x does not have reads as the entity's default.** The mapper fills a field the row lacks with the
property's default, so `ToolEmail` loaded from 6.49.13 reads `tls=no`, `certificate-verification=no`,
`vrf=main` — none of which that router has. A loaded entity saves only what changed, singletons included, so
those values are never written back; an entity saved without being loaded sends everything it holds, and on 6.x
that is refused as soon as it holds a 7.x-only field.

**A menu can change shape.** `/interface/ovpn-server/server` is one unnamed server on 6.49.13 (no `.id`, no
`add`) and a list of named servers on RouterOS 7. `OvpnServer` maps the list; on 6.x `LoadAll` reads the one
server as a row with a `null` `Id`, which cannot be saved through the entity.

## 6. Login: the banner quotes the router's critical log

After the password, RouterOS 6 prints the account's critical log entries it has not shown yet under the banner,
on a login that succeeds — and a failed login elsewhere is such an entry:
`sep/24/2026 19:26:05 system,error,critical login failure for user admin from … via api`. Each entry is shown
once. The CLI login therefore decides a refusal after the password by position only — RouterOS restarts the
dialogue with `Login:` — and never by the words on screen (`RouterOsCliLogin.ResolveToPromptAsync`); the phrase
list is consulted only before the password is sent. A wrong password is still refused in about 1.2 s over Telnet
(`Login failed, incorrect username or password`, then `Login:`), on 6.49.13 and 7.24.4 alike.

Seen on the way, the router's and not ours: an account with an EMPTY password is let in over SSH whatever
password the client offers (the SSH `none` method succeeds), on 6.49.13 and 7.24.4 alike.

## 7. `/system/resource` with `.proplist=cpu` drops the API session

`/system/resource/print =.proplist=cpu` makes 6.49.13 close the API connection (`cpu-count,cpu-frequency` alone
are answered; a plain print is answered). `SystemResource` sends no `.proplist`, so no mapped load reaches it —
only a raw command that names the field.

## 8. Monitors have no `as-value`

`:put [/ping address=<a> count=2 as-value]` is refused on 6.49.13 with `expected end of command (line 1 column 43)`
— column 43 is `as-value` — and `/tool traceroute … count=1 as-value` the same. The plain command prints its
table and ends:

```
  SEQ HOST                                     SIZE TTL TIME  STATUS
    0 192.0.2.1                                  56  64 0ms
    1 192.0.2.1                                  56  64 0ms
    sent=2 received=2 packet-loss=0% min-rtt=0ms avg-rtt=0ms max-rtt=0ms
```

A one-shot monitor read over a CLI transport sends the `as-value` form; when the whole answer is one parse
error, it sends the plain form and reads the table by column (`CliTableParser`, the streaming monitors' parser),
and asks that command in the plain form for the rest of the connection. The rows carry what the table prints
(`seq`, `host`, `size`, `ttl`, `time`, `status`); the summary line is not a row, as on 7.x.
`RomonRelayTest.Relay_SyncMonitor_ReturnsRows_AndKeepsTheRelay` covers it through the relay on Telnet, SSH and
MAC-Telnet.

A polled stream of a continuous monitor (`/tool profile`, `ExecuteWithCallback` over a CLI transport) takes the
same snapshot every round, so it falls back the same way.

**Torch has no `proplist=`.** `:put [/tool torch interface=ether1 duration=4 freeze-frame-interval=2 proplist=…]`
is refused at the column `proplist=` starts on (`expected end of command (line 1 column 71)`); without it the
command prints its plain table, even inside `:put`: frames between `-- [Q quit|D dump|C-z pause]` lines, a header,
one row per flow, and a last row with only `TX`, `RX`, `TX-PACKETS`, `RX-PACKETS` — the totals. The columns are the
ones the arguments ask for: `interface` alone prints the totals only, `src-address=0.0.0.0/0 port=any
ip-protocol=any` adds `MAC-PROTOCOL`, `IP-PROTOCOL`, `SRC-ADDRESS`, `SRC-PORT`, `DST-PORT`. That is what the binary
API returns for the same arguments, totals row included, and a port keeps its service name there too
(`dst-port=8291 (winbox)`). The rates are right-aligned under their header (`1664bps` starts left of `TX`), so the
table is read by which header a value overlaps rather than by the column spans `CliTableParser` uses
(`CliOutputParser.ParseTorchTable`).

## Open problems

Each is a statement of what is measured and what is not, to be settled one at a time.

1. **WinBox native against the API on 6.x.** Measured with the path-map audit (`TransportPathMapAuditTest`,
   WinboxNative, against CHR2, 2026-09-27): OK 137, unmapped 0, value differences 5, field-name mismatches 0,
   not on this RouterOS 20, no WinBox window 2; writes OK 184 with no value differing, refused 0, not probeable
   54 (the router refused the row on both transports). 26 of 1175 API field names are never reported over
   native (2 %). The same audit against 7.24.4 is unchanged by the 6.x fixes (OK 154, nothing differing). Each
   part below is separate work:

   - **1a. Mapping is complete; two lists are unproven beyond it.** Every path the audit covers now reaches
     its window. CHR2 has no wireless and no CAP interface, so for `/interface/wireless` and
     `/caps-man/interface` what is shown is only that the subtype filter applies (no rows, where the
     unfiltered interface table has two) — not that their fields decode.
   - **1b. Fields native does not report, on paths that otherwise agree.** Reported now, under the API's name
     where the 6.x window spells it out: `/ip/accounting` `enabled` (*Enable Accounting*), `/system/ntp/client`
     `primary-ntp`, `secondary-ntp` (*Primary/Secondary NTP Server*) and `last-update-before` (*Last Update*),
     `/routing/bgp/instance` `ignore-as-path-len`; the address list's
     `list`, the route's `pref-src` and the OSPF area's `name` (6.x labels them 'Name', 'Pref. Source' and
     'Area Name'), the mangle rule's `route-dst` (6.x labels it 'Dst. Address', the matcher's own label, so it
     lost the name; shipped as a synthetic field on `u3f4`, read and written), `default-name` and conntrack `total-entries`, keys the 6.x windows do not declare but
     the router sends, the route's `connect` and `static` (its origin `numflag`), and the fields the API
     itself renamed in 7 — `/ip/service` `address`, `/tool/e-mail` `address`, the OSPF area's `invalid` —
     and the remote action's `syslog-severity=auto` (§4). Still missing, each for a reason of its own:
     - `/ip/route` `scope`, `target-scope`: the router does not send them (§4).
     - `/ip/route` `gateway-status`: the API's `<gateway> reachable via  ether1` is composed from the
       gateway tuple's read-only parts (status enum, `via` interface), which the decode drops.
     - `/ip/route` `vrf-interface`: no key in the record identified.
     - `/system/logging/action` `syslog-time-format`: left unmapped on purpose (see the resolver's
       `/system/logging/action` entry).
     - `/ip/neighbor` `system-caps`, `system-caps-enabled`: 6.x sends `0x11`/`0x12`, the 7.x keys, but only
       as 0, which proves no pairing (an empty set matches anything); needs an LLDP neighbour.
     - `/ip/ipsec/active-peers` `natt-peer`: the 6.x window has no 'NATT Peer' (7.x: `be`); needs a peer.
     - Shared with 7.x, not a 6.x matter: `/interface` `fp-*` counters, `/interface/bridge/port`
       `debug-info` and `hw`.
   - **1c. Field names that differ.** `/routing/ospf/instance` reads under the API's names (the 6.x window
     spells them *Redistribute Connected Routes*, *Static Routes Metric*, …) and its `distribute-default`
     members without the window's parentheses. `metric-bgp` and `metric-other-ospf` arrive as 4294967295 at
     their default and read `auto`, as the API prints them. Still missing there: `state` (the API's `down`
     against the window's *Running* bool `u65`=0; only `down` has been seen, so no mapping is shipped).
     `/ip/dhcp-server/config` `accounting` and `interim-update` are keys the 6.x window does not declare but the
     router sends (7.x's `b3`/`u2`, confirmed by setting both and watching the keys move).
   - **1d. Values rendered the 7.x way.** Dates: the 6.x API prints `sep/21/2026`, native `2026-09-21`
     (`/system/clock` `date`, `/system/scheduler` `start-date`). Timestamps left as epoch seconds:
     `/certificate` `invalid-before`/`invalid-after`, `/tool/netwatch` `since`. `/system/package` `bundle`:
     `routeros-x86` against `1` — the window's unnamed `u6` holds the parent package's record id, and native has
     no way to resolve a reference to a row of the same table (no entity maps the field). The enum spellings
     (`advertise`, `cipher`) and the queue limits' `0/0` now read as the API prints them.

     **Dates and timestamps are postponed to the typed values of 5.0, deliberately.** The difference is not
     native's: the binary API itself prints a date `sep/21/2026` on 6.x and `2026-09-21` on 7.x, so "the API's
     spelling" is a per-version target, and the library does not read the router's version. What is
     version-neutral is a typed date property that reads either spelling — the value-type work of the 5.0
     entity model. Until then a date or timestamp is a string, spelled as the transport delivered it.
   - **1e. Not a defect: enabling the audit's `/ip/dhcp-server` row** is refused over the API as well
     (`can not run on slave interface` — on CHR2 the fixture's interface is a bridge port).

   Unknown for 1b's route fields: whether any request makes 6.49.13 send Scope and Target Scope — another
   getall flag, a `get` of the single row — since WinBox 6 itself shows a Scope for these routes; a capture of
   it reading them is the ground truth. The audit report is written per transport, not per router, so a run
   against CHR2 replaces the 7.x report of the same transport.

2. **Settled: REST legs are not run against 6.x.** MikroTik moves everyone to RouterOS 7, so a REST test failing on a
   6.x router is the expected answer, not a gap worth gating on the version. The `chr2` router profile carries
   `chr2.noRest`, and `run-integration-tests.ps1 -Router chr2` leaves the `rest` and `restssl` legs out (named
   explicitly, they are refused). `CliFlagFieldsTest`, the one test meant to run against CHR2, reports its REST row
   Inconclusive on the router's refusal (§3).

3. **The pre-7.20 flag path of RouterOS 7 has no lab router.** CHR2 on 7.19.6 was the one router where flags
   are read by name through `proplist=`; on 6.49.13 the id-list path runs instead, and CHR runs neither. The
   by-name path now has unit coverage only (`CliFlagFieldsTests`). To decide: a third CHR on a 7.x before 7.20,
   or accept unit coverage for it.

4. **Coverage beyond the smoke subset.** Measured 2026-09-23/24 against 6.49.13 (the same audit against
   7.24.4 is clean on all ten transports):

   | Audit transport | OK | MISMATCH | VALUE-DIFF |
   |---|---|---|---|
   | ApiSsl | 144 | 0 | 0 |
   | Telnet, Ssh | 132 | 9 | 3 |
   | WinboxCli | 92 | 50 | 2 |
   | WinboxCliMac | 114 | 27 | 3 |
   | MacTelnet | 7 | 137 | 0 (the session was lost after 7 paths — problem 7) |
   | WinboxNative (2026-09-27) | 137 | 0 | 5 (problem 1) |
   | WinboxNativeMac (before the 2026-09-27 fixes) | 132 | 2 | 8 |

   - **Flags: the audit's raw rows lack them, entities do not.** Over every CLI transport the audit's print has
     no `disabled`/`dynamic`/`invalid`/`running`/`slave`; the entity read gets them through the id-list path
     (§2), and `CliFlagFieldsTest` agrees with the binary API over all five CLI transports and WinboxNative,
     presence included; its REST row is Inconclusive there (§3).
   - **Counters: the same.** The audit's CLI rows also lack `bytes`, `packets`, `rx-byte`…; the entity's
     `IncludeCliStats` read fills them — `Interface` counters over Telnet, Ssh and WinboxCli track the binary API's
     on 6.49.13 as on 7.24.4.
   - **Telnet/Ssh value forms:** bridge `priority` `0x8000` (API) against `32768` (CLI), port `0x80` against `128`;
     `/routing/ospf/instance` `metric-bgp`/`metric-other-ospf` `auto` against `4294967295`. The CLI prints an
     empty `comment=` for a row with none, where the API omits it; the CLI read drops it
     ([findings-cli.md](findings-cli.md) §1).
   - **WinboxCli/WinboxCliMac** added incomplete reads and refusals with the prompt text inside them: 6.x repaints
     the typed line after every character, and the read stopped on a prompt inside that echo
     ([findings-cli.md](findings-cli.md) §4). Every entity now reads over WinboxCli as over Telnet — the audit's
     raw reads were not re-run.
   - **Every entity, one session, 2026-09-24** (`LoadAll`/`LoadSingle` of all 163 readable entities): Telnet and
     WinboxCli read all but `/routing/bgp/advertisements` (problem 9) and the
     menus 6.x does not have. WinboxCliMac and MacTelnet added `/ip/ipsec/policy` and `/system/package`, a
     windowed print that names no row (problem 8, settled), and MacTelnet a run of 30 s timeouts (problem 7,
     settled).
   - Not a finding: the audit's "refusing to CLEAR the field" lines appear against 7.24.4 too.
   - Still not run: the full suite — CHR2's missing topology will fail tests for reasons that are not defects, so
     its failures need sorting before any counts as a 6.x gap.

   **`RomonRelayTest` with the 6.49.13 target: 28 of 37 pass (2026-09-25).** The relay itself works over all three agent
   transports. The rest:
   - 9 Safe Mode tests: they check the target's state over its own API with `/safe-mode`, which is `no such
     command` on 6.x. Unknown: whether Safe Mode through the relay works there and only the check does not.
   - 1 large read over MAC-Telnet: the id-list flag query on the 450-row table,
     `:put [/ip firewall address-list find (dynamic=yes)]`, was refused as incomplete after four MAC backlogs
     on 2026-09-24 and passed on 2026-09-25; the same read passes over Telnet and SSH.
   - Not a gap of the target's CLI: the relay's `waiting for head` status line, spliced into about one large read in
     fifteen, is removed by the relaying transports ([findings-romon.md](findings-romon.md) §4).

5. **Two MCP-side gaps seen while measuring.** `mikrotik_call` over `MacTelnet` failed against both routers,
   6.x and 7.x alike, while the suite's own MAC-Telnet legs passed — so the server, not the router; it was a
   staged copy older than the library, so re-check after a reconnect first. And `mikrotik_call` over `ApiSsl`
   rejects the lab's self-signed certificate on both routers, with no option to accept it the way the suite's
   `restAllowInvalidCert` does.

6. **Settled: CLI completion of a shared prefix on 6.x.** `/interface bridge add frame-types=` completes inline to
   `admit-` on both versions; asked again with that prefix, RouterOS 7 lists the three `admit-*` values on the first
   Tab, while 6.49.13 only echoes the line and lists them on a **second** Tab (raw Telnet). `CompleteCli` now sends
   the second Tab when the first changed nothing; verified over Telnet and WinboxCli on 6.49.13, and over Telnet on
   7.24.4. The four enum vocabularies it left unmeasured match the entities on 6.49.13: `frame-types`, vrrp
   `v3-protocol` (`ipv4`, `ipv6`), security-profile `static-transmit-key` (`key-0`…`key-3`), and bonding
   `transmit-hash-policy`, which lists only `layer-2`, `layer-2-and-3`, `layer-3-and-4` — the `encap-*` members are
   7.x additions. Completion answers only for the SPACE form of a menu path on 6.x
   (`/ip firewall filter add action=`); after the slash form it answers nothing, a second Tab included, so a caller
   must use spaces.

7. **Settled: MAC-Telnet on one long session.** 6.49.13 repaints the whole typed line after every character of a
    command, and MAC-Telnet logged in without the `+c` every other PTY login uses, so the repaint came in colour:
    ~65 KB of echo for one 180-character window command. Under that volume the router's MAC-Telnet server
    re-sends packets we had already acknowledged — even ones whose exact ACK it had been sent — and then drops its
    own output: the echo stops mid-escape-sequence, nothing is re-sent, and the read waits 30 s at 17–24 KB (the
    MAC backlog-replay drop, [findings-mactelnet.md](findings-mactelnet.md)). The terminal user name now carries `+c`
    (the EC-SRP5 exchange keeps the bare name, and RouterOS accepts it). The one-session sweep of every entity over
    MAC-Telnet on 6.49.13, before → after: 21 load errors → 0, 55 → 62 entities with rows (the API's 62),
    856 s → 91 s. The uncoloured repaint is still quadratic in the command's length (~19 KB for 180 characters).

8. **Settled: a windowed print that names no row.** On 6.49.13 `print as-value from=<ids>` leaves `.id` out on
    `/system/package` (13 rows, no id in any) and `/ip/ipsec/policy` (its one row carries `.nextid=*ffffffff`
    instead), while the unwindowed print of both carries it and `/interface` keeps it windowed too. The parser
    starts a record at each `.id=`, so the packages read as one row and the policy as a row with no id. Seen only
    over the MAC transports because they are the ones that page (Telnet read the same menus unpaged); the
    router prints the same over Telnet when asked the windowed command (telnet-cli-probe). A window whose answer holds rows but no `.id` is now taken
    again one id at a time — `:foreach i in=$w do={ :put (".id=" . $i . ";" . [:tostr [print … from=$i]]) }` —
    and the menu is remembered for the connection; the `.nextid` is dropped, as the API does not print it there.

9. **Settled for an empty table: `/routing bgp advertisements print` has no `as-value` on 6.x.** Its `print` completes
   only `file`, `interval`, `peer` and `where` (6.49.13), so the read's `as-value` — and the `without-paging` the terminal
   transports add to a bare print — are taken for a peer name: `input does not match any value of peer`. That whole
   answer is now reported as the router's error rather than as an incomplete read, and an unfiltered read that gets it
   asks `:put [/routing bgp advertisements print]` instead: a script, so neither paged nor given `without-paging`, which
   still prints the table (`PEER PREFIX NEXTHOP AS-PATH ORIGIN LOCAL-PREF`). It is read by column (`CliTableParser`, as
   §8), and the menu is remembered for the connection. Verified over Telnet and WinboxCli with no peer: no rows, as the
   binary API. Not measured: a row. The table carries no `.id` and none of the API's other fields (`communities`, …), so
   `BgpAdvertisements`, whose `.id` is mandatory, would refuse a row read this way; measuring one needs a BGP peer
   with advertisements.
