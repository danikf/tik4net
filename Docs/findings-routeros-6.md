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

**A route record leaves Scope and Target Scope out.** The window declares both (`uf` `def:30`, `u10` `def:10`),
but neither key arrives in any row, with or without the statistics flag. It is not the default standing in
for itself: the API prints `scope=30` for the default route and `scope=10` for the connected one, while the
record lacks the key on both — so filling in `def` would make the second row wrong.

## Open problems

Each is a statement of what is measured and what is not, to be settled one at a time.

1. **WinBox native against the API on 6.x.** Measured with the path-map audit (`TransportPathMapAuditTest`,
   WinboxNative, against CHR2): OK 111, unmapped 22, value differences 8, field-name mismatches 2, not on this
   RouterOS 20; writes OK 158 with no value differing, refused 27, not probeable 53 (the router refused the row
   on both transports). 48 of 1064 API field names are never reported over native (4 %). Each part below is
   separate work:

   - **1a. 22 paths have no mapping** — windows under other labels, like routes were (§4): CAPsMAN (9 paths),
     `/interface/wireless` and five of its sub-menus, `/ip/accounting` and its two sub-menus (gone in 7),
     `/routing/bgp/instance`, `/network`, `/peer` (the 6.x BGP model), and `/system/ntp/client`. 26 of the
     27 refused writes are these same paths.
   - **1b. Fields native does not report, on paths that otherwise agree.** `/ip/route` misses seven: `scope`
     and `target-scope` (the router does not send them, §4), `connect` and `static` (7.x derives them from a
     field 6.x does not have), `pref-src` (arrives as `pref-source` — the 6.x label), `gateway-status`,
     `vrf-interface`. Elsewhere: `/ip/firewall/address-list` `list` — the row's own key field —
     `/interface` `default-name` and the `fp-*` counters, `/interface/bridge/port` `debug-info` and `hw`,
     `/ip/firewall/connection/tracking` `total-entries`, `/ip/ipsec/active-peers` `natt-peer`, `/ip/neighbor`
     `system-caps` and `system-caps-enabled`, `/ip/service` `address`, `/routing/ospf/area` `invalid` and
     `name`, `/system/logging/action` `syslog-severity` and `syslog-time-format`, `/tool/e-mail` `address`.
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
   did for WinBox native; `CliFlagFieldsTest` (the flags on every transport against the binary API);
   `RomonRelayTest` (whose target CHR2 now is); and the full suite — where CHR2's missing topology will fail
   tests for reasons that are not defects, so its failures need sorting before any counts as a 6.x gap.

7. **Two MCP-side gaps seen while measuring.** `mikrotik_call` over `MacTelnet` failed against both routers,
   6.x and 7.x alike, while the suite's own MAC-Telnet legs passed — so the server, not the router; it was a
   staged copy older than the library, so re-check after a reconnect first. And `mikrotik_call` over `ApiSsl`
   rejects the lab's self-signed certificate on both routers, with no option to accept it the way the suite's
   `restAllowInvalidCert` does.
