# Findings — MikroTik RouterOS REST API

**Source:** https://help.mikrotik.com/docs/spaces/ROS/pages/47579162/REST+API
**Date:** 2026-05-31
**Retrieval status:** Official page processed by a research agent (Confluence); **the key write-path
behavior (PUT/POST/PATCH/DELETE/unset/move) was additionally verified live** against the
test CHR router (RouterOS **7.21.4 long-term**) from the main session. Lines marked ✅ = verified live,
📄 = from documentation (not verified live).

> Purpose: reference material for `RestConnection : ITikConnection` in `tik4net` core (chapter A).
> See [A-rest-implementation-plan.md](A-rest-implementation-plan.md).

---

## 1. Basics

- ✅ Base URL: `http(s)://<host>/rest/<menu-path>`. CLI path `/ip/firewall/address-list`
  → URL `/rest/ip/firewall/address-list` (slashes map 1:1; the leading `/` is added implicitly and is not part of the API path).
- ✅ Available since **RouterOS 7.1+**. Service `www` (HTTP, port 80) or `www-ssl` (HTTPS, port 443) under `/ip/service`.
- ✅ **All values in JSON are strings** — bool as `"true"`/`"false"`, `.id` as `"*1"`,
  numbers as `"1500"`. Never expect a native JSON bool/number. (Matches the binary API → the existing
  conversion logic in the `tik4net.entities` mapper works unchanged.)
- ✅ `.id` has the same format as the API (`*1`, `*6`, …) → the mapper's `Id` property works unchanged.
- 📄 **Content-Type must be exactly `application/json`** (without `; charset=utf-8`) — older ROS versions did a
  strict string match and returned **HTTP 415**; fixed in v7.2RC5, but in C# `HttpClient` it's safer to send
  `application/json` without a charset. (Worked either way on 7.21.4, but stick with the safe variant.)

---

## 2. HTTP verbs — mapping (✅ verified live on 7.21.4)

| Operation | HTTP | URL | Body | Notes |
|---|---|---|---|---|
| **print / list** | `GET` | `/rest/<path>` | — | returns a JSON **array** of objects (incl. `.id`) |
| **print + proplist** | `GET` | `/rest/<path>?.proplist=a,b` | — | restricts the returned fields |
| **print + filter/proplist** | `POST` | `/rest/<path>/print` | `{".query":["name=x"],".proplist":["a","b"]}` | filter **must** go through `.query` (not `?`-keys → 400) |
| **add** | `PUT` | `/rest/<path>` | `{"field":"val",...}` | ✅ returns the **entire created object incl. `.id`** (no separate `ret`) |
| **set** | `PATCH` | `/rest/<path>/{id}` | `{"field":"val",...}` | `{id}` without `*`? — works with `*X` in the path; returns the whole object |
| **set (alt)** | `POST` | `/rest/<path>/set` | `{".id":"*X","field":"val"}` | `.id` in the body — equivalent to PATCH |
| **remove** | `DELETE` | `/rest/<path>/{id}` | — | ✅ |
| **unset (clear a field)** | `PATCH` | `/rest/<path>/{id}` | `{"field":null}` or `{"field":""}` | ✅ clears the field; **`POST /unset` returns 400** (see §4) |
| **move (ordered)** | `POST` | `/rest/<path>/move` | `{".id":"*X","destination":"*Y"}` or `{"numbers":"*X","destination":"*Y"}` | ✅ both forms work; `destination` = `.id` of the element it is inserted **before** |
| **arbitrary command** | `POST` | `/rest/<path>/<command>` | `{...}` | e.g. `/print`, `/set`, `/move`, `/monitor` |

> ⚠️ **POST to the collection root (`/rest/<path>`) without a command → HTTP 400.** POST is only for
> `/rest/<path>/<command>` endpoints. **Add is done with `PUT` on the collection root**, not POST.
> (This corrects the original assumption in the plan, where add was assumed to be POST.)

### Verified examples (abbreviated responses)

```
PUT  /rest/ip/firewall/address-list  {"list":"x","address":"192.0.2.50"}
 → 200 {".id":"*1","address":"192.0.2.50","list":"x","disabled":"false","dynamic":"false",...}

PATCH /rest/ip/firewall/address-list/*1  {"comment":"hello"}
 → 200 {".id":"*1",...,"comment":"hello",...}

PATCH /rest/ip/firewall/address-list/*1  {"comment":null}   → comment is cleared to ""

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
- 📄 `.detail` — requests detailed fields (analogous to `print detail`).
- ✅ Filters passed via `?name=value` in the POST/print body **do not work** (→ 400). Only through `.query`.

**Recommendation for `RestRequestBuilder`:** no filter and no proplist → `GET /rest/<path>`. As soon as a
filter or proplist is present → `POST /rest/<path>/print` (handles both uniformly).

---

## 4. Unset — watch out (✅ verified)

- **There is no working `POST /rest/<path>/unset`** on 7.21.4 — all variants
  (`{".id","value-name"}`, `{"numbers","value-name"}`) return **HTTP 400**.
  (The research agent reported `POST /unset {".id","value-name"}` — this does **not** hold on 7.21.4.)
- **Working replacement:** `PATCH /rest/<path>/{id}` with `{"field":null}` or `{"field":""}` → clears the field.
- ⚠️ Semantic difference: the tik4net mapper uses `/unset` to **reset a field to its default**. REST `PATCH null`
  sets an **empty value**, which for some fields may not be the same as "revert to default".
  For most text fields (comment, etc.) it is equivalent. **Document as a known limitation.**

---

## 5. Authentication

- ✅ **HTTP Basic auth** (`Authorization: Basic base64(user:pass)`), verified with an empty password.
- 📄 No token/cookie mechanism by default — Basic auth on every request.
- HTTPS: a certificate on the router is **mandatory** (.NET `SslStream` does not support anonymous-DH); accept
  self-signed certificates via `ServerCertificateCustomValidationCallback`. See
  [A-rest-implementation-plan.md §0.1](A-rest-implementation-plan.md).

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
    user admin logged in from 192.168.4.31 via rest-api
```

Counted on the live log: **`rest-api`: 4× logged in, 0× logged out** — versus `api` 81/74 and `winbox`
317/318, which balance. Logout is **never** logged for REST, and that is the ex-post signal: the difference between
the login count and the logout count per `via`.

#### Practical consequence

**The row count in `/user/active` measures nothing about the client.** The 164 rows measured (109 `api` + 55 `rest-api`)
in P2.35 match exactly that ~2:1 ratio and are the router's own accounting — the close path in tik4net is clean on
all transports (see `UserActiveSessionProbeTest`). There is nothing to fix in `RestConnection.Close()`;
anything added there would have no effect on this.

---

## 6. Errors

- 📄 The error response is JSON: `{ "error": <http-status-int>, "message": "<text>", "detail": "<text>" }`.
  The HTTP status matches `error` (400/404/415/500…).
- ✅ A malformed request (e.g. a filter passed as a `?`-key, POST to the collection root, an unknown body) → **400**.
- Mapping onto existing tik4net exceptions (in `RestCommand`): the `message`/`detail` text contains analogues
  of binary-API traps — `"no such command"`, `"no such item"`, `"already have such item"` →
  `TikNoSuchCommandException` / `TikNoSuchItemException` / `TikAlreadyHaveSuchItemException`;
  other 4xx/5xx → `TikCommandTrapException`. HTTP 401 → `TikConnectionLoginException`.
  (The exact REST error texts should be **verified during implementation** and the mapping completed.)

---

## 7. Capability gaps (📄 + reasoning)

- ⚠️ **Superseded by [§12](#12--rest-has-a-listen-and-it-delivers-nothing-p226-2026-08-02) — read that first.**
  This section reasoned from protocol style ("REST is request-response"), which turned out to be the wrong
  kind of argument even though the conclusion about *push* held: RouterOS does have a `/rest/<path>/listen`,
  it is accepted, and it delivers nothing. `Listen` is now supported by polling, as on the CLI transports.
- **No server push** — the router's own streaming form never flushes (§12).
- **No streaming/follow.** Monitor commands (`/interface/monitor`, `/tool/ping`, etc.) must be called with
  `{"once":""}` (or `once`/`count`/`duration` — §12.2), otherwise the request hangs with no output.
  Continuous results (Torch, continuous ping) are produced by re-issuing a bounded snapshot on a timer.
- 📄 **~60s hard timeout** on a request on the router side → long-running operations cannot be kept open.
- → In the capability matrix: `Crud` and `Listen` (polled) yes; `Streaming`, `RawSentences`, `Tagging` no.

---

## 8. Open questions / to verify during implementation

1. Exact REST error `message`/`detail` texts for "no such command/item", "already have" → refine the mapping in §6.
2. `{id}` in the PATCH/DELETE path — verified with `*X` (with the asterisk). Verify URL-encoding of `*` (raw `*1` works).
3. Multi-value fields (e.g. comma-separated lists) — how REST accepts/returns them (comma-string? array?).
4. `.detail` and `.proplist` interaction; behavior of `?.proplist` vs `.query` proplist on GET.
5. Exact `once` format for monitor endpoints (`{"once":""}` vs query `?once`).
6. Behavior of `PATCH null` vs `""` on numeric/enum fields (default vs empty) — §4 limitation.

---

## 9. Impact on the plan

- **Mapping correction:** add = **PUT** (not POST); POST only for `/<command>` endpoints. Reflected in
  [A-rest-implementation-plan.md §5.1](A-rest-implementation-plan.md).
- `ExecuteScalar` for `Save` (reading the new `.id`): PUT returns the whole object → read `.id` from the response body.
- Mapper's `/unset` → `PATCH {field:null}` (with a known-limitation note).
- `/move` → `POST /<path>/move {".id"|"numbers", "destination"}`.

---

## 10. ✅ Action commands cannot be told apart from the path (P2.48, 2026-07-31)

`/log/error` and `/ip/address` have the **same shape** — the last segment gives no way to tell "menu vs. action"
apart just by looking at the text. Up through 4.0, `RestRequestBuilder` handled this with a fixed allow-list of known write verbs, and
attached everything else to the path with an implicit `print`. `connection.LogError(…)` therefore went out as
`GET /rest/log/error`.

**Verified live on 7.23.2** (curl, outside our code):

```
GET  /rest/log/error                              → 400 {"detail":"no such command","error":400}
POST /rest/log/error   {"message":"…"}            → 200 []      and the row shows up in /log
POST /rest/log/info    {"message":"…"}            → 200 []      and the row shows up in /log
POST /rest/log/warning {"message":"…"}            → 200 []      and the row shows up in /log
POST /rest/log/debug   {"message":"…"}            → 200 []      but NOTHING in /log
```

- So the router is **not** the limiting factor — the parity rule holds, this was our bug.
- `debug` is accepted, but the row is only written if `/system/logging` lets it through (not on a default
  configuration). "200 with nothing in the log" is correct router behavior, not a silent failure.

**Fix:** the builder gets a `RestCallKind` from the caller — i.e. *which method* was used to run the command.
`ExecuteNonQuery()` (no rows returned) ⇒ an unknown last segment is an **action**; reads keep their original
meaning (part of the path plus an implicit `print`). The rule is deliberately "**POST the path as it came**", not
"strip the last segment as a verb": both give the same URL, but splitting means guessing which segment is the operation —
and for `/tool/wol` the operation **is the entire path** (the same trap CLAUDE.md records for wol under `print`).

**Watch out for `/tool/wol`:** it is also reachable via a *read* method (it returns rows), and
`RestCallKind.NonQuery` does not cover that case — which is why `wol` stays in `_writeVerbs`.

**WinBox native won't catch this either:** `/log` = handler `[3,4]`, `cmds={}` in the `.jg`, and across the entire catalog (18
plugins, 805 windows) there is no `doit`/`action` for writing to the log. WinBox itself cannot write a row to the
log, so this isn't a case of a badly formed request — the transport reports `NotSupportedException` and states
what the handler offers instead.

---

## 11. ✅ Monitor commands ran into `POST /path/print` (P2.51, 2026-08-01)

The same trap as §10, a different set of paths. `/ping`, `/tool/traceroute`, `/interface/monitor-traffic`,
`/tool/torch`, and `/tool/profile` are called via the **read** method (they return rows), so
`RestCallKind.NonQuery` does not cover them; and since none of those names were on the verb list, they were
picked up by the branch with the implicit `print`.

**Verified live on 7.23.2** (curl, outside our code):

```
POST /rest/ping/print                                     → 400 {"detail":"no such command"}
POST /rest/ping  {"address":"127.0.0.1","count":"2"}      → 200 [{seq:0,…},{seq:1,…}]
POST /rest/interface/monitor-traffic/print                → 400 {"detail":"no such command"}
POST /rest/interface/monitor-traffic {"interface":"ether1","once":""}
                                                          → 200 [{name:"ether1",rx-bits-per-second:…}]
POST /rest/tool/traceroute {"address":"127.0.0.1","count":"1"}
                                                          → 200 [{address:"127.0.0.1",…}]
```

So the router is again not the limiting factor. `_monitorCommands` is therefore checked **before**
the split into verb+path, and POSTs the path as it came — the same rule ("don't guess which segment is the operation") as in §10.
`monitor` (`/interface/ethernet/monitor`) is not in the list: it has long been in `_writeVerbs`, and both branches
produce the same URL for it anyway.

**`once` is required for REST.** `POST /rest/interface/monitor-traffic {"interface":"ether1"}` without `once`
**never responds** (measured: 8 s without a single byte before we cut it off) — the monitor keeps running and the HTTP
request hangs. The mapper sends `once` (`InterfaceMonitorTraffic.GetSnapshot`), so shipped entities are unaffected,
but a caller building the command by hand needs to add it themselves.

---

## 12. ✅ REST *has* a `listen`, and it delivers nothing (P2.26, 2026-08-02)

§7 recorded "no `/listen` / push — REST is request-response" as reasoning from protocol style, which is exactly
the shape of claim CLAUDE.md says to challenge. Challenged, on 7.23.2:

**The verb is real.** RouterOS maps `/rest/<path>/listen` onto its own `print follow-only`, and says so when a
menu cannot take it:

```
POST /rest/system/resource/listen  {}   → 400 {"detail":"unknown parameter follow-only"}
POST /rest/ip/address/listen       {}   → accepted, request held open
POST /rest/log/listen              {}   → accepted, request held open
```

**And it never produces a byte.** Three windows, with real events generated inside each from a separate API
connection:

| window | duration | events during the window | received |
|---|---|---|---|
| `/rest/ip/address/listen` | 25 s | one `set` (comment change) | **0 B** |
| `/rest/ip/address/listen` | 25 s | one `add`, one `set` | **0 B** |
| `/rest/log/listen` | 30 s | 60 `:log info` lines | **0 B** |

Measured at the socket, not through a client library: no response headers arrive either, so this is not client
buffering. RouterOS accumulates the whole REST response and flushes it when the command completes — and
`listen` never completes. The same mechanism explains §11's unbounded-monitor hang, and it is why an unbounded
`/rest/ping` also answers nothing (0 B in 8 s) rather than answering progressively.

So the gap is the router's, and it is now recorded as what the router did rather than as an argument from
protocol style. **What REST gains anyway is `Listen` by polling** — the same
`PollingMonitorEngine` the CLI family and native WinBox already use, since it needs nothing but a repeatable
"read the table". Twelve integration tests moved from skipped to passing (332/99 → 344/87 on
`rest.runsettings`).

### 12.1 Two consequences worth knowing

**Monitor rows arrive at the end, not as they happen.** An async `/ping count=20` over REST delivers 0 rows for
20 s and then all 20. Every other transport streams (the API natively, the CLI family via the bare interactive
form — P2.50, native WinBox via the M2 monitor window), so a test asserting *when* rows arrive has to branch:
`TestBase.DeliversMonitorRowsLive()`.

**An unfinished command keeps the router's REST session busy, and aborting the socket does not free it.**
Measured: a `count=30` ping abandoned by the client at 5 s left every further REST request timing out for the
remaining ~23 s — including opening a *new* connection, because RouterOS reuses one session per (user, source
address) (§5.1). Closing a tik4net REST connection therefore stops our delivery but not the router's work.

### 12.2 The bound is per verb, and one of them is not one second

Because an unbounded monitor answers nothing, REST appends the snapshot bound its verb takes, unless the caller
supplied that parameter themselves — the same fact the CLI transports append to the command line, now stated
once in `TikMonitorVerbs.SnapshotBound`. `torch` is the one that does not fit the pattern: `duration=1` answers
`[]` in 1.5 s and `duration=2` answers rows, which is the same floor the CLI's freeze-frame driver hit from the
other side (a frame needs two intervals).
