# Probe and harness scripts

Standalone diagnostic and test-harness scripts used while developing and debugging tik4net's
transports. None is part of the shipped library; they live here so that the skills in
`.claude/skills/` can reference a script instead of restating a command, and so that the commands stay
runnable by hand.

None of them takes router credentials as a baked-in value: coordinates come from
`tik4net.integrationtests/App.config` or from an explicit argument.

| Script | Purpose |
|---|---|
| [`run-integration-tests.ps1`](#run-integration-testsps1) | Run the integration suite: one transport, a smoke subset, or the full matrix |
| [`parse-trx.ps1`](#parse-trxps1) | Summarise TRX results — counts, failures, and the named skips |
| [`telnet-cli-probe.ps1`](#telnet-cli-probeps1) | Raw RouterOS Telnet client, for CLI ground truth without the library |
| [`jg_analyze.py`](#jg_analyzepy) | Parse and report on WinBox `.jg` catalogs |

## `run-integration-tests.ps1`

Runs `tik4net.integrationtests` against a live router. It resolves the repository root from its own
location, so it works from any working directory, and it always writes TRX so that skips remain
inspectable afterwards.

```powershell
Tools/probes/run-integration-tests.ps1 -Transport api          # one transport, full suite
Tools/probes/run-integration-tests.ps1 -Smoke                  # smoke subset, every transport
Tools/probes/run-integration-tests.ps1                         # full matrix
Tools/probes/run-integration-tests.ps1 -Transport telnet -WireTrace auto
```

The default transport order runs the API-based transports before the CLI ones, because CLI transports
are the ones that leave orphans on the router and an orphan changes the error a later transport sees.

`-WireTrace` sets `TIK4NET_WIRETRACE` for the run; test boundaries are written into the trace, so a
failure can be located without correlating timestamps.

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
