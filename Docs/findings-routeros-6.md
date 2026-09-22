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
| `WinboxCli`, `WinboxCliMac` | 18 of 21 | the clock read and two adds — open problems 2 and 3 |
| `WinboxNative`, `WinboxNativeMac` | 20 of 21 | route `scope` is not in the record; the path-map audit finds more — open problem 1 |
| `Rest`, `RestSsl` | cannot work | RouterOS 6 has no REST API (§3) |

Beyond the smoke subset, only WinBox native has been measured, by the path-map audit (open problem 1); the
rest is open problem 6.

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

## Open problems

Each is a statement of what is measured and what is not, to be settled one at a time.

1. **WinBox native against the API on 6.x.** Measured with the path-map audit (`TransportPathMapAuditTest`,
   WinboxNative, against CHR2): OK 132, unmapped 0, value differences 8, field-name mismatches 2, not on this
   RouterOS 20, no WinBox window 2; writes OK 184 with no value differing, refused 1, not probeable 53 (the
   router refused the row on both transports). 41 of 1174 API field names are never reported over native
   (4 %). Before the fallback labels and the windows' own commands (§4) it was OK 111, unmapped 22, writes
   refused 27. Each part below is separate work:

   - **1a. Mapping is complete; two lists are unproven beyond it.** Every path the audit covers now reaches
     its window. CHR2 has no wireless and no CAP interface, so for `/interface/wireless` and
     `/caps-man/interface` what is shown is only that the subtype filter applies (no rows, where the
     unfiltered interface table has two) — not that their fields decode.
   - **Newly reached paths that still disagree** (same kinds as 1b–1c): `/ip/accounting` `enabled` (6.x labels
     it *Enable Accounting*), `/system/ntp/client` `primary-ntp`, `secondary-ntp`, `last-update-before`,
     `/routing/bgp/instance` `ignore-as-path-len`.
   - **1b. Fields native does not report, on paths that otherwise agree.** Reported now: the address list's
     `list`, the route's `pref-src` and the OSPF area's `name` (6.x labels them 'Name', 'Pref. Source' and
     'Area Name'), `default-name` and conntrack `total-entries`, keys the 6.x windows do not declare but
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
   - **1c. Field names that differ.** `/routing/ospf/instance`: the 6.x API says `redistribute-connected`,
     `metric-static`, `distribute-default`; native derives `redistribute-connected-routes`,
     `static-routes-metric`, `redistribute-default-route` from the 6.x labels. `/ip/dhcp-server/config`:
     `accounting` and `interim-update` are not reported.
   - **1d. Values rendered the 7.x way.** Dates: the 6.x API prints `sep/21/2026`, native `2026-09-21`
     (`/system/clock` `date`, `/system/scheduler` `start-date`). Timestamps left as epoch seconds:
     `/certificate` `invalid-before`/`invalid-after`, `/tool/netwatch` `since`. Enum spelling:
     `/interface/ethernet` `advertise` `10M-half` against `10m-half`, `/interface/ovpn-server/server` `cipher`
     `blowfish128` against `blowfish-128`. `/queue/simple` limits: `0/0` against `unlimited/unlimited`.
     `/system/package` `bundle`: `routeros-x86` against `1`, a reference not resolved.
   - **1e. One write refused over native only.** Enabling an `/ip/dhcp-server` row: `can not run on slave
     interface` (M2 error `0xFE0006`). Not yet compared against the same step over the API.

   Unknown for 1b's route fields: whether any request makes 6.49.13 send Scope and Target Scope — another
   getall flag, a `get` of the single row — since WinBox 6 itself shows a Scope for these routes; a capture of
   it reading them is the ground truth. The audit report is written per transport, not per router, so a run
   against CHR2 replaces the 7.x report of the same transport.

2. **WinBox CLI: the `/system/clock` read never finishes on 6.x.** `WinboxCli` and `WinboxCliMac` both refuse
   it as incomplete — the counted read's closing `#n=` line never arrives. Telnet, SSH and MAC-Telnet read the
   same singleton on the same router, so the command is sound; something in the WinBox terminal on 6.x is not.
   Next step: a wire trace of that one read (`-WireTrace auto`) against the Telnet one.

3. **WinBox CLI: an add answers without its `.id` on 6.x.** `AddInterfaceListWillNotFail` and
   `AddInterfaceListMemberWillNotFail` raise `TikAddIdNotReadException` over `WinboxCli` and `WinboxCliMac`,
   every run; the same tests pass there on 7.24.4. The row is created — one was on the router after a failed
   run. It may share
   a cause with problem 2. Next step: the same trace comparison.

4. **What a suite run should say about REST on a 6.x router.** REST now refuses clearly (§3), but a leg run
   against 6.x still counts every REST test as a failure. To decide: gate REST on the router's version so those
   tests are Inconclusive, or keep them failing as the honest answer and not run the REST legs there.

5. **The pre-7.20 flag path of RouterOS 7 has no lab router.** CHR2 on 7.19.6 was the one router where flags
   are read by name through `proplist=`; on 6.49.13 the id-list path runs instead, and CHR runs neither. The
   by-name path now has unit coverage only (`CliFlagFieldsTests`). To decide: a third CHR on a 7.x before 7.20,
   or accept unit coverage for it.

6. **Coverage beyond the smoke subset is unmeasured on the other transports.** Not yet run against 6.49.13:
   the path-map audit over each CLI transport (`TIK4NET_AUDIT_TRANSPORT`), which does for them what problem 1
   did for WinBox native; `CliFlagFieldsTest` (the flags on every transport against the binary API); and the
   full suite — where CHR2's missing topology will fail tests for reasons that are not defects, so its failures
   need sorting before any counts as a 6.x gap.

   **`RomonRelayTest` with the 6.49.13 target: 24 of 37 pass.** The relay itself works over all three agent
   transports. The rest:
   - 9 Safe Mode tests: they check the target's state over its own API with `/safe-mode`, which is `no such
     command` on 6.x. Unknown: whether Safe Mode through the relay works there and only the check does not.
   - 3 synchronous-monitor tests (one per agent transport): the monitor command is refused on the target with
     `expected end of command (line 1 column 43)` — a 7.x-only argument, not yet identified.
   - 1 large read over MAC-Telnet: the id-list flag query on the 450-row table,
     `:put [/ip firewall address-list find (dynamic=yes)]`, was refused as incomplete after four MAC backlogs.
     The same read passed over Telnet and SSH.

7. **Two MCP-side gaps seen while measuring.** `mikrotik_call` over `MacTelnet` failed against both routers,
   6.x and 7.x alike, while the suite's own MAC-Telnet legs passed — so the server, not the router; it was a
   staged copy older than the library, so re-check after a reconnect first. And `mikrotik_call` over `ApiSsl`
   rejects the lab's self-signed certificate on both routers, with no option to accept it the way the suite's
   `restAllowInvalidCert` does.
