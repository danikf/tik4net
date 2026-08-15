# findings-rest-api.md — investigation history

Superseded diagnoses, incidents and pinned measurements for the REST transport.
The current behaviour is in [findings-rest-api.md](findings-rest-api.md); this file is history and describes
nothing about how the code works today.

Indexed from [HISTORY.md](HISTORY.md), which carries the transferable lessons.

---

## Diagnoses that were wrong

### REST "no server push" — reasoned from protocol style, not from the router

**First diagnosis (wrong):** REST has no `/listen` and no push mechanism, because "REST is
request-response" — a conclusion drawn from the shape of the protocol rather than from testing it.

**Actual cause found on re-investigation (7.23.2):** RouterOS *does* expose `/rest/<path>/listen` — it
maps onto the internal `print follow-only` and is accepted by menus that support it (`400 unknown
parameter follow-only` from ones that don't, e.g. `/rest/system/resource/listen`). The verb is real; the
gap is elsewhere: RouterOS buffers the entire REST response and only flushes it when the command
completes, and `listen` (like any unbounded monitor) never completes. Measured with real events injected
from a separate API connection during three open `listen` windows (25 s / 25 s / 30 s) — 0 bytes
received in every case, confirmed at the socket level (no response headers either, ruling out client-side
buffering).

**Why the wrong diagnosis was attractive:** the *conclusion* (no usable push) was correct, so nothing
in production behaviour contradicted it — only the *reasoning* was wrong, and reasoning from protocol
style rather than from a probed router is exactly the shape of claim CLAUDE.md's "assume feature parity
until proven otherwise" rule was written to catch. `Listen` is now implemented for REST by polling (the
same `PollingMonitorEngine` the CLI family and native WinBox already use). This closed twelve integration
tests that had been skipped (332 passing / 99 skipped → 344 passing / 87 skipped on `rest.runsettings`).

### `/tool/wol` over REST — recorded as a REST gap that did not exist

The builder was posting `/tool/wol/print` and had never asked for `/tool/wol` at all — the router was
never given the correctly-formed request. This is the origin of the "assume feature parity across
transports until the router proves otherwise" rule in CLAUDE.md/AGENTS.md.

### REST action verbs — `/log/error` sent as GET because it looked like a menu name

**Symptom:** `connection.LogError(…)` went out as `GET /rest/log/error` and RouterOS answered
`400 no such command`.

**Cause:** `/log/error` and `/ip/address` have the identical shape on the wire — a menu path ending in a
bare word — and nothing in the text tells you whether that last word is a sub-menu or an action verb.
Up through tik4net 4.0, `RestRequestBuilder` handled this with a fixed allow-list of known write verbs
and attached everything else to the path with an implicit `print`, so an action verb that wasn't already
on the list silently became a GET.

**Verified live on 7.23.2** that the router was never the limiting factor: `POST /rest/log/error`,
`/log/info`, `/log/warning` all return `200 []` and the row appears in `/log`; `POST /rest/log/debug`
also returns `200 []` but writes nothing, because `/system/logging` doesn't let `debug` through on a
default configuration (itself correct router behaviour, not a silent failure).

**Fix:** the builder now takes a `RestCallKind` from the caller (which command method was used).
`ExecuteNonQuery()` marks an unrecognised trailing segment as an action; read methods keep the original
"part of the path, implicit print" meaning. The fix deliberately POSTs the path exactly as given rather
than splitting off "the verb" — for `/tool/wol` the whole path *is* the operation, so guessing which
segment names it would have reintroduced the same class of bug.

**Cross-transport note that turned out to be a dead end:** WinBox native cannot be used to double-check
this class of gap, because it cannot write a log row at all — `/log` maps to handler `[3,4]` with an
empty `cmds={}` in the `.jg` catalog across the entire catalog (18 plugins, 805 windows). It reports
`NotSupportedException` rather than attempting the write, so it would never have surfaced the REST bug
either way.

### REST monitor commands — same trap, different paths

The same shape of bug as the `/log` case above, hitting `/ping`, `/tool/traceroute`,
`/interface/monitor-traffic`, `/tool/torch` and `/tool/profile`: none of those names were on the known
write-verb list, so they were reached through a *read* method (they answer with rows) and the builder's
implicit-print branch sent `POST /rest/ping/print` → `400 no such command`. Verified live on 7.23.2 that
the router accepts the path directly (`POST /rest/ping {"address":"127.0.0.1","count":"2"}` →
`200 [{seq:0,…},{seq:1,…}]`, and the same for traceroute and monitor-traffic). Folded into the same
`RestCallKind`-independent "the path itself is the operation" rule as the `/log` fix.

### REST `unset` — PATCH-with-null looked like it worked, until a typed field was unset

**First implementation:** `unset` was translated to `PATCH /rest/<path>/{id}` with `{"field":null}` (or
`{"field":""}`). This cleared free-text fields like `comment` and looked like a working general solution.

**Actual cause of the limitation:** RouterOS validates a `null` value against the field's declared type
*before* treating it as "clear". A free-text field accepts a bare null; a typed one (e.g. `src-address`)
answers `400 value of range expects range of ip addresses`. PATCH-null only ever worked for the subset
of fields with no type constraint — every typed field would 400.

**Fix:** REST also exposes the router's own `unset` operation directly — `POST /rest/<path>/unset` with
`{".id":"*X","value-name":"field"}`, the same spelling and semantics as the binary API's `unset` command.
Verified live on 7.23.2 that this clears typed and untyped fields alike, and — because it is RouterOS's
actual unset operation rather than an emulation via a blanked field — it reverts a field to its declared
default, resolving the earlier "does REST unset go to empty or to default?" ambiguity along the way.
`RestRequestBuilder.BuildUnset` was rewritten to always use the POST form.

### REST singleton `set` — no test ever exercised a singleton write

Singletons (`/system/identity`, `/ip/dns`, `/snmp`, …) have no `.id`, so `PATCH /rest/<path>` — the
general `set` mapping — has nothing to substitute into the URL, and RouterOS answers
`400 missing or invalid resource identifier`. No integration test had covered a singleton write over
REST, which is why the wrong verb (implicitly assumed to be PATCH, same as record `set`) survived
unnoticed. The fix routes singleton `set` (no `.id` in the parameters) to `POST /rest/<path>/set`
instead — RouterOS's own spelling for it, and the same URL shape the binary API already uses.

### REST error mapping — left as an open TODO, then quietly finished

The original write-up flagged "exact REST error message/detail texts" as something to verify during
implementation, with the mapping left incomplete, citing a `RestCommand` class that never existed under
that name. By the time of this rewrite, `RestConnection`'s error path had already been completed: it
classifies error text through the shared `TikTrapClassifier` (also used by the API and CLI transports)
and adds one REST-specific rule — a bare HTTP 404 with no matching phrase is treated as `NoSuchItem`.
The open question closed without anyone going back to update the document that had recorded it as open.

---

## Measurement traps and incidents

### The "impact on the plan" section outlived the plan

An earlier revision of this document carried a "Consequences for the implementation" section that
restated corrections against `A-rest-implementation-plan.md`, an out-of-repo working document that no
longer exists. Its content (add = PUT not POST; PUT returns the whole object so `Save` reads `.id` from
the response body; unset maps to the REST `/unset` endpoint; move = `POST /<path>/move`) was already
fully covered by the invariant-first sections elsewhere in the document (verb-mapping table, the unset
section), so removing the section lost no technical content — only the "this corrects an assumption in
the plan" framing, which had nothing left to point at.

### `/user/active` accounting — the REST session bug (kept in the reference doc, not here)

The REST session-accounting bug (sessions never logging out, confirmed against a live router on 7.23.2,
reported upstream against RouterOS 7.16 through 7.24rc1 with four open MikroTik support tickets) is
**current router behaviour**, not a historical incident — it was deliberately kept in full in
`Docs/findings-rest-api.md` §5.1 rather than moved here. Recorded in this history file only for
completeness of what was reviewed during the rewrite, not because it changed.

### Dated test-count snapshot removed as narrative

The `/listen` investigation section originally reported a specific before/after integration-test count
tied to the moment of the fix ("Twelve integration tests moved from skipped to passing — 332 passing/99
skipped → 344 passing/87 skipped on `rest.runsettings`"). That is a snapshot of test suite state at a
point in time rather than a statement about current router or library behaviour, so it was dropped from
the reference document; the qualitative fact (`Listen` is implemented by polling because RouterOS's own
`listen` never flushes) was kept.

---

## Stale references found and corrected during the present-tense rewrite (2026-08-16)

- **`RestCommand`** — the original document attributed REST error-to-exception mapping to a class named
  `RestCommand`. No such class exists in `tik4net/Rest/`; the mapping actually lives in
  `RestConnection.cs`, built on the shared `tik4net/TikTrapClassifier.cs`. Corrected to cite the real
  files.
- **"exact REST error texts should be verified during implementation and the mapping completed"** — the
  mapping was in fact completed (see `TikTrapClassifier`); the open-question framing was stale and has
  been replaced with the actual phrase table.
- **Unset via `PATCH {field:null}`** — no longer how the library implements `unset`; superseded by
  `POST /rest/<path>/unset {".id","value-name"}`. The old claim "there is no working `POST /unset`" is
  the exact opposite of current behaviour. See the "Diagnoses that were wrong" entry above.
- **`{id}` URL-encoding of `*` listed as an open question** — resolved; every verified example in the
  document (and the unit tests) uses the raw `*1` form successfully, so this is no longer open.
- **`.detail`/`.proplist` interaction listed as an open question** — resolved for `.detail`: it is a
  no-op on REST (`IsSpecialParam` in `RestRequestBuilder.cs` treats it as such, because REST already
  returns full detail by default). `.proplist` behaviour (URL query on GET vs. body array on POST print)
  was already fully described elsewhere in the document.
- **Exact `once` format listed as an open question** — resolved by `TikMonitorVerbs.SnapshotBound` and
  `RestConnection.ApplySnapshotBound`, which supply the bound (`once=""`, `count=1`, `duration=1/2`
  depending on verb) automatically.
- **`set (alt)` table row described as a general PATCH-equivalent** — narrowed to what the code actually
  does: `POST /rest/<path>/set` is specifically the singleton path (no `.id`), not an alternate spelling
  usable for any record.
- **Retained but reduced to one genuinely open item:** multi-value/comma-separated field encoding over
  REST — still unverified against a router.

### REST add/set verbs recorded with the wrong HTTP methods

The doc previously listed REST `add` as `POST` and did not distinguish singleton `set`. Corrected
against `RestRequestBuilder.cs`: `add` is `PUT` to the path itself; `set` is `PATCH {id}` for a
normal record but `POST <path>/set` for a singleton, which has no `.id` to address; a bare action
verb (`/tool/wol`, `/ip/ipsec/key/rsa generate-key`, …) is `POST <path>/<verb>`. `POST` was never
used for `add`.
