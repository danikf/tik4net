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
