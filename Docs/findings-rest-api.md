# REST API (HTTP/HTTPS) — transport behaviour

Reference: [MikroTik REST API docs](https://help.mikrotik.com/docs/spaces/ROS/pages/47579162/REST+API).
Router coordinates for live verification are in `tik4net.integrationtests/App.config`. Complements
[`protocol-coverage.md`](protocol-coverage.md). Reference material for `RestConnection : ITikConnection`
in `tik4net` core (`tik4net/Rest/`).

Lines are marked ✅ (verified live against the test router) or 📄 (from the official documentation page
only, not independently verified) — a statement about evidence quality, not about when it was checked.

> The C# implementation cites these sections by number, so the section numbers below are stable — do not
> renumber a heading without checking who cites it (`grep -rn "findings-rest-api.md" --include="*.cs" .`).

> Superseded diagnoses, incidents and pinned measurements for this area are in
> [`findings-rest-api-history.md`](findings-rest-api-history.md); this document describes current behaviour only.

---

## 1. Basics

- ✅ Base URL: `http(s)://<host>/rest/<menu-path>`. CLI path `/ip/firewall/address-list`
  → URL `/rest/ip/firewall/address-list` (slashes map 1:1; the leading `/` is added implicitly and is not part of the API path).
- ✅ Available since **RouterOS 7.1+**. Service `www` (HTTP, port 80) or `www-ssl` (HTTPS, port 443) under `/ip/service`.
- ✅ **All values in JSON are strings** — bool as `"true"`/`"false"`, `.id` as `"*1"`,
  numbers as `"1500"`. Never expect a native JSON bool/number. (Matches the binary API, so the
  `tik4net.objects` mapper's conversion logic works unchanged.)
- ✅ `.id` has the same format as the API (`*1`, `*6`, …) → the mapper's `Id` property works unchanged.
- 📄 **Content-Type must be exactly `application/json`** (without `; charset=utf-8`) on some ROS
  versions — a strict string match there returns **HTTP 415**. `RestConnection` sends
  `application/json` without a charset to stay on the safe side.

---

## 2. HTTP verbs — mapping (✅ verified live on 7.21.4/7.23.2)

| Operation | HTTP | URL | Body | Notes |
|---|---|---|---|---|
| **print / list** | `GET` | `/rest/<path>` | — | returns a JSON **array** of objects (incl. `.id`) |
| **print + proplist** | `GET` | `/rest/<path>?.proplist=a,b` | — | restricts the returned fields |
| **print + filter/proplist** | `POST` | `/rest/<path>/print` | `{".query":["name=x"],".proplist":["a","b"]}` | filter **must** go through `.query` (not `?`-keys → 400) |
| **add** | `PUT` | `/rest/<path>` | `{"field":"val",...}` | ✅ returns the **entire created object incl. `.id`** (no separate `ret`) |
| **set** | `PATCH` | `/rest/<path>/{id}` | `{"field":"val",...}` | ✅ `{id}` carries the `*` (`*1`); returns the whole object |
| **set (singleton, no `.id`)** | `POST` | `/rest/<path>/set` | `{"field":"val",...}` | ✅ for menus with no `.id` (`/system/identity`, `/ip/dns`, `/snmp`, …); `PATCH` on a singleton has no id to put in the URL and answers `400 missing or invalid resource identifier` |
| **remove** | `DELETE` | `/rest/<path>/{id}` | — | ✅ |
| **unset (clear a field)** | `POST` | `/rest/<path>/unset` | `{".id":"*X","value-name":"field"}` | ✅ the router's own spelling, same as the binary API — see §4 |
| **move (ordered)** | `POST` | `/rest/<path>/move` | `{".id":"*X","destination":"*Y"}` or `{"numbers":"*X","destination":"*Y"}` | ✅ both forms work; `destination` = `.id` of the element it is inserted **before** |
| **arbitrary command** | `POST` | `/rest/<path>/<command>` | `{...}` | e.g. `/print`, `/set`, `/move`, `/monitor` |

> ⚠️ **POST to the collection root (`/rest/<path>`) without a command → HTTP 400.** POST is only for
> `/rest/<path>/<command>` endpoints. **Add is done with `PUT` on the collection root**, not POST.

### Verified examples (abbreviated responses)

```
PUT  /rest/ip/firewall/address-list  {"list":"x","address":"192.0.2.50"}
 → 200 {".id":"*1","address":"192.0.2.50","list":"x","disabled":"false","dynamic":"false",...}

PATCH /rest/ip/firewall/address-list/*1  {"comment":"hello"}
 → 200 {".id":"*1",...,"comment":"hello",...}

POST /rest/ip/firewall/filter/unset  {".id":"*1","value-name":"src-address"}
 → 200, src-address is cleared (works for typed and untyped fields alike — see §4)

POST /rest/ip/firewall/filter/move  {".id":"*7","destination":"*6"}  → moves *7 before *6

DELETE /rest/ip/firewall/address-list/*1  → 200
```

---

## 3. Querying (`print`)

- ✅ **Simple list:** `GET /rest/<path>` → array of all elements.
- ✅ **proplist (field restriction):** `GET /rest/<path>?.proplist=name,type` (query string, comma-separated).
- ✅ **Filter + proplist (recommended uniform approach):** `POST /rest/<path>/print` with body:
  ```json
  { ".query": ["name=ether1", "type=ether"], ".proplist": ["name","type",".id"] }
  ```
  `.query` is an **array of conditions**; `.proplist` is an array (or comma-string) of field names.
- 📄 **Operators in `.query`:** `=`, `<`, `>`, `~` (regex/substring); logical connectives as standalone
  array elements: `"#|"` (OR), `"#&"` (AND), `"#!"` (NOT). Example:
  `[".query":["type=ether","type=vlan","#|"]]` = type==ether OR type==vlan.
- ✅ `detail` is a no-op on REST — the full field set is already returned by default, so there is no
  separate "detail" request shape to build.
- ✅ Filters passed via `?name=value` in the POST/print body **do not work** (→ 400). Only through `.query`.

`RestRequestBuilder.BuildPrint` picks the request shape: no filter and no command-input parameter →
`GET /rest/<path>` (with `?.proplist=` appended if a proplist was requested); a filter or an input
parameter present → `POST /rest/<path>/print` with `.query`/`.proplist` in the body.

---

## 4. Unset uses the router's own `/unset` endpoint

`RestRequestBuilder.BuildUnset` issues `POST /rest/<path>/unset` with body
`{".id":"*X","value-name":"field"}` — the same spelling the binary API uses. ✅ Verified live on 7.23.2
that this clears both free-text and typed fields, and reverts to the property's default the same way
the binary API's `unset` does (it is the router's real unset operation, not an emulation via a blanked
field).

**Not `PATCH {field:null}`.** RouterOS validates a `null` against the property's declared type before
treating it as "clear": a free-text field (e.g. `comment`) accepts it, but a typed one answers
`400 value of range expects range of ip addresses` (measured for `src-address`). `POST /unset` has no
such restriction, which is why the builder uses it for every field, typed or not.

Source: `tik4net/Rest/RestRequestBuilder.cs` (`BuildUnset`); tests in
`tik4net.unittests/Rest/RestRequestBuilderUnsetTests.cs`.

---

## 5. Authentication

- ✅ **HTTP Basic auth** (`Authorization: Basic base64(user:pass)`), verified with an empty password.
- 📄 No token/cookie mechanism by default — Basic auth on every request.
- HTTPS: a certificate on the router is **mandatory** (.NET `SslStream` does not support anonymous-DH); accept
  self-signed certificates via `ServerCertificateCustomValidationCallback`.

### 5.1 Session accounting — the REST session lives above TCP and never logs out (✅ 7.23.2, 2026-07-28/29)

**This is a confirmed RouterOS bug, not ours.** Reported on the forum for versions 7.16 through 7.24rc1 (including 7.22, 7.22.1,
7.23beta2, 7.23rc1, 7.23.1, **7.23.2** = our router, 7.24rc1), with four support tickets
(SUP-214490, SUP-218559, SUP-219610, SUP-219529), no fix and no workaround; once marked as
fixed in 7.16, but it isn't. See the
[forum thread](https://forum.mikrotik.com/t/users-logged-in-via-rest-api-shown-in-active-users-do-not-disappear/269432).
The official [REST API](https://help.mikrotik.com/docs/spaces/ROS/pages/47579162/REST+API) page
says **nothing at all** about session lifecycle — the only timeout it mentions is the 60 s command execution limit.

**Observed behavior model (measured):** the router keeps a session per (user, source-address) pair, and further requests
**recycle** it — a new login is not logged and no new row is created. Over the last ~50 requests
(serial, parallel, with and without `Connection: close`) the router did not log **a single** rest-api login.
Occasionally, though, it does create a new session anyway, and the old one stays hanging forever. This matches what the
forum describes ("it seems to reuse the session occasionally"). So rows don't accumulate per request, but per day.

Each such login creates **two** rows: `via=rest-api` and `via=api` with the same timestamp. The `api` row is
not created by the client — it's the router's internal www→api backend (recognizable in the log by having no address:
`user admin logged in via api` with no `from …`).

#### What does NOT end the session (all verified, all three disproven)

| Claimed mechanism | Result |
|---|---|
| HTTP header `Connection: close` | ❌ **Nothing.** 20 requests with `Connection: close` → 0 new rows and 0 new logins, meaning it ran on a recycled session; it also didn't release any row. |
| Closing the socket / client disposal (`Dispose`, process exit) | ❌ **Nothing.** A row from a single `curl` call was still in the table 90 s after the curl finished, and `Get-NetTCPConnection` on the host showed **no** connections on 80/443. The session lives above the TCP layer. |
| Inactivity timeout on the router | ❌ **Does not exist.** The oldest row lived **~24 hours** and did not disappear. Over 33 minutes of continuous monitoring of 12 rows, exactly one disappeared — the `api` half of a pair after ~10 min — while its `rest-api` counterpart was still there after 25 min. The two halves of the pair don't even share the same rule; the actual rule was **not determined**, and should not be guessed. |

`/user/active/remove` refuses to remove them (`action failed (6)` — the forum reports the same error). The only reliable way to
clear them is a reboot.

#### Ex-post identification — possible, but not via ID

The client gets **no session ID**: the response carries only `Cache-Control / Connection / Content-Length /
Content-Type / Date / Expires / X-Frame-Options` — **no cookie, no session header** (verified with
`curl -D -`). `/user/active` only has `.id, when, name, address, via, group, radius`, so the only
correlator is the client's IP and the timestamp — a session can be attributed to a host, but not to a process or connection.

The router's own log, however, does reveal it — topic `account` (caught by the `info` rule on a default
configuration, so nothing needs to be enabled):

```
/log print where message~"rest-api"
    user admin logged in from <client-ip> via rest-api
```

Counted on the live log: **`rest-api`: 4× logged in, 0× logged out** — versus `api` 81/74 and `winbox`
317/318, which balance. Logout is **never** logged for REST, and that is the ex-post signal: the difference between
the login count and the logout count per `via`.

#### Practical consequence

**The row count in `/user/active` measures nothing about the client.** The 164 rows measured (109 `api` + 55 `rest-api`)
match exactly that ~2:1 ratio and are the router's own accounting — the close path in tik4net is clean on
all transports (see `UserActiveSessionProbeTest`). There is nothing to fix in `RestConnection.Close()`;
anything added there would have no effect on this.

---

## 6. Errors

- ✅ The error response is JSON: `{ "error": <http-status-int>, "message": "<text>", "detail": "<text>" }`.
  The HTTP status matches `error` (400/404/415/500…).
- ✅ A malformed request (e.g. a filter passed as a `?`-key, POST to the collection root, an unknown body) → **400**.
- ✅ `RestConnection` classifies the combined `message`/`detail` text through the shared `TikTrapClassifier`
  (also used by the API and CLI transports), then applies one REST-only rule: a bare `404` with no matching
  phrase is treated as `NoSuchItem`.

  | RouterOS phrase (case-insensitive substring) | Exception |
  |---|---|
  | `no such item`, `expected item id`, `missing or invalid resource identifier` | `TikNoSuchItemException` |
  | `no such command`, `bad command name`, `expected end of command`, `no such directory`, `syntax error` | `TikNoSuchCommandException` |
  | `already have` + `such`, or `item with such name already` | `TikAlreadyHaveSuchItemException` |
  | anything else | `TikCommandTrapException` |
- HTTP 401 → `TikConnectionLoginException`.

Source: `tik4net/Rest/RestConnection.cs`, `tik4net/TikTrapClassifier.cs`.

---

## 7. Capability gaps

- **No streaming / follow.** Monitor commands (`/interface/monitor`, `/tool/ping`, etc.) must carry a bound
  — `once`, `count`, or `duration` depending on the verb (§12.2) — otherwise the request hangs with no
  output: RouterOS buffers the entire REST response until the command completes, and an unbounded monitor
  never completes (§12).
- **No server push.** RouterOS's own `/rest/<path>/listen` is accepted but never flushes a byte (§12);
  tik4net's `Listen` capability is implemented by polling instead, the same `PollingMonitorEngine` the CLI
  family and native WinBox transports use.
- 📄 **~60 s hard timeout** on a request, router-side, so a long-running operation cannot be kept open
  regardless of client-side handling.
- Capability matrix: `Crud`, `Listen` (polled), `AsyncCommands`, `CancelInFlight` — yes. `Streaming`,
  `RawSentences`, `Tagging`, `SafeMode` — no. Full matrix in [`protocol-coverage.md`](protocol-coverage.md).

---

## 8. Open questions

- **Multi-value fields** (e.g. the comma-separated `key-usage` on a certificate) — how REST accepts and
  returns them (comma-string vs. JSON array) has not been verified against a router.

---

<!-- §9 intentionally absent: its content (verb-mapping notes that duplicated §2/§4) was folded into
     those sections. §10–§12 keep their numbers because tik4net/Rest/RestConnection.cs and
     tik4net.integrationtests/AsyncCommandTest.cs cite §12.1 and §5.1 by number. -->

## 10. Action verbs and menu names have the same shape

`/log/error` and `/ip/address` cannot be told apart by looking at the trailing path segment — both are a
single word after a menu path. `RestRequestBuilder.Build` resolves this from `RestCallKind`, supplied by the
caller: `ExecuteNonQuery()` passes `NonQuery`, so an unrecognised trailing segment is posted as an action;
`ExecuteList`/`ExecuteScalar`/`print` pass `Read`, so an unrecognised trailing segment keeps its other
meaning — part of the path, with an implicit `print`.

The rule POSTs the path exactly as given rather than splitting off a "verb": for `/tool/wol` the whole path
is the operation, not a suffix on a menu.

**Verified live on 7.23.2:**

```
GET  /rest/log/error                              → 400 {"detail":"no such command","error":400}
POST /rest/log/error   {"message":"…"}            → 200 []      and the row shows up in /log
POST /rest/log/info    {"message":"…"}            → 200 []      and the row shows up in /log
POST /rest/log/warning {"message":"…"}            → 200 []      and the row shows up in /log
POST /rest/log/debug   {"message":"…"}            → 200 []      but NOTHING in /log
```

`debug` is accepted, but the row is only written if `/system/logging` lets it through (not on a default
configuration) — `200` with nothing in the log is correct router behaviour, not a silent failure.

**`/tool/wol` is reachable through both a read method and `ExecuteNonQuery`** (it returns a row), so it
stays in `RestRequestBuilder`'s explicit write-verb list rather than relying on `RestCallKind.NonQuery`
alone; both paths produce the same URL.

**WinBox native cannot write a log row at all** — `/log` maps to handler `[3,4]` with `cmds={}` in the
`.jg` catalog, and no window across the whole catalog exposes a `doit`/action for it. That transport
reports `NotSupportedException` rather than attempting the write, so cross-checking against it would not
have caught this gap either.

Source: `tik4net/Rest/RestRequestBuilder.cs`; tests in
`tik4net.unittests/Rest/RestRequestBuilderActionVerbTests.cs`.

---

## 11. Monitor commands are POSTed to their own path, not to `/print`

`/ping`, `/tool/traceroute`, `/interface/monitor-traffic`, `/tool/torch` and `/tool/profile` are invoked
through a read method (they return rows), so `RestCallKind.NonQuery` does not cover them —
`RestRequestBuilder` recognises them by name (`TikMonitorVerbs`) and POSTs the path exactly as given,
with the parameters as the command's inputs rather than a print filter.

**Verified live on 7.23.2:**

```
POST /rest/ping/print                                     → 400 {"detail":"no such command"}
POST /rest/ping  {"address":"127.0.0.1","count":"2"}      → 200 [{seq:0,…},{seq:1,…}]
POST /rest/interface/monitor-traffic/print                → 400 {"detail":"no such command"}
POST /rest/interface/monitor-traffic {"interface":"ether1","once":""}
                                                          → 200 [{name:"ether1",rx-bits-per-second:…}]
POST /rest/tool/traceroute {"address":"127.0.0.1","count":"1"}
                                                          → 200 [{address:"127.0.0.1",…}]
```

`monitor` (as in `/interface/ethernet/monitor`) is checked by the same rule as the other monitor names,
and is also on the ordinary write-verb list — both routes produce the same URL for it.

**`once` (or the bound the verb needs) is mandatory for REST** — see §12.2 for why and the exact bound per
verb. The `tik4net.objects` mapper already supplies it (e.g. `InterfaceMonitorTraffic.GetSnapshot`); a
caller building the command by hand has to add it.

Source: `tik4net/Rest/RestRequestBuilder.cs`; tests in
`tik4net.unittests/Rest/RestRequestBuilderMonitorTests.cs`.

---

## 12. REST's own `listen` is accepted, but never delivers a byte

RouterOS maps `/rest/<path>/listen` onto its internal `print follow-only`, and it says so when a menu
cannot take it:

```
POST /rest/system/resource/listen  {}   → 400 {"detail":"unknown parameter follow-only"}
POST /rest/ip/address/listen       {}   → accepted, request held open
POST /rest/log/listen              {}   → accepted, request held open
```

But it never produces a byte. Three windows, with real events generated inside each from a separate API
connection:

| window | duration | events during the window | received |
|---|---|---|---|
| `/rest/ip/address/listen` | 25 s | one `set` (comment change) | **0 B** |
| `/rest/ip/address/listen` | 25 s | one `add`, one `set` | **0 B** |
| `/rest/log/listen` | 30 s | 60 `:log info` lines | **0 B** |

Measured at the socket, not through a client library: no response headers arrive either, so this is not
client buffering. RouterOS accumulates the whole REST response and flushes it only when the command
completes — and `listen` never completes. The same mechanism explains why an unbounded `/rest/ping` also
answers nothing (0 B in 8 s) rather than answering progressively.

**tik4net's `Listen` capability is implemented by polling** instead — the same `PollingMonitorEngine` the
CLI family and native WinBox transports use, since it needs nothing but a repeatable "read the table".

### 12.1 Monitor rows arrive at the end, not as they happen

An async `/ping count=20` over REST delivers 0 rows for 20 s and then all 20 at once. Every other
transport streams (the binary API natively, the CLI family via the bare interactive form, native WinBox
via the M2 monitor window), so a test asserting *when* rows arrive has to branch on transport:
`TestBase.DeliversMonitorRowsLive()`.

**An unfinished command keeps the router's REST session busy, and aborting the socket does not free it.**
Measured: a `count=30` ping abandoned by the client at 5 s left every further REST request timing out for
the remaining ~23 s — including opening a *new* connection, because RouterOS reuses one session per (user,
source address) (§5.1). Closing a tik4net REST connection therefore stops delivery to the caller, but not
the router's own work.

### 12.2 The bound is per verb, and one of them is not one second

Because an unbounded monitor answers nothing, REST appends the snapshot bound its verb takes, unless the
caller already supplied that parameter — `TikMonitorVerbs.SnapshotBound` states it once, shared with the
CLI transports' equivalent command-line modifier:

| verb | bound |
|---|---|
| `ping`, `traceroute` | `count=1` |
| `profile` | `duration=1` |
| `torch` | `duration=2` |
| `monitor`, `monitor-traffic` | `once=""` |

`torch` is the one that does not fit the "one" pattern: `duration=1` answers `[]`, and `duration=2`
answers rows — the same floor the CLI's freeze-frame driver hits from the other side (a frame needs two
intervals).

Source: `tik4net/Rest/RestConnection.cs` (`RunMonitorAsync`, `ApplySnapshotBound`),
`tik4net/Connection/TikMonitorVerbs.cs`; tests in
`tik4net.unittests/Rest/RestMonitorSnapshotBoundTests.cs`.

---

## Settled questions — do not re-investigate

- **REST cannot be made to push or stream — don't add a workaround for it.** `/listen` is real and
  accepted, but RouterOS never flushes it, and an unbounded monitor behaves the same way (§12). Nothing on
  the client side changes that; `Listen` stays implemented by polling.
- **Add is `PUT`, never `POST`, on the collection root.** `POST /rest/<path>` without a trailing command
  is always `400` — RouterOS does not special-case an empty command the way `PUT` implies "create".
- **`/user/active` row counts say nothing about client behaviour** (§5.1). A growing or non-decreasing
  count is the router's own session accounting, not a leak in `RestConnection.Close()`.
