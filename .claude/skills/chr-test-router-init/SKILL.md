---
name: chr-test-router-init
description: >
  Provision (or re-provision) the virtual CHR MikroTik test router used by tik4net development, so the
  full 11-transport integration suite can run against it. Use this skill whenever the test router was
  restored / reinstalled / reset / netinstalled / recreated in HyperV, when the user says the router is
  "fresh", "clean", "back to defaults" or that they had to reset the password, when packages need to be
  (re)installed ("doinstalovat packages", "full package set"), when API-SSL/REST-SSL suddenly fail with
  no certificate, when NTP/timezone need setting, when the router's IP / MAC / identity no longer match
  App.config, when you need an MNDP scan to work out which MikroTik on the segment is the test box, or
  when asked to check that the RouterOS version we promise to test against in README/wiki still matches
  the live router. Covers: MNDP discovery + coordinate reconciliation, package set, NTP + timezone, all
  services, self-signed certs for api-ssl/www-ssl, the test/test fallback admin account, an 11-transport
  smoke matrix, and the docs/version reconciliation.
---

# CHR test-router initialization (tik4net)

Brings a **freshly restored CHR** back to the state the integration suite assumes. Runs top to bottom;
every step ends in a live verification, not just a "command accepted".

> **Router coordinates are read from `tik4net.integrationtests/App.config`** — that file is the single
> source of truth (`host`, `user`, `pass`, `routerMac`). Never hardcode an IP or MAC in this skill or
> in a command; read the current values from `App.config` at the start of the run.

All router calls go through the **tik4net MCP** (`mikrotik_call`) — see the `mikrotik` skill. Using our
own library to provision the router is deliberate: it smoke-tests the transports as a side effect.

---

## Step 0 — MNDP scan: find the router and let the user confirm which one it is

**Start here, before touching `App.config` or assuming any IP.** After a VM rebuild the IP, the MAC and
the identity can all have changed, and there is usually **more than one** MikroTik reachable (the
developer's own home router shares the segment). Never guess which one is the test box.

MNDP is a broadcast listen — no credentials, no IP needed. The MCP server does **not** expose it yet
(tracked as **P5.5** in the improvement plan), so drive the library directly against the built DLL:

```powershell
Add-Type -Path "tik4net\bin\Debug\netstandard2.0\tik4net.dll"   # build first if missing
$enc = [System.Text.Encoding]::GetEncoding('iso-8859-1')
[tik4net.Mndp.MndpHelper]::Discover([TimeSpan]::FromSeconds(8), $enc, $false) |
  Select-Object Identity, @{n='IP';e={$_.IpDescription}}, Mac, Version, BoardName | Format-Table -AutoSize
```

Use a short timeout — `Discover()` with no arguments defaults to **60 s**. Zero rows usually means the
host firewall is blocking the UDP 5678 broadcast, not that the router is dead.

Typical output — note that guessing here would be a coin flip:

```
Identity   IP            Mac               Version           BoardName
home_rtr   10.0.0.1      AA:BB:CC:DD:EE:FF 7.17rc3 (testing) RB4011iGS+5HacQ2HnD
CHR        10.0.0.2      AA:BB:CC:DD:EE:01 7.23.2 (stable)   CHR
```

**Then ask the user which entry is the test router** (`AskUserQuestion`, one option per discovered
router, labelled `Identity — IP — BoardName`). `BoardName=CHR` is a strong hint but not proof: there can
be several CHRs, and the old test router may still be running alongside its replacement. If MNDP finds
nothing, fall back to asking for the IP directly.

### Then reconcile all three coordinates with `App.config`

With the router confirmed, compare and **update `tik4net.integrationtests/App.config`** so the suite
targets it:

| `App.config` key | Source of truth | Note |
|---|---|---|
| `host` | chosen router's `IP` | all IP transports |
| `routerMac` | chosen router's `Mac` | a recreated VM gets a **new MAC**, and the 3 MAC-layer transports (`mactelnet`, `winboxclimac`, `winboxnativemac`) then can't find the router at all |
| `routerIdentity` | chosen router's `Identity` | see below |

On an **identity mismatch, ask which side is right** — don't assume. A fresh CHR reports `CHR` while
`App.config` says `MikroTik`, and either answer is legitimate:

> MNDP reports the router's identity as `CHR`, but `App.config` expects `MikroTik`. Rename the router to
> `MikroTik` (`/system/identity/set =name=MikroTik`), or update `App.config` to `CHR`?

> ℹ️ `routerIdentity` is currently a **dead key** — nothing in `tik4net.integrationtests/` reads it, so a
> mismatch breaks no test *today*. Still worth resolving so the config isn't lying, but don't let it
> block the run.

Finally confirm the box over the API and capture the version:

```
mikrotik_call /system/resource/print   → version, board-name, free-hdd-space  (need ≥ ~15 MB)
mikrotik_call /interface/print  =.proplist=name,mac-address   → confirms the MNDP MAC
```

Record the **live version** — Steps 1 and 7 both need it.

---

## Step 1 — Full package set

A fresh CHR ships only the `routeros` bundle. The suite wants the extras (notably `wireless`, so
`/interface/wireless` exists at all, and `user-manager`).

**The router cannot unzip, and `/tool/fetch` has no individual-npk URLs to pull** — so the archive is
downloaded and extracted on the dev box, then uploaded over FTP, then installed by a reboot.

⚠️ **The x86_64 CHR uses the archive named `x86`** — `all_packages-x86_64-<ver>.zip` is a **404**.

```bash
V=7.23.2   # the LIVE version from step 0 — never a different one, npk version must match routeros
SP="$SCRATCH"   # session scratchpad
curl -sI "https://download.mikrotik.com/routeros/$V/all_packages-x86-$V.zip" | grep -iE "^HTTP|content-length"
curl -s -o "$SP/all_packages-x86-$V.zip" "https://download.mikrotik.com/routeros/$V/all_packages-x86-$V.zip"
```

Extract (PowerShell `Expand-Archive -Force`), then upload every `.npk`:

```bash
for f in "$SP"/pkg/*.npk; do curl -sS --ftp-pasv -u "admin:" -T "$f" "ftp://<host>/$(basename "$f")"; done
```

Verify the uploads landed with the right byte counts (`/file/print =.proplist=name,size`), **then**:

```
mikrotik_call /system/reboot   executeMode=nonquery
```

Reboot takes ~40 s. Verify:

```
mikrotik_call /system/package/print  =.proplist=name,version,disabled
```

Expect **12 packages**, all at the live version, all `disabled=false`: `routeros`, calea, container,
dude, gps, iot, openflow, rose-storage, tr069-client, ups, user-manager, wireless. The `.npk` files are
consumed by the install and disappear from `/file` — if one is still there, it did not install (version
mismatch or truncated upload).

---

## Step 2 — NTP client + timezone

Tests compare router time against the dev box; a drifting clock produces confusing failures.

```
mikrotik_call /system/clock/set      =time-zone-name=Europe/Prague  =time-zone-autodetect=no
mikrotik_call /system/ntp/client/set =enabled=yes =mode=unicast =servers=cz.pool.ntp.org,europe.pool.ntp.org
```

Verify `/system/ntp/client/print` reaches **`status=synchronized`** (it reports `waiting` for a few
seconds first) and that `/system/clock/print` shows `Europe/Prague` with the right `gmt-offset`.

---

## Step 3 — Enable every service our transports need

```
mikrotik_call /ip/service/print =.proplist=.id,name,port,certificate,disabled,invalid
```

Required, all `disabled=false` **and** `invalid=false`:

| Service | Port | Used by |
|---|---|---|
| `api` | 8728 | `api` |
| `api-ssl` | 8729 | `apissl` — needs a certificate (step 4) |
| `www` | 80 | `rest` |
| `www-ssl` | 443 | `restssl` — needs a certificate (step 4) |
| `telnet` | 23 | `telnet`, and the `mikrotik-cli-probe` skill |
| `ssh` | 22 | `ssh` |
| `winbox` | 8291 | `winboxcli`, `winboxnative` |
| `discover` | 5678/udp | MNDP discovery for the MAC-layer transports |
| `ftp` | 21 | package upload in step 1 |

Enable with `/ip/service/set =.id=<id> =disabled=no`. Note `reverse-proxy` also sits on **443**
alongside `www-ssl`; on 7.23.2 they coexist fine — only disable `reverse-proxy` if `www-ssl` reports
`invalid=true`.

---

## Step 4 — Self-signed certificates for api-ssl / www-ssl

A restore wipes `/certificate`, leaving both SSL services with `certificate=none` → `apissl` and
`restssl` cannot connect at all. Build a CA and a server cert signed by it:

```
/certificate/add  =name=ca-tik4net =common-name=ca-tik4net =key-usage=key-cert-sign,crl-sign =days-valid=3650 =key-size=2048
/certificate/add  =name=server-tik4net =common-name=<host> =subject-alt-name=IP:<host> =days-valid=3650 =key-size=2048 =key-usage=digital-signature,key-encipherment,tls-server
/certificate/sign =.id=<ca id>     =ca-crl-host=<host>
/certificate/sign =.id=<server id> =ca=ca-tik4net
```

⚠️ **`/certificate/sign` streams progress rows** (`progress=…`, then `progress=done`). Over
`mikrotik_call` the default path returns them as `!re` rows — fine. With `executeMode=nonquery` it
raises `TikCommandUnexpectedResponseException: Single response sentence expected` **even though the
signing succeeded** — verify the certificate instead of trusting the error.

Bind and enable:

```
/ip/service/set =.id=<api-ssl id> =certificate=server-tik4net =disabled=no
/ip/service/set =.id=<www-ssl id> =certificate=server-tik4net =disabled=no
```

Verify via `/certificate/print =.proplist=name,private-key,trusted,issued,akid,skid`: the CA has
`private-key=true trusted=true`, and the server cert's `akid` == the CA's `skid` (that's the proof it
was really signed, not just created). Self-signed is fine — `App.config` sets
`restAllowInvalidCert=true`.

---

## Step 5 — Second admin account: `test` / `test`

Create a second full-privilege account so `admin` is not the only way in:

```
mikrotik_call /user/add  =name=test =password=test =group=full =comment=tik4net-test-fallback
```

Verify it exists **and actually authenticates** — a user that was created but can't log in is worse than
no user, because it will be trusted in an emergency:

```
mikrotik_call /user/print =.proplist=name,group,disabled
mikrotik_call /system/identity/print   username=test  password=test    ← must succeed
```

> **Why this matters.** The P2.13c incident (see
> [`_notes/connections/P2.13c-mepty-recycle-findings.md`](../../../_notes/connections/P2.13c-mepty-recycle-findings.md))
> wedged the lab router by feeding a desynced terminal into RouterOS's `new password>` nag, silently
> changing the `admin` password. There was **no second account**, so recovery needed an out-of-band
> config reset and the whole investigation stalled. `test`/`test` is that missing escape hatch.

A non-empty password on `test` also has a second benefit: it never triggers the change-password nag, so
it is the safer account to use when probing the CLI/mepty layer.

⚠️ **Lab router only.** `test`/`test` on a `full` group is deliberately trivial and must never reach a
device that is routable from anywhere untrusted. The suite itself keeps using `admin`/empty from
`App.config` — leave those settings alone; this account is a fallback, not the test identity.

---

## Step 6 — 11-transport smoke matrix

Prove the box, not one transport. `/system/clock/print` over each:

```
Api  ApiSsl  Rest  RestSsl  Telnet  MacTelnet  WinboxCli  WinboxCliMac  WinboxNative
```

- MAC transports need `routerMac=<mac>`.
- **`WinboxNative` does not map `/system/clock`** — it answers "no M2 handler mapping for path". Use
  `/ip/address/print` instead. Reaching that error still proves auth + M2 worked.
- A `WinboxNative` success also confirms the **`.jg` catalog re-fetch** succeeded on the new RouterOS
  version — the most likely thing to break after a version bump.
- `Ssh` is not exposed by the MCP server (satellite package) — cover it via the integration suite.

---

## Step 7 — Reconcile the version we *promise* to test against ⭐

**Always do this after a version change — it is the step most easily forgotten.**

The claim lives in **two** places, and they must agree with the live router:

```bash
grep -n "Tested and debugged against" README.md
grep -n "Tested and debugged against" ../tik4net.wiki/Home.md
```

- [`README.md`](../../../README.md) line ~4 — "Tested and debugged against **RouterOS x.y.z** (latest stable)."
- [`tik4net.wiki/Home.md`](../../../../tik4net.wiki/Home.md) intro paragraph — same sentence.

Compare with the live version from step 0. **If they differ, ask the user before editing** — bumping the
promise is a claim about what has actually been tested, so it is their call, and it may need to wait
until the suite has actually passed on the new version. Ask explicitly, e.g.:

> The router is now on RouterOS `<live>`, but README and the wiki still promise `<documented>`.
> Update both to `<live>` now, or leave the promise until the full test matrix has passed on it?

Then:

- Update **both** files together — per `CLAUDE.md`, doc changes land with the change, not as a follow-up.
- Also check whether `/system/package/print`-adjacent wiki pages state a minimum version
  (`Connection-types-and-capabilities.md`, `REST-connection.md`, `Safe-Mode.md` carry `7.1+` / `7.18+`
  feature floors). Those are **feature minimums, not the tested version** — leave them alone.
- **Do not bulk-update** the `7.x.y` mentions scattered through source XML docs and `_notes/`
  (`CliCommandBuilder.cs`, `M2Message.cs`, `BgpTest.cs`, …). Those are dated *"verified live against"*
  probe records — historical facts, still true of the version they name.
- The wiki is a separate git clone (`../tik4net.wiki`, see the wiki-location memory) — its commit is
  separate from the repo commit.

---

## Step 8 — Flag the version-bump fallout

A version bump silently invalidates version-pinned material. Tell the user, and note it in whatever
findings doc is in flight:

- **`mikrotik-tests` baseline failure catalog** is pinned to a specific RouterOS version — expect
  baseline drift on the first full run; a newly red test may be a router behaviour change, not a
  regression. (Per `CLAUDE.md`: never *just report* a pre-existing failure — fix it or schedule it in
  `_notes/Reviews/ARCHITECTUREIMPROVEMENTPLAN.md`.)
- **Offline `.jg` dumps** in `_notes/WinboxMessage/<ver>-http/` are version-matched copies. Re-dump
  before trusting them for `winbox-native-dev` work.
- **`user-manager` reinstalls the `um5files/*.html|css|js` tree**, which is what makes `/file/print`
  return `contents` full of `;` and `=` — the known CLI as-value shredding behind
  `ListFilesWillNotFail`. Expected, and useful to have reproducible.
- Topology assumptions in `TestConstants.cs` (`testInterface=ether1`, `testAddress=192.168.1.1/24`,
  `testWirelessInterface=wlan1`) come from `App.config`. On CHR `wlan1` never exists — wireless tests
  are expected to skip/fail regardless of the `wireless` package.

---

## Final checklist

| # | Item | Verified by |
|---|---|---|
| 0 | Router **confirmed by the user** from the MNDP list; `host` + `routerMac` + `routerIdentity` reconciled in `App.config` | MNDP scan + `AskUserQuestion` + `/interface/print` |
| 1 | 12 packages, live version, enabled; no leftover `.npk` | `/system/package/print`, `/file/print` |
| 2 | NTP `synchronized`; timezone `Europe/Prague` | `/system/ntp/client/print`, `/system/clock/print` |
| 3 | All services enabled, none `invalid` | `/ip/service/print` |
| 4 | CA + server cert signed (`akid`==CA `skid`), bound to api-ssl & www-ssl | `/certificate/print` |
| 5 | `test`/`test` admin account exists **and logs in** | `/user/print` + a call authenticated as `test` |
| 6 | 11-transport smoke passes | `mikrotik_call` per transport |
| 7 | README **and** wiki version match live (or user decided to defer) | `grep` + asked |
| 8 | Version-bump fallout reported | — |

Then hand back to the `mikrotik-tests` skill for the real suite: a full `api.runsettings` pass plus the
smoke subset over the other transports.
