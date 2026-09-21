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
| `WinboxNative`, `WinboxNativeMac` | 19 of 21 | `/ip/route` has no handler mapping — open problem 1 |
| `Rest`, `RestSsl` | cannot work | RouterOS 6 has no REST API (§3) |

The smoke subset is the only coverage so far; see open problem 6.

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

## 4. WinBox native gets no field catalog

The WinBox login and the `list` catalog work: `WinboxDumpCatalogTest` reads the list and parses 12 plugin
entries from it. Every plugin download then fails — **0 of 12** through the route 7.x serves them on
(`/var/pckg/`, command 3). Paths whose mapping does not need a plugin's window still read (19 of 21 smoke
tests pass); `/ip/route`, whose window is WinBox's *Route List* under IP → Routes (tabs Routes, Nexthops,
Rules, VRF), answers `no M2 handler mapping for path '/ip/route'`.

## Open problems

Each is a statement of what is measured and what is not, to be settled one at a time.

1. **Download the `.jg` catalog from RouterOS 6.** The plugin route that works on 7.x returns nothing on
   6.49.13 (§4). Unknown: whether 6.x serves the plugins on another path or command, or under other names
   (the `list` entries' `unique` field is what resolves a name on 7.x — `findings-winbox-catalog.md`), and whether the 6.x
   `.jg` format matches `jg-catalog-format.md`. WinBox 6.x itself loads these windows, so a capture of WinBox
   connecting to 6.49.13 is the ground truth to compare against. Until this works, how much of WinBox native
   covers 6.x cannot be measured; `/ip/route` is only the first symptom.

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

6. **Coverage beyond the smoke subset is unmeasured.** Not yet run against 6.49.13: `CliFlagFieldsTest` (the
   flags on every transport against the binary API), `RomonRelayTest` (whose target CHR2 now is), and the
   full suite — where CHR2's single port and missing topology will fail tests for reasons that are not defects,
   so its failures need sorting before any counts as a 6.x gap.

7. **Two MCP-side gaps seen while measuring.** `mikrotik_call` over `MacTelnet` failed against both routers,
   6.x and 7.x alike, while the suite's own MAC-Telnet legs passed — so the server, not the router; it was a
   staged copy older than the library, so re-check after a reconnect first. And `mikrotik_call` over `ApiSsl`
   rejects the lab's self-signed certificate on both routers, with no option to accept it the way the suite's
   `restAllowInvalidCert` does.
