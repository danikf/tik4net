# tik4net wiki audit & improvement plan (2026-07-13)

All 39 wiki pages were read and every verifiable claim/sample was checked against the current
source code (branch `master`, 4.x line). Findings are grouped by the requested categories.
Items marked **[FIXED]** were corrected directly in the wiki as part of this pass.

---

## 1. Invalid information (verified against sources)

| # | Page | Problem | Source evidence | Action |
|---|------|---------|-----------------|--------|
| 1.1 | High-level-API-reading-data | `LoadById` documented twice as "Returns null if not found" | `TikConnectionExtensions.LoadById` throws `TikNoSuchItemException` (0 rows) / `TikCommandAmbiguousResultException` (>1) | **[FIXED]** description corrected; `LoadByName` added; null-returning alternative (`LoadSingleOrDefault`) cross-referenced |
| 1.2 | Safe-Mode | REST `SafeMode*` documented as throwing `NotSupportedException` (transport table + Notes) | `TikCommandConnectionBase.SafeModeTake/Release/Unroll` throw `TikConnectionCapabilityNotSupportedException` | **[FIXED]** (the `NotSupportedException` for `WinboxNative.SafeModeUnroll` is correct and kept) |
| 1.3 | WinBox-Native-connection | Capability section claims transport reports `Crud \| Listen` | `WinboxNativeConnection.Capabilities = Crud \| Listen \| SafeMode`; the page also contradicted the capability matrix, the Roadmap and its own MAC sibling page | **[FIXED]** SafeMode added (incl. take/release-only caveat) |
| 1.4 | WinBox-Native-connection | Error table: "other M2 errors → `TikCommandUnexpectedResponseException`" | `TranslateM2Error` fallback returns `TikCommandTrapException` (this very change is listed in Roadmap-4x breaking changes) | **[FIXED]** |
| 1.5 | MAC-Telnet-connection, WinBox-CLI-connection, WinBox-CLI-MAC-connection | Capability sections claim `Crud \| Listen` only | `CliConnectionBase.Capabilities = Crud \| Listen \| SafeMode \| RawCommand` | **[FIXED]** SafeMode (and RawCommand) reflected, `Supports` samples extended |
| 1.6 | WinBox-Native-MAC-connection | Text says "CRUD **and Listen**" while quoting flags `Crud \| Listen \| SafeMode` in the same sentence | flags are correct; wording contradicted them | **[FIXED]** |
| 1.7 | History | 4.0.0-alpha notes name the raw pass-through API `ExecuteRaw` | actual API is `connection.CreateRawCommand(raw)` (`TikRawCommandExtensions`) | **[FIXED]** |
| 1.8 | login-versions | "login process was unified (RouterOS 3.5.0 era…)" | 3.5.0 is a **tik4net** version (see History 3.5.0.0: "Unification of login process"), not a RouterOS version | **[FIXED]** |
| 1.9 | Testing-high-level-API | "Save on an existing entity calls `/print` (to diff fields), then `/set`" | 4.x default `TikSaveMode.OnlyChanges` never does the `/print` round-trip; a snapshot-less entity sends all writable fields, a fake-loaded entity diffs its snapshot | **[FIXED]** section rewritten to the 4.x flow |
| 1.10 | MCP-server | "Only `Api`/`ApiSsl` support Listen/Streaming; the rest are request/response only" contradicts the capability matrix (CLI transports do report Listen) | `CliConnectionBase` reports Listen; in MCP context every call is one-shot anyway | **[FIXED]** reworded; unsupported MCP transports (`Ssh`, `WinboxNativeMac`) now stated explicitly |

Verified-correct claims worth noting (no action): exception tree on Exception-handling matches the
code exactly; MNDP `Discover()` 60 s / `FindMacByHost` 5 s defaults are right; capability matrix on
Connection-types-and-capabilities is right (the per-transport pages were the stale side);
`tik4net.testing` builder API (`WithEntities/WithScalarResponse/WithNonQuery/WithTrap/WithResponse`,
`AssertWasSent`, `GetCallCount`, `SentCommands`, `WithId`/`WithValue`) all exist as documented.

## 2. Invalid samples / invalid sample syntax

| # | Page | Problem | Action |
|---|------|---------|--------|
| 2.1 | Getting-started | Page ended with literal `</content>` `</invoke>` tool-call artifact | **[FIXED]** removed |
| 2.2 | Testing-high-level-API | Samples set read-only properties via object initializer: `new IpAddress { Id = "*1", … }`, `new QueueSimple { Id = "*1", … }` — `Id` has a private setter, does not compile; `new ToolTorch { TxBps = 1000, RxBps = 500 }` — properties are `Tx`/`Rx` **and** read-only | **[FIXED]** switched to the page's own `WithId`/`WithValue` helpers (same pattern as `FakeConnectionSampleTest.cs`) |
| 2.3 | Communication-debugging-&-testing | Same `new IpAddress { Id = "*1", … }` issue in quick example; `Connection.OnWriteRow` casing | **[FIXED]** |
| 2.4 | Low-level-API, How-to-use-tik4net-library, High-level-API-with-O-R-mapper, CRUD-examples-for-all-APIs, High-level-API-CRUD, High-level-API-reading-data, High-level-API-advanced | `Connection.Xxx` (capital C — a leaked test-class field name) used where the sample defines `connection` — samples don't compile | **[FIXED]** all normalized to `connection.` |
| 2.5 | High-level-API-advanced | `existingAdressList` typo (variable defined as `existingAddressList`) breaks the SaveListDifferences sample | **[FIXED]** |
| 2.6 | TikListMerge | Example method missing a closing brace | **[FIXED]** |
| 2.7 | High-level-API-entities | `public string Mtu { get; set; }}` — stray `}` | **[FIXED]** |
| 2.8 | ADO.NET-like-API, High-level-API-reading-data, High-level-API-advanced, TikListMerge | `##Heading` / `###Heading` without a space — GitHub markdown does **not** render these as headings (they show as literal text) and the `High-level-API-advanced#asynchronous-loading` anchor used from Home was dead | **[FIXED]** space added everywhere |
| 2.9 | High-level-API-reading-data | `var queues = connection.LoadAll<QueueTree>()` missing `;` | **[FIXED]** |

## 3. Duplicate information

| # | Where | Problem | Action |
|---|-------|---------|--------|
| 3.1 | Home | "Where to go next", "Choosing the three API levels" and "Documentation" repeat the same set of links three times | **[FIXED]** "Documentation" merged into "Where to go next"; API-level list kept once |
| 3.2 | Getting-started vs How-to-use-tik4net-library | Overlap is intentional (tutorial vs API-level overview) and both cross-link each other | no change |
| 3.3 | Per-transport capability boilerplate (5 CLI pages) | Repetition is deliberate (each page is a self-contained landing page); made consistent instead of deduplicated | consistency fixed via 1.5 |
| 3.4 | README vs Home | README repeats Home's intro — acceptable for a GitHub landing page; README now links (rather than restates) the 4.0 feature pages | **[FIXED]** in README |

## 4. Structure

* **[FIXED]** Broken headings (2.8) were the biggest structural defect — several pages rendered as
  one flat wall of text with literal `##` characters.
* **[FIXED]** Home page nav consolidated (3.1).
* Kept as-is (reasonable structure): transport pages all follow the same template
  (status banner → prerequisites → basic usage → TikConnectionSetup → capability → notes → see also);
  testing docs split low/mid/high mirrors the three API levels.

## 5. Missing info / docs

| # | Where | Gap | Action |
|---|-------|-----|--------|
| 5.1 | High-level-API-reading-data | `LoadByName` (used by other wiki pages' samples) was not listed at all; behaviour of not-found for `LoadSingle`/`LoadById`/`LoadByName` vs `LoadSingleOrDefault` was not spelled out | **[FIXED]** |
| 5.2 | MNDP | `TikInstanceDescriptor` table missed `SoftwareId`, `IPv6`, `Unpack` | **[FIXED]** |
| 5.3 | MCP-server | Did not say that `Ssh` and `WinboxNativeMac` are not wired into the MCP tool | **[FIXED]** |
| 5.4 | README | No links to the flagship 4.0 docs (Safe Mode, Change tracking, Exception handling, unit testing, command translation); features list pre-dated 4.0 | **[FIXED]** in README (repo branch) |
| 5.5 | High-level-API-tools | "Advanced usage tutorial … coming soon" stub; wiki importer described as "(currently used)" — worth revisiting whether the entity generator/wiki importer description still matches the tools | future work (needs decision from maintainer) |
| 5.6 | Entities coverage | No wiki page lists the built-in entity classes (High-level-API-entities explains the *concept* only). With the 4.x entity push (health, tunnels, hotspot, …) an auto-generated "supported entities" index would help discoverability | future work (could be generated from `[TikEntity]` attributes) |
| 5.7 | History | 1.3.0.0/1.4.0.0/1.5.0.0 dates are mutually inconsistent (1.4 dated after 1.5) | left as-is — historical record, cannot verify true dates from source |

## Future work (not done in this pass)

1. **Supported-entities index page** (5.6) — generate from `[TikEntity]` metadata; keeps itself honest.
2. **High-level-API-tools refresh** (5.5) — verify which generator tool is current, finish the stub section.
3. **Consider merging** `Testing-low/mid/high` into one page with three sections if maintenance cost of
   three pages grows; today the split mirrors the API levels and is fine.
4. **Automate sample compilation** — most invalid samples found here would be caught by extracting
   ```cs blocks from the wiki into a compile-only test project (several samples had drifted from the
   code for years).

---

## How to apply the wiki fixes

The corrected pages are in `wiki-fixes.patch` (a `git format-patch` of one commit against the wiki repo).
Apply with:

```
git clone https://github.com/danikf/tik4net.wiki.git
cd tik4net.wiki
git am path/to/wiki-fixes.patch
git push
```

(The patch could not be pushed directly from the automation session — the GitHub wiki repo is read-only there.)
