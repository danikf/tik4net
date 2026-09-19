# tik4net.integrationtests

Tests that require a **live MikroTik router**. There are no mocks here.

| | |
|---|---|
| Framework | MSTest |
| Target | `net48` |
| Router required | Yes, for nearly all of ~415 test methods |
| Runs in CI | **No** |

The full operating guide — running, reading skips, orphan cleanup, writing new tests, current
transport limitations — is the **`mikrotik-tests` skill**. This file covers only what the project is.

## Router coordinates

`App.config` is the single source of truth: `host`, `user`, `pass`, `routerMac`, plus the topology
assumptions consumed by `TestConstants.cs` (`testInterface`, `testAddress`, `testWirelessInterface`).
Point it at your router before running anything; do not restate its values elsewhere.

To provision a router from scratch, use the **`chr-test-router-init` skill**.

## The lab routers

The lab runs two CHRs. Only the first is needed for the suite; the second covers what one router cannot.

| | **CHR** — the suite's router | **CHR2** — RoMON target, older RouterOS |
|---|---|---|
| `App.config` keys | `host`, `user`, `pass`, `routerMac`, `routerIdentity` | `romonTargetId`, `romonTargetHost`, `romonTargetUser`, `romonTargetPass` |
| RouterOS | the version README promises (current stable) | **7.19.6**, kept there on purpose |
| Identity | `CHR` | `CHR2` — must differ from the first |
| Ports | two (`testInterface`, `testSecondInterface`) | one |
| Role | every test runs against it; the RoMON agent | reached through CHR over RoMON; the pre-7.20 router |

**RoMON.** `RomonRelayTest` opens CHR2 *through* CHR (Telnet, SSH and MAC-Telnet to CHR, `/tool romon ssh`
beyond it) and writes to it, checking each write over CHR2's own API connection (`romonTargetHost`). RoMON is
enabled on both. With `romonTargetId` empty those tests are Inconclusive.

**Older RouterOS.** 7.19.6 is the last release before 7.20, which changed `print as-value` to include the flag
fields (`Docs/findings-cli.md`). CHR2 is the one router here where the CLI transports' by-name flag read
actually runs, so it is not upgraded along with CHR. A test that should also hold on the older version is run
against it by pointing `host` / `routerMac` at CHR2 for that run and putting them back afterwards —
`CliFlagFieldsTest` (flags over every transport against the binary API) is the one that matters. The full suite
is not run there: CHR2 has one port and none of CHR's provisioned topology, so topology tests fail for reasons
that are not defects.

## The lab VM, if the router is virtual

Most of this suite's history of unexplained slowness turned out to be the **VM**, not the router and
not the library, so the host configuration is part of the test setup rather than an operational
detail. What the lab is running now, and why each part is that way:

| | |
|---|---|
| Hypervisor | Hyper-V on a workstation |
| Guest | RouterOS CHR, x86_64 |
| **vCPUs** | **2** |
| Memory | 4 GB, **static** |
| vNICs | two: the connection link and a spare |

**Two vCPUs, not sixteen.** This is the one that matters. A 16-vCPU CHR on a laptop host wedged
large API reads roughly once every six to eight, and did it convincingly enough to look like a
RouterOS defect: sentences arriving, then nothing for a full 30 s, on a session the router was still
*executing writes* for. Dropping to two cleared it — 40 reads across five connection-state arms, no
stalls. A wide VM is harder for the hypervisor to place, and while it waits nothing inside it runs,
including the timers that would retransmit a lost segment. Give a virtual test router the narrowest
CPU that serves it. `Docs/findings-router-throughput-ceiling.md` has the measurements.

**Two network adapters, and the suite assigns them by itself.** `TestConstants.Interface` (default
`ether1`, override `testInterface`) is the link the connection under test arrives on;
`TestConstants.SecondInterface` is discovered as *the first ethernet that is not that one*, and is
where the fixtures that must not disturb the connection get built — bridge ports, VLAN parents, VRRP,
DHCP servers. So the second adapter needs to exist and to be up; it does not need to lead anywhere,
and nothing else should depend on what a test does to it.

Take care when the connection link changes. With only two adapters, pointing `App.config` at the
*second* one inverts the discovery and makes the **first** the fixture target — which, if that is the
adapter carrying the router's internet, means a test run bridges the link it downloads packages over.
Either keep the connection on the first adapter, pin `testSecondInterface`, or add a third.

Measured and **not** required: moving the test traffic onto an internal, host-only vSwitch, away from
a bridged adapter. It was tried against exactly this problem and changed nothing — the stall
reproduced on the second read and took a second, unrelated connection down with it. Advised but
untested here: no dynamic memory, and no automatic checkpoints on the VM.

## One run per transport

The transport under test comes from the `tik.connectionType` run parameter, supplied by one
`*.runsettings` file per transport — `api`, `apissl`, `rest`, `restssl`, `telnet`, `ssh`, `mactelnet`,
`winboxcli`, `winboxclimac`, `winboxnative`, `winboxnativemac`. Covering the matrix means running the
suite eleven times.

```bash
Tools/probes/run-integration-tests.ps1 -Transport api      # one transport, full suite
Tools/probes/run-integration-tests.ps1 -Smoke              # smoke subset, every transport
Tools/probes/parse-trx.ps1 -ShowFailures -ShowSkips        # read the results
```

## Inconclusive is not failure

A test that hits a capability its transport lacks reports **Inconclusive**. When a test is skipped,
check the capability flags before "fixing" it — and equally, confirm the limitation is real before
adding a new guard, because a gate on an unproven assumption silently disables the test everywhere it
mattered.

## Layout

`Protocols/` holds low-level protocol proof-of-concept tests that manage their own connection and do
**not** derive from `TestBase` — they run regardless of the active runsettings. Everything else is
organised by domain, mirroring the entity folders in `tik4net.objects/`.

The project is SDK-style — new `.cs` files are picked up automatically.

## Clean up after yourself

Every test must delete what it created, in a `finally`. An orphan left on the router does not just fail
its own test next time; it changes the error a *different* transport sees on a later run, which is
considerably harder to diagnose.
