# Probe and harness scripts

Standalone diagnostic and test-harness scripts used while developing and debugging tik4net's
transports. None is part of the shipped library; they live here so that the skills in
`.claude/skills/` can reference a script instead of restating a command, and so that the commands stay
runnable by hand.

None of them takes router credentials as a baked-in value: coordinates come from
`tik4net.integrationtests/App.config` or from an explicit argument.

| Script | Purpose |
|---|---|
| [`run-integration-tests.ps1`](#run-integration-testsps1) | Run the integration suite: every leg in parallel, one transport, a smoke subset, one router |
| [`parse-trx.ps1`](#parse-trxps1) | Summarise TRX results — counts, failures, and the named skips |
| [`telnet-cli-probe.ps1`](#telnet-cli-probeps1) | Raw RouterOS Telnet client, for CLI ground truth without the library |
| [`jg_analyze.py`](#jg_analyzepy) | Parse and report on WinBox `.jg` catalogs |

## `run-integration-tests.ps1`

Runs `tik4net.integrationtests` against the lab routers. It resolves the repository root from its own
location, so it works from any working directory, and it always writes TRX so that skips remain
inspectable afterwards.

```powershell
Tools/probes/run-integration-tests.ps1                                        # everything, all routers, in parallel
Tools/probes/run-integration-tests.ps1 -Router default -Transport api -Sequential   # one leg, in this console
Tools/probes/run-integration-tests.ps1 -Smoke -Router default                 # smoke subset, every transport
Tools/probes/run-integration-tests.ps1 -Router default -Transport telnet -Sequential -WireTrace auto
Tools/probes/run-integration-tests.ps1 -Router chr2                           # the AnyRouter tests on the second lab router
```

A run is a set of **legs**: one `dotnet test` per router and transport, plus one `independent` leg per router for the
classes that never read the leg's transport (category `LegIndependent`). Every leg is a separate process
and four run at a time (`-MaxParallel`, 0 = all; 4 is where the lab CHR stops gaining — the `mikrotik-tests` skill has
the measurement) — the script builds once and starts each with `--no-build`, writing its console output to a `.log`
beside its TRX. The MAC-layer legs always share one lane, so two MAC sessions never run at once. `-Sequential` runs
the legs one after another in the console; `-Pairs` runs just two lanes per router; `-LockAll` keeps the processes but
serialises every test. What keeps parallel legs from colliding on the router
is in the suite (test locks and run leases, described in the `mikrotik-tests` skill), not in this script.

`-Router` takes `default` and/or the router profiles in `tik4net.integrationtests/App.config` (the `<name>.<key>`
entries, read through `LabConfig.cs`); omitted, it is every router the file defines. Each leg gets `TIK4NET_ROUTER`
for its own process. A secondary router gets only the tests in category `AnyRouter` — it carries none of the default
router's topology — and its TRX go to `<ResultsDirectory>/<name>`. A name the file does not define is refused before
anything runs.

The summary prints, per leg, the wall clock, the sum of the test durations and how much of it was waiting for test
locks (each test writes `[lock-wait-ms] n` into its output), the exit code and the test counts from the TRX; the script
exits non-zero when any leg failed.

`-WireTrace` sets `TIK4NET_WIRETRACE` for the run; test boundaries are written into the trace, so a
failure can be located without correlating timestamps. With more than one leg, pass `auto` so each leg gets a file.

## `parse-trx.ps1`

```powershell
Tools/probes/parse-trx.ps1 -ShowFailures -ShowSkips
Tools/probes/parse-trx.ps1 -Pattern 'results_winboxcli.trx' -FailedTestFilter
```

MSTest records `Assert.Inconclusive` in a TRX as `notExecuted`, and neither the console summary nor
`-v q` names the skipped tests. That matters for intermittent bugs: two runs that both report zero
failures can still differ, and a changed skip count is often the only observation available.

`-FailedTestFilter` emits a ready-made `--filter` expression for re-running only the failures.

## `telnet-cli-probe.ps1`

A minimal RouterOS Telnet (TCP 23) client that reproduces what `tik4net.Telnet.TelnetClient` does —
IAC option negotiation, the VT100 cursor-probe answer (without it RouterOS treats the terminal as
1×1 and renders nothing), and the change-password nag dismissal — then prints the **raw** bytes the
router returns, with `ESC` shown as `\e` and CR/LF as `\r`/`\n`.

Use it to establish ground truth for "what does the router actually emit for command X",
independently of the library.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/probes/telnet-cli-probe.ps1 `
  -RouterHost <host> -User <user> `
  -Command ':put [/interface print detail as-value]'
```

`-RouterHost` is mandatory; `-User` defaults to `admin` and `-Pass` to empty. Take the actual
coordinates from `tik4net.integrationtests/App.config`. **Omit `-Pass` for an empty password** —
passing `-Pass ''` through `powershell -File` is unreliable.

**`-CommandFile <path>` instead of `-Command`** when a command contains parentheses or anything else
PowerShell parses as syntax — `-Command ':put [... where comment="a (b)"]'` fails with *"A positional
parameter cannot be found"*. One command per line; blank lines and `#` comments are skipped, and
reading from a file bypasses PowerShell's argument parsing entirely.

**`-ReadMs` for a large answer** (default 2000). Each command's answer is read for a fixed window, not
up to a prompt — RouterOS redraws the prompt before the data, so a prompt match would stop early — and a
table of a few hundred KB takes longer than two seconds. Its tail then lands in the *next* command's
capture, which reads as two broken answers rather than one long one. `-ReadMs 15000` covered a
5000-row `/ip firewall connection` print.

The login uses fixed delays and **retries on a fresh TCP connection** (`-LoginTries`, default 10):
the probe answers VT100 probes with a canned cursor report, which occasionally desyncs the router's
credential read and produces a spurious *"incorrect username or password"*. Prompt-driven sending was
measured worse — the probe cannot tell a prompt from an echo of one — so retrying is the fix, and a
new socket is required because the router has already made up its mind about the old session.

See the `mikrotik-cli-probe` skill for the accumulated CLI findings this script was used to
establish.

## `jg_analyze.py`

Parses the WinBox `.jg` catalogs (the JS object literal encoding the M2 handler/field catalog) and
reports handlers, windows, and field keys/types. `tik4net/Winbox/WinboxJgCatalog.cs` is a C# port of
this parser, so the two must agree.

```
python Tools/probes/jg_analyze.py <jg-dir>                    # summary
python Tools/probes/jg_analyze.py detail <jg-dir> "20,3"      # one handler's windows + fields
python Tools/probes/jg_analyze.py report <jg-dir> out.txt     # full catalog to a file
python Tools/probes/jg_analyze.py <jg-dir> --json catalog.json
python Tools/probes/jg_analyze.py diff <dirA> <dirB>          # cross-version drift
```

The `.jg` files themselves are MikroTik's, so they are not redistributed here — dump them from a
router (the `WinboxDumpCatalogTest` integration test writes them) or from webfig. See the
`winbox-native-dev` skill.
