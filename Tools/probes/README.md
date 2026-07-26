# Probe scripts

Two standalone diagnostic scripts used while developing and debugging tik4net's transports. Neither
is part of the shipped library; both are here because the Claude Code skills in `.claude/skills/`
reference them.

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
