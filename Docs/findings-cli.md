# CLI/PTY transports (Telnet, SSH, MAC-Telnet, WinBox terminal) — RouterOS ground truth

How RouterOS's terminal actually behaves, and how the CLI-family transports are built on top of it.
Complements the design document [terminal-cli-parsing.md](terminal-cli-parsing.md) (the
`CliConnectionBase`/`CliOutputParser`/`VtStripper` architecture), the MAC-layer specifics in
[findings-mactelnet.md](findings-mactelnet.md), and the WinBox `mepty` terminal's byte-ack protocol in
[findings-mepty-byte-ack.md](findings-mepty-byte-ack.md). Probe tool:
[`telnet-cli-probe.ps1`](../Tools/probes/telnet-cli-probe.ps1).

> **Section numbers are stable.** The C# implementation and other `Docs/` files cite this document by
> section number (grep for `findings-cli.md §` under `tik4net/`, `tik4net.integrationtests/`,
> `tik4net.unittests/` and `Docs/` to see every citation). Do not renumber a heading without checking,
> and updating, everyone who cites it.

> **Principle:** Telnet, SSH, MAC-Telnet, WinBox-CLI and WinBox-CLI-MAC are one transport family, not
> five. All five drive `ITikConnection` the same way: `ITikCommand` is turned into a RouterOS CLI
> string (`:put [/path print … as-value]` for reads, a bare command line for writes), sent over a raw
> VT100 terminal, and the reply is stripped of ANSI/echo/prompt and parsed back into records. No binary
> `!re` sentences anywhere in this family — `.id` comes from `as-value`/JSON output, not from the API.

> Superseded diagnoses, incidents and pinned measurements for this area are in
> [`findings-cli-history.md`](findings-cli-history.md); this document describes current behaviour only.

## Architecture

```
tik4net/Cli/                 shared by every PTY transport
├── CliConnectionBase.cs       ITikConnection over a CLI text channel — print/add/set/monitor dispatch
├── CliCommandBuilder.cs       ITikCommand → RouterOS CLI text (quoting, print modifiers, :serialize)
├── CliOutputParser.cs         as-value text → TikRecordSentence (plus the torch table parser)
├── CliJsonParser.cs           :serialize to=json output → TikRecordSentence
├── CliOutputHelper.cs         echo/prompt trimming, echo alignment, router log-line detection
├── CliErrorParser.cs          CLI output text → Tik*Exception
├── CliTableParser.cs          interactive (non as-value) table output, by column offset
├── CliLineStreamer.cs         carves completed lines out of a growing read buffer
├── CliMonitorVerbs.cs         per-verb snapshot modifiers for monitor commands
├── CliSafeModeParser.cs       Safe Mode prompt/response parsing
├── RouterOsCliLogin.cs        shared login sequence and prompt detection
├── Vt100State.cs / VtStripper.cs   VT100 cursor-probe negotiation and ANSI stripping
└── CliReadTimeout.cs           receive-deadline exception with the last-seen screen text attached

tik4net/Telnet/                TelnetClient.cs, TelnetConnection.cs, TelnetNegotiator.cs
tik4net/MacTelnet/              MacTelnetUdpClient.cs, MacTelnetConnection.cs — see findings-mactelnet.md
tik4net/WinboxCli/               WinboxCliClient.cs, WinboxCliConnection.cs — the mepty terminal
tik4net/WinboxCliMac/            WinboxCliMacConnection.cs — mepty over the MAC layer
tik4net.ssh/                    SshConnection.cs, SshShellClient.cs, Tik4NetSsh.cs
```

Each transport owns only its byte I/O and login handshake; `RouterOsCliLogin`, the `Cli/` parsers and
`CliCommandBuilder` are shared, so a RouterOS behaviour fixed once is fixed on all five transports.

---

## 1. Output format: `print as-value`, `:put`, `detail`, `stats`, and free-text fields

### `print as-value` only materialises inside `:put [ … ]`

A bare `/interface print as-value` typed into a PTY returns **nothing** — just the command echo and
the next prompt. `as-value` only appears in a **script context**, so every read is wrapped:

```
:put [/interface print detail as-value where name=ether1]
```

The output of `:put` is **one line**, with every record's fields chained together by `;`; the
**boundary between records is `.id=`** (a singleton with no `.id` is a single record). This is not
one-line-per-record — `CliOutputParser.ParseAsValue` splits records at `.id` boundaries, not at line
breaks.

### `detail` is required for the full field set

`:put [/path print as-value]` alone returns only **summary columns** — e.g. `/interface` omits
`default-name`, `mtu`, `rx-byte`. The full field set (parity with the binary API) requires
`print detail as-value`. The O/R mapper's `IncludeDetails` metadata flag controls this; the builder
translates it into the `detail` modifier.

### `as-value` spells values the router's way, not the API's

`print` and the binary API render a field for a reader; `as-value` renders it for a script, and the two
disagree on four kinds of value. Measured across the 154 audited paths on 7.24:

| | `print` / API | `as-value` |
|---|---|---|
| durations | `15s`, `1w`, `5m`, `100ms` | `00:00:15`, `1w00:00:00`, `00:05:00`, `00:00:00.100` |
| a zero spelled as a word | `mtu=auto`, `mrru=disabled`, `max-sessions=unlimited`, `dscp=inherit` | `0`, `0`, `0`, `256` |
| scaled fixed-point | `bucket-size=0.1`, `freq-drift=-40.955`, `gmt-offset=+02:00` | `100`, `-40955`, `7200` |
| an IPv4 address in an IPv6 slot | `local=192.168.4.236` | `::ffff:192.168.4.236` |

`CliValueNormalizer` re-spells the durations, which is the only one of the four identifiable from the
value: the others depend on which field the value belongs to. Two fields' `HH:MM:SS` is a clock TIME and
not a duration — `/system/clock` `time` and `/system/scheduler` `start-time`, and that is the whole list.

### A print the router REFUSES answers with text and no records

`:put [/ip dns print detail as-value]` answers `bad parameter detail (line 1 column 27)` — a singleton
menu has no `detail` modifier. Since as-value output is `key=value;…` or nothing at all, text that parses
to no record is not output: it is the router saying why there is none. `CliConnectionBase.ParseRecords`
throws on it, the same positional rule monitors have always used, and with no phrase list.

### Secret fields are WRITE-ONLY over the CLI — `detail` does not help

A field the `.jg` types as `secret` — a pre-shared key, a WEP key, a RADIUS or MSCHAPv2 password — is
**absent from `print as-value` entirely**, not empty. Adding `detail` changes nothing. The catalog types 166
fields this way, so this is a general rule rather than a quirk of one menu.

Measured on 7.24, one profile carrying `wpa2-pre-shared-key=SuperSecret123`, read four ways:

| transport | result |
|---|---|
| binary API / REST | `wpa2-pre-shared-key=SuperSecret123` |
| WinBox native (M2) | `wpa2-pre-shared-key=SuperSecret123` |
| Telnet / SSH / MAC-Telnet / WinBox CLI | field **not present in the record at all** |

This is the router's decision, not a gap in this client, and it is not something a `.proplist` or a
different verb recovers. The consequences for a caller:

* a secret property read over a CLI transport is `null`, and *cannot* be distinguished from one the router
  genuinely holds empty;
* WRITING one works normally on every transport — these fields are write-only, not unsupported;
* a read-modify-write cycle over a CLI transport therefore must not send the secret back. The O/R mapper's
  diff-based `Save` already handles this: an unread field is unchanged, so it is not in the diff. A
  `FullUpdate` save, or any code that copies an entity field by field, will blank it.

### `print stats` — counters need a second query

`print detail as-value` does **not** include runtime counters (`bytes`/`packets`, `rx-byte`, …).
Counters only appear in `print stats as-value`, a different column mode that returns counters plus
`.id` and a handful of identity fields — but not the config fields. Combining `detail stats` behaves
like `stats` alone (the config fields disappear). No single modifier returns both, so entities that
need both issue **two queries and merge by `.id`**.

The O/R mapper controls this with the entity metadata flag `IncludeCliStats` (currently set on
`Interface`, `FirewallFilter`/`Mangle`/`Nat`/`Raw`, `QueueSimple`/`Tree`), which
`TikConnectionExtensions` renders as the CLI-only marker parameter `.cli-stats`
(`TikSpecialProperties.CliStats`). `CliConnectionBase.RunPrint` sees the marker, runs
`CliCommandBuilder.BuildPrint` (config) and `CliCommandBuilder.BuildPrintStats` (counters), and merges
the two record sets by `.id` — config fields win on a key collision, and a missing stats side falls
back to config-only rather than dropping the record. The binary API and REST transports **ignore** the
marker (`IsSpecialParam` in `ApiCommand`/`RestRequestBuilder`) — they already get counters from
`detail`, and the marker never reaches the wire.

### A number as-value prints, and the word the API prints for it

Some fields store a number whose extreme value the API renders as a word. as-value always gives the
number, so the CLI reader has to know the pairing — and the pairing is a property of the FIELD, not of
the value. Measured on 7.24 by setting a non-sentinel value and reading it back both ways:

| Field | as-value | API | a real value, both ways |
|---|---|---|---|
| `mtu` | `0` | `auto` | `1400` → `1400` |
| `ttl` | `0` | `auto` | `64` → `64` |
| `horizon` | `0` | `none` | `5` → `5` |
| `mrru` | `0` | `disabled` | `1600` → `1600` |
| `max-sessions` | `0` | `unlimited` | `10` → `10` |
| `dscp` | `256` | `inherit` | **`0` → `0`** |

`dscp` is the one that matters: **its zero is a real DSCP class**, and its sentinel is `256`, outside the
field's own 0..63 range. Applying the other five fields' rule to it would have quietly replaced a valid
value with `inherit`.

`horizon` is worth stating too, because it looks like it could have a real zero: setting `horizon=0` reads
back `none` over the API, so 0 and `none` are one state and there is no third case to lose. And `mrru`
cannot be *set* to 0 at all (range 1500..16384), which is what makes 0 unambiguously the disabled state.

### Two fields as-value scales by a thousand, and one it gives in seconds

`bucket-size=5` comes back from as-value as `5000` (the router's own range for the field is 0..10), and
`freq-drift` the same way — a scale on every value, not a sentinel. `/system/clock`'s `gmt-offset` is
seconds east of UTC where the API prints a signed clock offset: `7200` → `+02:00`.

### An IPv4 in an IPv6-shaped slot

`/ip/service` `local` reads `::ffff:192.168.4.236` over the CLI and `192.168.4.236` over the API. Unlike
the two above this needs no field knowledge — `::ffff:` followed by a dotted quad cannot be anything else,
so it is recognised by shape, like a duration.

### `as-value` has no escaping — free-text fields go through `:serialize to=json`

`as-value` joins records with `;`, fields with `;` and `=`, and has **no escape mechanism whatsoever**.
Two distinct symptoms follow from that:

- **List-type fields already use `;` internally.** A multi-value field is rendered with `;` **between
  its own elements** — the same character used between fields — e.g.
  `key-usage=key-cert-sign;crl-sign;name=mikrotik-CA`. `CliOutputParser.ParseOrderedFields` handles this
  by treating an element with no `=` as a continuation of the previous field's value (joined with `,`,
  the API's own multi-value separator), splitting only on the **last** `;` before an `=`.
- **A value that itself contains `;`, `=` or a newline is indistinguishable from further fields and
  records.** A file body, a script source or free-form text corrupts the parse outright — newlines
  inside the value become field separators and get silently re-joined as multi-value elements.

For fields marked `IsFreeText` on the O/R mapper side (currently `SystemScript.Source`,
`File.Contents`, `SystemScheduler.OnEvent`, `SystemNote.Note`), the entity carries the CLI-only marker
`.cli-json` (`TikSpecialProperties.CliJson`), which switches the read to
`:put [:serialize to=json [ /path print … as-value ]]`, parsed by `CliJsonParser` instead of
`CliOutputParser`. JSON is escaped, so the read is exact. `CliJsonParser` converts JSON back to the
string wire form the binary API would have produced (`true`/`false` as text, array elements joined with
`,`, an integral-valued float rendered without its fractional part — RouterOS serialises some integer
fields as e.g. `2048.000000`) and **throws** rather than degrading on anything it cannot map (a nested
object, an array of arrays) — a wrong value that parses is worse than a loud failure.

`:serialize` requires **RouterOS 7.13+**. Support is detected per connection from what the router
actually answers, not from a parsed version string: `CliConnectionBase` tries the JSON form once, and
only a router that *refuses* it while support is still unknown falls back to plain `as-value` for the
rest of that connection (and the fallback is only trusted once the plain form actually succeeds — an
unrelated failure of both forms concludes nothing). A pre-7.13 router therefore degrades silently to
the same `;`-splitting behaviour as any other field; there is no error raised for it, so a free-text
field on such a router should be assumed to parse incorrectly.

### `/tool/torch`: a different snapshot mechanism entirely

Torch's `as-value` form (with either `once` or `duration`) prints **nothing**, and it rejects the
`once` snapshot modifier other monitors use (`bad parameter once (line 1 column 40)`). Torch is
instead driven by two torch-specific parameters: an explicit `proplist` (RouterOS's default columns
omit `tx-packets`/`rx-packets`) and `freeze-frame-interval=N`, which makes torch append a new,
terminated `Columns:`/data block every `N` seconds instead of redrawing the previous one in place —
turning the display into discrete, parseable snapshots. `duration` must be at least
`2×freeze-frame-interval`, or zero frames are flushed before the command self-terminates.
`CliOutputParser.ParseTorchFrame` reads the field **order** back from each frame's own `Columns:`
declaration rather than assuming it matches the requested `proplist` order, because RouterOS reorders
the columns to its own canonical order regardless of what was requested; only the last complete frame
is parsed. `CliMonitorVerbs.Kind.FreezeFrame` routes torch to this dedicated builder/parser pair instead
of the `once`/`as-value` path every other monitor uses.

### Print modifiers

| Modifier | Purpose |
|---|---|
| `as-value` | machine `key=value;…` output — only materialises inside `:put [ … ]` |
| `without-paging` | disables paging (`-- [Q quit...]`) — injected automatically on every `print` on a PTY transport |
| `detail` | full field set (see above); human-readable form shows the comment as a `;;;`-prefixed line, which is not the `as-value` form and is not used for parsing |
| `terse` | a more machine-friendly line-based alternative to `as-value` |
| `count-only` | record count only |
| `where <cond>` | filter, equivalent to the API's `?name=value` — see quoting rules in §2 |

### Comments

The comment field parses like any other field in `as-value` output (`comment=<text>`); the `;;;`-prefixed
form only appears in human-readable `print detail` and is never used for parsing.

---

## 2. Quoting and escaping — `where` clauses and `name=value` arguments

### `where` values with special characters must be quoted

`where address=192.168.1.1/24` (unquoted) **matches nothing** — in a `where` expression context, `/`
and `:` are interpreted as operators. It must be `where address="192.168.1.1/24"`. The safe unquoted
character set is `[A-Za-z0-9._-]` (`CliCommandBuilder.QuoteForWhere`); anything outside it is
double-quoted. `*N` (an `.id`) works unquoted inside a `find` — `where .id=*1` — and also works quoted, so the builder
quotes it like anything else.

### A `name=value` argument is parsed by the PARAMETER's type, so it needs the same quoting

An unquoted value is not read as text: RouterOS parses it according to the type of the parameter it is
being given to. `address=10.0.0.0/24` is fine because that parameter is an IP prefix — and the same
characters are a syntax error on a **script-typed** parameter, where the router parses the value as
code. `/system/script`'s `source` is the one that shows it:

```
/system script add name=x source=:nothing     → syntax error (line 1 column 47)
/system script add name=x source=":nothing"   → accepted, stored as :nothing
```

Measured on 7.24 against `source`, every one of `` : [ ] ( ) { } ' ? ! ~ < > | & , * / + = `` breaks an
unquoted value, leading **or** mid-value. `[`, `(` and `{` do not report an error on their own line —
they open a construct that swallows the *next* command, which then fails at "line 2".

So the safe unquoted set for `name=value` is the same `[A-Za-z0-9._-]` as for `where`, and
`CliCommandBuilder.IsSafeUnquoted` is shared by both. Quoting costs nothing: an IP prefix, an interface
reference, a bool, an enum and `numbers=` with a `*`-id were each verified to round-trip unchanged when
quoted. The rule has to be an allow-list rather than a list of dangerous characters, because the builder
does not know the parameter's type — and a type nobody has looked at yet must not be able to produce a
value that is silently mis-sent.

### Inside double quotes, `$` and `\` are live — an unescaped value is silently rewritten

RouterOS has no single-quote string form at all (`:put 'a$b'` → `syntax error` at the `'`), so double
quotes are the only quoting mechanism — and inside them, **variable substitution** (`$name`) and
**backslash escapes** both apply. Measured on 7.23.2, writing to `/system script`:

| Sent | Router stores | Note |
|---|---|---|
| `source="x\$y z"` | `x$y z` | `\$` = literal `$` |
| `source="x$y z"` | `x z` | **silent substitution** — `$y` undefined → empty |
| `source="x\\y z"` | `x\y z` | `\\` = literal `\` |
| `source="x\"b"` | `x"b` | |
| `source="C:\temp\new w"` | `C:<TAB>emp<LF>ew w` | **silent** — `\t`, `\n` are known escapes |
| `source="x\y z"` | `syntax error` | unknown escape |
| `source=x$y` | `syntax error` | `$` outside quotes is never legal |
| `source=x\y` | `syntax error` | neither is `\` outside quotes |

Full escape set (MikroTik docs, verified for `\$ \\ \" \t \n \_ \41`): `\"` `\\` `\n` `\r` `\t` `\$`
`\_` `\a` `\b` `\f` `\v` `\<hex>`.

`CliCommandBuilder.QuoteIfNeeded`/`QuoteForWhere` therefore quote any value that is not entirely
`[A-Za-z0-9._-]` — which covers `$`, `\`, whitespace, `;`, `#` and `"` along with everything above — and
escape inside the quotes in the order `\` → `"` → `$` (escaping the
backslash first, or the pass that escapes `$` would double what the backslash pass already added). An
actual newline inside a quoted value is left as a real character rather than rewritten to `\n` — RouterOS
accepts a literal line break inside an open quote (that is how a multi-line script source round-trips),
and rewriting it would be indistinguishable from a value that genuinely contains a backslash followed by
`n`. CR/LF are still in the trigger set for the opposite reason: **unquoted**, they would terminate the
command line and the remainder would be executed as a separate command.

A value read back that was written unescaped is corrupted **at the router**, not in transit — the add
succeeds, the `.id` comes back, nothing complains, and every other transport agrees on the same wrong
stored value.

---

## 3. An empty value is not an empty token

### `name=` mid-line is a syntax error

```
/system note set note= show-at-login=yes
  → expected end of command (line 1 column 37)     ← column 37 = start of `show-at-login`
/system script add name=X source=":put 1" comment=
  → *1                                              ← passes at END of line
```

A bare `name=` doesn't pass an empty string — the parser consumes nothing for it and stumbles on the
next token; it only survives when it happens to be the last thing on the line. `QuoteIfNeeded`/
`QuoteForWhere` therefore always render an empty value as the two-character literal `name=""`, which
works in both positions.

### `where name` and `where name=""` are opposite queries

Two `/system/script` entries, one with the comment `hello`, the other without:

```
:put [/system script print as-value where comment]      → the row WITH a comment
:put [/system script print as-value where comment=""]   → nothing
API  ?comment=                                          → nothing
```

`where <field>` (bare name, no `=`) tests "is set" (truthiness); the API's `?field=` means "equals
empty". `CliCommandBuilder` distinguishes these by whether the parameter's value is `null` (→ bare
`name`) or an empty string (→ `name=""`).

---

## 4. Login sequence: prompts, the change-password nag, and refusal detection

`RouterOsCliLogin` (`tik4net/Cli/RouterOsCliLogin.cs`) owns the RouterOS-specific login/prompt state
machine for every PTY transport; each transport supplies only the raw byte I/O.

### Prompt and login sequence

- `Login:` prompt — matched case-insensitively on the substring `ogin:` (`IsLoginPrompt`) — send the
  user name, with `+c` appended (`RouterOsCliLogin.TerminalLoginFlags`) on transports that perform the
  interactive exchange. `+c` disables ANSI colour; **terminal width is deliberately not pinned** to a
  fixed value such as `80` — the transport instead answers RouterOS's VT100 cursor-probe negotiation
  (§6) advertising a wide terminal, because a narrow one makes RouterOS wrap long `:put` as-value
  records and insert `\r\n` into the data.
- `Password:` prompt — matched on the substring `assword:` (`IsPasswordPrompt`) — send the password.
- Shell prompt — `[user@identity] > ` (identity is arbitrary) — detected as
  `TrimEnd().EndsWith("] >")`(`RouterOsCliLogin.EndsWithPromptSuffix`), i.e. the suffix only, not the
  whole prompt. Safe Mode replaces the `>` with a `<SAFE>` token; both `] <SAFE>` and the older
  `] <SAFE> >` form are matched.
- Change-password nag — a router with a default/empty password shows `new password>` (and
  `repeat new password>`), detected by the substring `password>` (`IsChangePasswordNag`, deliberately
  **not** matched by `IsPasswordPrompt`, which requires a trailing colon). Answered with **Ctrl-C
  (0x03)**, up to three rounds, then a loud failure. RouterOS repaints the nag, so a single read can
  carry `new password>` twice.

### A refusal is detected positionally, not by wording

RouterOS answers a wrong password (7.23.2) with:

```
\r\nLogin failed, incorrect username or password\r\n\r\nLogin:
```

A phrase list alone cannot be trusted across RouterOS versions and languages — an unmatched phrase does
not throw, it silently waits out the full receive deadline. The authoritative signal is **positional**:
after a refusal, RouterOS *restarts the login dialogue*, so a fresh `Login:` prompt arriving **after
credentials have already been sent** means rejected, in any wording, on any version.
`RouterOsCliLogin.ResolveToPromptAsync` takes `loginPromptMeansFailure`, set only by the interactive
login path (`LoginAsync`) — a transport that authenticates below the terminal (SSH, WinBox `mepty`)
never sends credentials over the terminal itself, so a `Login:` string reaching it there is not evidence
of a refusal. `IsLoginFailure`'s phrase list (`"login failed"`, `"incorrect username"`,
`"login failure"`, `"incorrect login"`, `"invalid user name"`, `"bad password"`, `"access denied"`) is
kept as a fast path and a better exception message; it is not load-bearing — with the list emptied the
transcript tests still pass on the positional signal alone.

### What each assumption costs when it is wrong

There is no error channel on a terminal: every login decision below is a guess about wording and
layout that differ by RouterOS version and by router state, and a wrong guess does not fail loudly —
it runs to the receive deadline and returns something plausible, late.

| Assumption | Where | Cost if wrong |
|---|---|---|
| Login prompt contains `ogin:` | `IsLoginPrompt` | Credentials typed into a screen that isn't asking for them |
| Password prompt contains `assword:` | `IsPasswordPrompt` | Same |
| Nag contains `password>` | `IsChangePasswordNag` | Ctrl-C never sent → login stalls to the deadline; worse, bytes meant for the shell can land in the new-password field |
| Prompt ends `] >` / `] <SAFE>` | `EndsWithPromptSuffix` | Every command runs to the full receive deadline, though results are still correct |
| Refusal wording | `IsLoginFailure` | Superseded by the positional signal above — a full receive deadline per rejected login was the cost before it existed |
| `+c` login flag accepted | `TerminalLoginFlags` | SSH falls back to the bare user name on `SshAuthenticationException`; Telnet has no such fallback |

### Prompt redraw and settling

RouterOS repaints the prompt (`\r\r\r\r] > `) even **before** a command's own output, so a naive
"ends with the prompt" check can match prematurely. A read is only complete once the prompt is present
**and** the stream has been silent for a settle window afterwards (`SettleMs` — 120 ms on Telnet, 150 ms
on MAC-Telnet and the WinBox terminal); any further output resets the window. See §7 for the additional
requirement that the settled prompt also follow this command's own echo.

---

## 5. SSH is a PTY shell, not exec

SSH is **not** "exec without PTY". Over the exec channel RouterOS doesn't produce `as-value` output any
better than Telnet does, so the SSH transport (`tik4net.ssh/`) opens an interactive
`ShellStream` PTY and drives the same shared `RouterOsCliLogin`/`Vt100State`/`CliOutputHelper` stack as
every other CLI transport — SSH only adds the transport layer (~280 LOC), split into a separate package
(`tik4net.ssh`) to isolate the `Renci.SshNet` dependency.

- **Auth has no `Login:`/`Password:` prompts.** `SshClient.Connect()` performs authentication;
  `SshShellClient.SettleAfterConnectAsync` then calls `RouterOsCliLogin.ResolveToPromptAsync` directly
  (skipping the credential exchange) to dismiss the nag and settle to a usable prompt. The `+c`
  terminal-flag suffix is tried on the login name first; a `SshAuthenticationException` triggers one
  retry with the plain user name.
- **Raw PTY terminal modes.** `CreateShellStream` sets
  `ECHO/ICANON/ISIG/IEXTEN/IXON/IXOFF/ICRNL/INLCR/OPOST = 0` — RouterOS runs its own line editor and
  expects raw keystrokes; without this, control keys sent for Safe Mode would be eaten by the SSH
  server's own line discipline before reaching RouterOS.
- **Ctrl+D is SSH EOF and closes the channel.** The Safe Mode discard key **Ctrl+D (0x04)** is the SSH
  EOF convention; RouterOS's SSH server closes the channel on it regardless of the raw terminal modes
  (Telnet has no such convention — a bare byte there just reaches the console). Safe Mode unroll
  therefore goes through the scriptable `/safe-mode/unroll` (RouterOS 7.18+), a normal command that
  keeps the channel alive; older routers fall back to Ctrl+D as rollback-by-disconnect
  (`SshConnection.SafeModeUnrollByControlKey`). Take/Release (Ctrl+X) work fine in place over SSH.
- **`ShellStream.DataAvailable` polling**, same pattern as `NetworkStream` on Telnet (see §7) — a
  blocking read on .NET Framework respects neither `ReadTimeout` nor `CancellationToken` once pending
  with no data, so both transports poll instead.

### SSH accepts a wrong password for a password-less account — and that is the router, not a bug here

`admin` with an **empty** password authenticates over SSH with method `none`:

```
ssh -o PreferredAuthentications=none admin@<host> "/system/identity/print"   →   name: CHR
```

The server grants a full session without ever checking a password, so a wrong password is accepted too
— measured per account (7.23.2), each row below ran `/system/identity/print` **and** `/user/print` and
got real data back:

| account | password given | result |
|---|---|---|
| password-less | correct (empty) | OPENED |
| password-less | **wrong** | **OPENED** |
| password-protected | correct | OPENED |
| password-protected | **wrong** | **REFUSED — `Permission denied (password)`** |

Telnet and WinBox-CLI, which have no `none` method, reject the same credentials. So SSH *is* an access
control for an account that has a password; an account without one has no check at all, and this is
RouterOS policy — not something a client can detect or refuse. `LoginFailureTest` reports Inconclusive
for SSH while `App.config` points at a password-less account, rather than pretending to cover it; point
it at a password-protected account to exercise SSH refusal.

---

## 6. VT100 cursor-probe negotiation is mandatory

Without responses to RouterOS's cursor probe (`ESC[6n` → cursor report `ESC[row;colR`), the router
treats the terminal as 1×1 and renders no command output — typically just `…\r\n\r\r\r\r] > ` with
nothing else. `Vt100State` tracks cursor position and answers every probe. The width advertised must
be **large** (not 80): RouterOS measures width with `ESC[9999C ESC[6n`, so the reported column is
`min(Vt100State.Width, ~10000)` — `Width` must be at least 10000, or the client truncates its own
answer, RouterOS measures a narrow terminal, and long `as-value` lines get wrapped with `\r\n` inserted
into the data. For the MAC-Telnet framing of the same negotiation (and the ACK-offset requirement it
depends on) see [findings-mactelnet.md](findings-mactelnet.md) §2.

---

## 7. Reading a response: settle, redraw, and echo alignment

Every PTY read here terminates the same way: wait until the accumulated, ANSI-stripped text ends at a
shell prompt that has then stayed silent for the settle window (§4). That test only asks whether **a**
command has finished — never whether **this** one has, because a response can arrive after the read
that asked for it has already returned:

- the read returns as soon as a settled prompt is seen;
- the next command's pre-send drain is gated on `DataAvailable` at one instant;
- so a tail that arrives after both is still sitting in the socket when the next command is sent.

That tail then lands ahead of the next command's own echo, and two independent layers can be fooled by
it: the read loop sees a settled prompt and returns before the router has said a word about the command
it was asked to run, and `CliOutputHelper.CleanOutput`'s head-trim loop sees leftover output that is not
blank, not a prompt and not a fragment of the sent command, and treats it as the first genuine data line.
Neither failure is detectable from the result alone — a read gets the wrong row, a silent-on-success
write gets non-empty "output" that the positional error rule (§10) reads as a rejection, and an `add`
can hand back an `.id` that was never created here, which only fails one call later, in the read-back,
with `no such item`.

**The fix: the command's own echo is the anchor.** RouterOS echoes the command before it answers it, so
the response carries its own identity.

- A settled prompt only terminates a read **once this command's echo is visible on screen**
  (`CliOutputHelper.ContainsEcho`).
- Content in front of the echo that is genuinely foreign — not blank, not a repainted prompt, not an
  asynchronous router log line (§11), not a partial echo — is dropped by `SkipForeignResidue`, and
  logged to the wire trace on channel `cli.align`.

Matching is on whitespace-squashed, lower-cased text (the line editor repaints the echo, it does not
replay the bytes) and only on a leading 40-character slice (the tail of a long `add` may be wrapped or
repainted separately). The **first** match wins: a stored record that happens to quote the command back
(a script `source`, say) can only appear after the echo that introduced it, so a false match later in
the output is harmless.

Prompt-line detection is anchored on the leading `[` of `[user@identity]`, not on a bare
`EndsWith("] >")`/`Contains("] >")` test — a *data* line can legitimately end that way too (a stored
script source such as `source=:put [$x] >`), and an anchor-free test would delete it as if it were the
prompt (`CliOutputHelper.IsPromptLine`/`IsPromptPrefixed`).

A PTY transport can echo a command **more than once** — a character-echo on its own line, then a
line-editor repaint as `<prompt> <command>` — and a multi-line command (e.g. a script `source`
containing `\n`) is echoed across several lines; `CleanOutput` strips every leading echo/blank line, not
just the first, using the same foreign-content test. The trailing prompt is stripped the same way in
reverse — RouterOS can repaint it more than once with blank lines between, so all trailing prompt lines
are removed, not just the last.

Measured over full traced suite runs on all five CLI transports, the "no echo found" trace note fired
zero times: at the moment each read actually returned, its own echo was already present. Requiring it
therefore changes nothing on a healthy session, and only a response that never arrives at all reaches
the receive deadline, where the read throws `CliReadTimeout`. What it does **not** catch: two identical
commands issued back to back (the poll-and-diff `listen` emulation does this) — the previous response's
echo satisfies the gate just as well as the real one, so a splice there is invisible and can only show up
as a duplicated row.

---

## 8. Add, scalar reads, and action commands

- **`add`**: `:put [/ip/address/add address=10.0.0.1/24 interface=ether1]` returns the new record's
  **`.id`** (e.g. `*3`), the CLI equivalent of the API's `=ret=*3`. Without the `:put [...]` wrapper,
  `add` returns nothing useful, not `*N`.
- **Scalars must be read via `print`, not `get`.** `:put [/path get .id=*N value-name=.id]` is invalid:
  `get .id=` is a syntax error, and `value-name=.id` answers "input does not match any value of
  value-name". `.id` and every other scalar are read by selecting the value out of a `print` row instead.
- **Action commands with no per-row output** (`/system/script/run` and similar): the command runs, but
  RouterOS does not return the per-row `!re` output the binary API produces — it is fire-and-forget over
  a terminal. `CliConnectionBase` routes such verbs (`IsActionVerb`) to a non-query path and returns an
  empty result set; calling `ExecuteList()`/`ExecuteScalar()` on one over a CLI transport **throws
  `NotSupportedException`**, directing the caller to `ExecuteNonQuery()` instead
  (`ActionVerbOnReadPath`). `RunScript_Issue53_WillNotFail` (`tik4net.integrationtests/TikCommandTest.cs`)
  asserts exactly this split via `TestBase.IsNonApiTransport()` (true for every CLI transport and native
  WinBox M2 as well — both go through the structured-command model rather than the binary-API sentence
  protocol): `ExecuteList` must throw, `ExecuteNonQuery` must succeed, and the API/REST branch still
  checks the `!re` row count. A small number of verbs (`wol`) are the mirror case — read through a
  *read* method because the binary API returns an empty row for them, even though they act rather than
  query (`CliConnectionBase.IsEmptyRowAction`).

---

## 9. Monitor and streaming commands

### `once` (and its per-verb exceptions)

Continuous commands (`/interface/monitor-traffic`, `/tool/profile`, `/system/resource/monitor`, …)
normally repaint the screen until interrupted, which a request/response CLI transport cannot consume.
`CliMonitorVerbs` appends a per-verb snapshot modifier so the command takes one reading and returns to
the prompt: `once` is the default, but `ping` and `traceroute` have no `once` form and take `count=1`
instead (traceroute confirmed live: `:put […count=1 once as-value]` → `bad parameter once (line 1
column 54)`; the same command without `once` returns its hop rows), and `profile` takes `duration=1`
(it rejects `once` with "expected end of command"). `torch` is the outlier described in §1 — it never
reaches this modifier at all.

### Monitor commands reached through a read method must keep their own inputs

`/ping`, `/tool/traceroute`, `/interface/monitor-traffic` and `/interface/ethernet/monitor` are called
through a **read** method (`ExecuteList`/`LoadList`) — they return rows — but their parameters are the
command's own **inputs** (`address=`, `interface=`), not a print filter. `CliConnectionBase.RunPrint`
recognises these verbs (`CliMonitorVerbs.IsSyncMonitorVerb`) and routes them through
`CliCommandBuilder.BuildMonitorSnapshot(..., includeFilters: true)` instead of the ordinary print path,
which otherwise understands only print modifiers and a `where` clause and would silently drop every
input (`/ping =address=127.0.0.1` sent as `:put [/ping as-value]` fails with `failure: resolve failed`).
`includeFilters` is needed because by the time a descriptor reaches this point,
`TikGenericCommand.ResolveParamsForRead` has already rewritten the caller's parameters into Filter form,
and a monitor has no query semantics of its own for that rewrite to mean anything else. The async
monitor path (`RunMonitorAsync`) never had this gap — it always built through
`BuildMonitorSnapshot`.

### A monitor's success is recognised by producing a record, not by phrase

No phrase classifier in `CliErrorParser` catches every rejection text a monitor can produce (e.g.
"input does not match any value of interface"). Instead, `ParseMonitorSnapshot` treats a **successful**
snapshot as always emitting at least one `.id=…` record, and a rejected one as printing only its
complaint — so output with zero records is the router's refusal, regardless of wording. Empty output on
its own is *not* an error: torch's `as-value` form legitimately prints nothing, which is exactly why it
is excluded from this path (§1).

### `:put […as-value]` prints nothing until the command finishes

Streaming is a property of the command's own shape, not of the read loop: `:put` receives an
already-complete array as its argument, so RouterOS sends nothing until the whole command has finished.
Measured on 7.23.2 (telnet):

```
:put [/ping address=127.0.0.1 count=5 as-value]
  +     5 ms     49 B  command echo
  +  4019 ms    322 B  .id=*0;host=…;seq=0;…  ← ALL five records at once, at the end
  +  4066 ms     24 B  prompt
```

against the bare interactive form of the same command, which streams:

```
/ping address=127.0.0.1 count=5
  +     4 ms     33 B  command echo
  +    58 ms    150 B  header + seq=0 row
  +  1008 ms     68 B  seq=1
  +  2025 ms     68 B  seq=2
  +  3021 ms     68 B  seq=3
  +  4003 ms    148 B  seq=4 + summary sent=/received=/min-rtt=…
```

This holds across the whole CLI family — it is router behaviour, not transport behaviour. Self-terminating
monitors (`ping`, `traceroute`; `CliMonitorVerbs.Kind.Once`) are therefore sent in their **bare**
interactive form (`CliCommandBuilder.BuildInteractiveMonitor`) and read line by line: every CLI read loop
calls `CliLineStreamer.Feed(stripped)` at the point where the stripped text is already computed for
prompt detection anyway. The prompt remains the sole terminator of the read — streaming only changes
*when the caller learns about* individual rows, not when the read itself completes. Continuous monitors
(`monitor-traffic`, `profile`, …) are unaffected: they are polled with a `once` snapshot every
`MonitorPollIntervalMs` (500 ms), so rows already arrive that way.

`CliLineStreamer` counts **delivered lines**, not a character offset into the buffer — `VtStripper`
re-runs over the whole accumulated text on every pass, and a chunk boundary that falls mid-escape-sequence
shortens the text on a later pass, which would make a character pointer drift and deliver shifted data.

### The interactive table is read by column offset, not whitespace splitting

An empty column stays empty and the columns after it keep their own positions — splitting on whitespace
instead would shift a later field into an earlier one's slot on any row with a missing value:

```
  SEQ HOST                                     SIZE TTL TIME       STATUS
    0 127.0.0.1                                  56  64 107us
    1 203.0.113.99                                                 timeout
```

Measured offsets: header `SEQ[3..5] HOST[7..10] SIZE[48..51] TTL[53..55] TIME[57..60] STATUS[68..73]`,
values `0[4] 127.0.0.1[6..14] 56[49..50] 64[53..54] 107us[56..60] timeout[67..73]`. A value can start
**one character to the left** of its own header (`107us` at 56 under `TIME` at 57 — the router reserves
one padding character before the column), and a left-aligned value can overflow far past its header
(`203.0.113.99` under `HOST`). A column therefore spans from one character before its own header to one
character before the next header — the one rule `CliTableParser` uses that places both row shapes
correctly. Field names are the header text, lowercased, which happen to match the binary API's field
names exactly (`seq`, `host`, `size`, `ttl`, `time`, `status`) — this table form is actually more
faithful than `as-value`, which renders the same `time` field as `00:00:00.000060` instead of `60us`.
The trailing summary row (`sent=…/received=…/min-rtt=…`) is discarded, since it describes the whole
table rather than one row, and arrives after the last record.

The interactive `traceroute` table starts with a `#` column — the terminal's own row-sequence number,
with no equivalent in the binary API (which returns `.id` in its place). `CliTableParser` keeps the
column for offset purposes only and never emits it as a value.

---

## 10. CLI error text → exception mapping

RouterOS has no structural error channel over a terminal — output and errors are the same text — so
error classification is necessarily by pattern, shared with the API and REST transports through one
`TikTrapClassifier` so the four outcome exception types cannot drift per transport:

| CLI text (also matched from API/REST dialects) | Exception |
|---|---|
| `no such item`, `expected item id` (e.g. `[find .id=…]` resolves to nothing), `missing or invalid resource identifier` | `TikNoSuchItemException` |
| `no such command`, `bad command name`, `expected end of command`, `no such directory`, `syntax error` | `TikNoSuchCommandException` |
| `already have … such …`, `item with such name already …` | `TikAlreadyHaveSuchItemException` |
| `failure:` / `error:` prefix, or any unrecognised non-empty text on a verb classified below | `TikCommandTrapException` |

Verbs RouterOS answers with **no output at all** on success (`set`, `remove`, `enable`, `disable`,
`move`, `unset`, `comment` — `CliErrorParser.IsSilentOnSuccessVerb`) get an extra, purely positional
rule: any surviving text after echo/prompt trimming is an error, regardless of how it is worded, because
there is nothing else it could be. This runs last, after the classified kinds above, and is why
`remove`/`set` against a nonexistent `.id` produces `expected item id` → `TikNoSuchItemException` rather
than a generic trap.

---

## 11. The router can write into a live session on its own

RouterOS ships by default with a `/system/logging` rule `topics=critical action=echo`, and `echo` means
**into every open terminal session**, not "to the local console". A line nobody asked for can land in a
session at any time:

```
21:18:05.412 telnet.sock RECV | <CR>23:17:46 echo: system,error,critical login failure for user
                                admin from 203.0.113.31 via api<ESC>[K<CR><LF><CR><ESC>[9999B[admin@CHR] >
```

Properties worth knowing:

- **It is not a login banner** — it can arrive on a long-established session with no IAC negotiation
  nearby (the banner also prints recent log lines, so the two are easy to confuse when scanning a
  trace — filter on whether negotiation bytes are nearby).
- **The router buffers it**: the timestamp inside the line can be many seconds older than the moment of
  delivery, so cause and effect are not adjacent in a trace.
- **It arrives only on a session that is currently idle** between commands.
- **A redrawn prompt follows it**, so a read already in progress still sees a prompt at the end.
- It can be triggered by a failed login on a **different** session (`login failure` is `critical`), but
  delivery depends on the target session being idle, so it is not a reliable way to provoke one on
  demand.

`CliOutputHelper.IsRouterLogLine` recognises such a line by its leading wall-clock timestamp (an
optional `mmm/dd ` date prefix, then `hh:mm:ss `) — no as-value record and no RouterOS diagnostic ever
starts that way. If the line arrives between commands, the pre-send drain simply swallows it. It only
matters when it arrives **inside** a response: `CleanOutput`'s echo/head-trim loop and
`SkipForeignResidue` (§7) both skip a recognised log line explicitly rather than treating it as the
first real content, and the record-join loop discards it from the data as well — otherwise it either
shreds an `as-value` parse (a log line has no `=`, so it gets absorbed as a bogus field) or is read as
the router rejecting a silent-on-success write (§10).

---

## 12. WinBox terminal (`mepty`): a pull protocol

The WinBox terminal's `mepty` `Data` command (`0x0A0067`) does two things at once: it sends keystrokes
**and** pulls whatever output RouterOS currently has pending, one batch at a time. Sending a `Data`
frame and then only reading passively means output larger than one batch (on the order of a few hundred
bytes) never arrives — the session appears to wedge after enough output has accumulated.
`WinboxCliClient.SendPull()` sends an empty `Data` frame (no `Input` key) to keep pulling whenever the
read buffer is empty and the completion prompt hasn't been seen yet; once the prompt has arrived the
read stops pulling and just settles. This is shared by `WinboxCliMacConnection`, so it applies to that
transport too.

This pull mechanism is only half of the `mepty` protocol; the other half — the frame's `Counter` field
being a cumulative **byte acknowledgement**, not a message count, and what happens when it is wrong — is
in [findings-mepty-byte-ack.md](findings-mepty-byte-ack.md).

---

## 13. Tests

- `tik4net.unittests/Cli/RouterOsTranscripts.cs` holds RouterOS 7.23.2 byte streams captured off the
  wire (banner, the nag — which the router repaints, so one read carries `new password>` twice —
  prompt, refusal); `tik4net.unittests/Cli/FakeRouterTerminal.cs` replays them into the real login state
  machine, exercised by `tik4net.unittests/Cli/CliLoginTranscriptTests.cs`. A new RouterOS version is a
  new block in that file, not another live campaign. Two of the transcript tests exist specifically
  because the failure mode is silence: one asserts nothing but Ctrl-C is ever sent while the nag is on
  screen, and one refuses a login whose wording nobody here has seen
  (`Authentisierung fehlgeschlagen`) to prove the positional signal (§4) carries it alone.
- `tik4net.unittests/Cli/CliStreamingMonitorTests.cs` covers `CliLineStreamer` and `CliTableParser`
  against table samples captured verbatim from a live 7.23.2 run over Telnet (§9).
- `tik4net.integrationtests/Protocols/Tests/SshAuthProbeTest.cs` covers the password-less-account
  behaviour in §5.
- `tik4net.integrationtests/LoginFailureTest.cs` (`ConnectionTest.OpenConnectionWithInvalidCredential_WillFailWithProperException`)
  covers login refusal; it reports Inconclusive on SSH when `App.config` points at a password-less
  account (§5).
- `tik4net.integrationtests/TikCommandTest.cs` (`RunScript_Issue53_WillNotFail`) covers the
  action-command split in §8.

---

## Settled questions — do not re-investigate

- **SSH as "exec, no PTY" is not how this transport works, and never will be.** RouterOS's exec channel
  does not produce usable `as-value` output; the interactive `ShellStream` PTY (§5) is not a workaround
  for a missing feature, it is the only channel that works, and the whole shared CLI/PTY stack depends
  on it.
- **A wrong password succeeding over SSH against a password-less account is not a bug in this client.**
  It is the RouterOS SSH server's own `none`-method behaviour (§5); there is nothing to detect or refuse
  from the client side, and `LoginFailureTest`'s Inconclusive report for that configuration is correct,
  not a gap to close in code.
- **The `mepty` "hang after ~N commands" symptom is not a command-count limit, a pull-cadence issue, or
  cursor-probe drift.** It is the byte-acknowledgement rule in
  [findings-mepty-byte-ack.md](findings-mepty-byte-ack.md); the pull mechanism in §12 is necessary but
  was never sufficient on its own.
- **`torch`'s `as-value` form printing nothing is not a missing mapping to add.** It has no working
  one-shot `as-value` form on any RouterOS version tested; the freeze-frame mechanism in §1 is the
  supported path, not a stopgap for one.
