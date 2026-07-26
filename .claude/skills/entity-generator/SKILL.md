---
name: entity-generator
description: >
  Generate a tik4net.objects O/R-mapper entity (C# class) for a MikroTik object/path — decorated with
  [TikEntity]/[TikProperty], correctly typed, R/O vs R/W resolved, documented (including WinBox-native
  GUI names), with Execute/connection-extension methods and a ToString override where appropriate. Use
  whenever the user wants to add/create/scaffold an entity for a `/some/api/path`, port a MikroTik menu
  into tik4net.objects, or update an existing entity from the wiki or a live router. Reproduces the logic
  of the old WinForms tools (tik4net.entitygenerator + tik4net.entityWikiImporter) as a guided, MCP-driven
  workflow.
---

# tik4net entity code generator

Goal: produce a finished, idiomatic entity class under `tik4net.objects/` for a given MikroTik API path
(e.g. `/ip/dhcp-server/lease`). This replaces the two legacy WinForms helpers — you do the same work
they did, but with better sources (live router via MCP **and** the wiki) and you finish the code (the old
tools always produced a draft that a human had to clean up).

Two legacy tools whose logic this skill folds in (read them if you need the exact heuristics):
- `Tools/tik4net.entitygenerator/` — connects to a live router, runs `/path/print [detail]`, takes the
  **union of all field names over all rows**, and infers C# types from the **values**
  (`EntityCodeGenaratorMainForm.Generate` + `GeneratorHelper.DetermineFieldType`).
- `Tools/tik4net.entityWikiImporter/` — parses the MikroTik wiki HTML property tables, reading field
  name, documented type, default, and the R/O-vs-R/W split from the "Properties" vs "Read-only
  properties" sections (`HtmlParserExtensions.ParsePropertyTable` + `DetermineFieldTypeFromDocumentation`).

The router tells you **what fields really exist and their live values**; the wiki tells you **types,
defaults, R/O split, and human descriptions**. Use both and reconcile.

## When to use / inputs

Trigger when the user names a MikroTik path or menu and wants a tik4net entity for it. The only required
input is the **API path** (`/ip/.../...`). If the user gives a WinBox menu name instead, translate it to
the API path first (lowercase, slash-separated).

## Step 1 — Namespace and class name (convention)

From `High-level-API-custom-entities` wiki page. **Class name = last two parts of the API path**, camelized:

```
/ip/firewall/filter   → namespace tik4net.Objects.Ip.Firewall   class FirewallFilter
/interface/vlan       → namespace tik4net.Objects.Interface      class InterfaceVlan
/ip/dns               → namespace tik4net.Objects.Ip             class IpDns
```

- **Sub-namespace** when the sub-path will hold more than one class:
  `/ip/dns` → `Ip.IpDns`, `/ip/dns/cache` → `Ip.Dns.DnsCache`.
- **Three-part names** when a hypothetical sub-namespace would hold only one class:
  `/ip/hotspot/user` → `Ip.Hotspot.HotspotUser`, `/ip/hotspot/user/profile` → `Ip.Hotspot.HotspotUserProfile`.
- Property names: MikroTik field → PascalCase (`add-mac-cookie` → `AddMacCookie`). Camelization drops
  `-` and `.` and title-cases (see `GeneratorHelper.Camelize`).
- Enum member names: MikroTik value → PascalCase (`reply-only` → `ReplyOnly`), value carried by `[TikEnum("reply-only")]`.

**File location**: place the `.cs` file in the folder matching the namespace, e.g.
`tik4net.objects/Ip/Firewall/FirewallFilter.cs`. Match the namespace casing `tik4net.Objects.*` (note the
capital **O** — the runtime namespace differs from the `tik4net.objects` project/folder name). Look at a
neighbouring file in the target folder and copy its `using`/namespace header verbatim.

Before writing, **check the entity doesn't already exist** (Glob the folder / Grep for the path string) —
if it does, update it rather than duplicating.

## Step 2 — Resolve the field list (two sources, reconciled)

### 2a. Live router via MCP (authoritative field names + values)

Use `mcp__tik4net-mcp__mikrotik_call` (the `mikrotik` skill). Read the test router's coordinates
(`host`, `user`, `pass`) from `tik4net.integrationtests/App.config`; use transport `Api`.

```
command: /ip/dhcp-server/lease/print
parameters: ["=detail="]          # add detail to get the full field set, mirrors IncludeDetails
```

- Take the **union of field keys across all returned rows** — different rows expose different optional fields.
- If the menu is empty on the test router, create one throwaway entry first (or pick a router that has data),
  otherwise you get no fields. Clean up anything you add.
- Singletons (e.g. `/system/resource`) return a single record and have **no `.id`** → set `IsSingleton = true`.
- The live **values** drive type inference and reveal which fields are dynamic/R-O (see Steps 3–4).
- **Duplicate-key suffixes**: some menus emit the *same* word twice (e.g. `/interface/list` sends
  `=dynamic=false` twice — confirm with `includeRawTrace: true`). A JSON object can't hold duplicate keys,
  so the MCP losslessly renames the second to `dynamic2`. Map the field **once** under its real name and
  ignore any `<name>2` phantom — it is not a separate property.

**Use more than `Api` — this is the big advantage over the old WinForms `tik4net.entitygenerator`, which
could only talk plain API.** The MCP runs the *same* `command`/`parameters` over 9 transports, so run the
print over several and diff the JSON to learn things one transport alone won't tell you:

- **`Api` + `=detail=`** — baseline field set and live values (type/R-O inference). Source of truth for names.
- **`WinboxNative`** (with `includeRawTrace: true`) — gives you the **WinBox GUI names** for free (Step 6):
  the `.jg`-mapped field names in the trace are exactly the GUI labels. Diffing WinboxNative vs Api also
  surfaces fields WinBox exposes under a different key. (Singletons/ordered lists are the usual mismatch
  spots — see the `mikrotik` skill.)
- **`Rest`** (RouterOS 7.1+) — REST often returns fields with their canonical names and JSON-native types,
  a useful cross-check on whether a field is really boolean/number vs string.
- **CLI (`Telnet`/`WinboxCli`)** — `print detail` over CLI can expose a slightly different column set; and
  fields that only appear under `print stats` (counters) tell you to set `IncludeCliStats = true`.

Pick the transports that add signal for the menu at hand — at minimum `Api` for the field set and
`WinboxNative` when you want GUI names. Don't run all 9 blindly; each is a router round-trip.

### 2b. MikroTik wiki (types, defaults, R/O split, descriptions)

The wiki page for the path (e.g. `https://help.mikrotik.com/docs/...` or the legacy
`wiki.mikrotik.com/wiki/Manual:...`) has property tables. Fetch with WebFetch and read:
- **"Properties"** / "Property Description" table → writable properties.
- **"Read-only properties"** table → R/O properties.
- The "Summary" paragraph → entity-level `///` doc.
- Each row is `field-name (type; Default: x)` + a description column — exactly what
  `HtmlParserExtensions.ParseFieldText` parses. Use the documented **type** and **default**.

Reconcile: field exists on router but not wiki → keep it (R/O string, note "undocumented"); field in wiki
but not on router → likely version-specific, keep it but verify. The router is truth for **names**, the
wiki is truth for **types/defaults/docs**.

### 2c. CLI Tab-completion via MCP — the writable-field enumerator

`mcp__tik4net-mcp__mikrotik_cli_complete` drives RouterOS terminal **Tab-completion** and returns the
tokens the router offers. This is the most reliable router-side source for the **writable field set** and
for **walking the whole menu tree** (it lists parameters that have no value on any current row, which a
`print` cannot show). It runs over a CLI transport (Telnet by default).

```
input: "/interface/vlan add "   → tokens: arp, arp-timeout, comment, copy-from, disabled, interface,
                                   loop-protect, …, mtu, mvrp, name, use-service-tag, vlan-id   ← WRITABLE fields
input: "/interface "            → tokens: child menus (vlan, bridge, ethernet, …) + verbs (print, set, …)
input: "/system/resource "      → tokens: cpu, hardware, irq, export, get, monitor, print
```

Usage rules (match RouterOS completion semantics exactly):
- **Include the trailing space** in `input` — it tells RouterOS to list the *next* word. `"/interface/vlan add "`
  lists the addable parameters; `"/interface/vlan add"` (no space) tries to complete the verb itself.
- For the **writable field set**, complete after `add ` (or `set ` on an existing item): `"<path> add "`.
- **Drop the meta-helpers** the `add ` completion always lists but which are *not* persisted properties —
  never map them: `copy-from` (clone-an-existing-row helper) and `from`/`to`/`place-before` style position
  args on ordered menus. They show up in completion but no entity in the project maps them (verify with a
  Grep for `copy-from`). `comment`/`disabled` *are* real and kept.
- For the **menu tree**, complete after the path: `"/ip "`, `"/ip/firewall "`. Tokens mix child menus and
  command verbs (print/set/add/remove/…) — the non-verbs are the sub-menus to recurse into.
- **Long names are column-truncated** by RouterOS (e.g. `connection-...`, `per-connection-classifier` may
  appear cut). Treat completion as the authoritative *set* of fields; get each field's full name + type from
  `print detail` (2a) and the wiki (2b).
- Supported on all CLI terminal transports (Telnet — the default, WinboxCli, MacTelnet, WinboxCliMac).
  `Api`/`Rest`/`WinboxNative` are rejected: no terminal to complete on.

Recommended resolution flow: **completion (2c) for the authoritative field/menu set → `print detail` (2a)
for real names/values/types → wiki (2b) for types, defaults, R/O split and docs.** A field present in the
`add ` completion but absent from the wiki "Read-only" table is writable; a field that appears only in
`print` output (never in `add ` completion) is read-only.

> Implementation: the tool is backed by `ITikCliCompletion.CompleteCli` on the CLI transports
> (`tik4net/Cli/ITikCliCompletion.cs`, driven from `CliConnectionBase`). It sends `<input><Tab>`, reads the
> listing on a settle window, then `Ctrl-C` to clear the line. `?` is **not** used — it emits nothing over a
> RouterOS PTY (verified live); Tab is the only key that lists.

## Step 3 — Field types

Apply the same precedence the legacy tools use (`GeneratorHelper.DetermineFieldType` /
`DetermineFieldTypeFromDocumentation`):

| Field / signal                                              | C# type |
|-------------------------------------------------------------|---------|
| `.id`                                                       | `string` (always `[TikProperty(".id", IsReadOnly = true, IsMandatory = true)]`) |
| `comment`                                                   | `string` |
| `disabled`, `invalid`, `active`, `dynamic`, `running`       | `bool` |
| value is `true/false/yes/no`, or wiki type `yes \| no`      | `bool` |
| wiki type `integer`, or value parses as a whole number      | `int` (router-call path infers `long`; prefer `int` for documented integers) |
| wiki type `string`, or anything else                        | `string` |
| a documented enumerated set of values                       | a nested `enum` (see below) |
| time/MAC/IP-ish values                                      | `string` — keep as string; annotate `string/*time*/`, `/*MAC*/` etc. |

Important conventions:
- **bool on the wire**: writable `yes`/`no`, read-only `true`/`false` — the mapper handles both; just use `bool`.
- **bool `DefaultValue` MUST be `"no"`/`"yes"`, never `"false"`/`"true"`.** The mapper's `ConvertToString(bool)`
  always emits `yes`/`no`, and `HasDefaultValue` compares that wire form against `DefaultValue`. So a bool with
  `DefaultValue = "false"` NEVER matches its default and is sent on every `/add` — harmless for `disabled` (router
  accepts `disabled=no`) but it breaks fields the router rejects when inapplicable (e.g. `no-summaries` on a
  non-stub OSPF area: `value out of range` / `not applicable`). Use `DefaultValue = "no"` for a field that defaults
  off, `DefaultValue = "yes"` for one that defaults on. (Matches `WireguardPeer.Disabled` / `WirelessSniffer.StreamingEnabled`.)
- **Valueless presence-flags** (e.g. `/routing/table fib`): some fields are toggles whose "on" state reads back
  as `field=` (**empty string**), not `field=yes`. The *write* path works (`Fib=true` → sends `=fib=yes`, router
  accepts), but the mapper's bool `ConvertFromString` treats empty string as `false`, so the *read* path always
  yields `false` regardless of the router's real state. Model it as `bool` anyway (write works), document the
  read-back limitation in the property's XML `///` comment, and in the Add test **do NOT assert the loaded
  value of that flag** (assert the other round-tripped fields instead).
- **Keep "typed-looking" fields as `string`** (time, MAC, IP, rates). Per the wiki: many MikroTik fields
  accept exotic values (`none`, version-specific tokens) that don't fit a strict type. Mark intent with an
  inline `/*type*/` comment, e.g. `public string/*time*/ ArpTimeout { get; set; }`. There are
  `Ipv4Address`/`MacAddress` helper types in the project — use them only when you're sure the field is always
  a plain IP/MAC.
- **R/O properties default to `string`** even when the doc names a richer type (strong typing isn't needed
  for read-only display) — matches `DetermineFieldTypeFromDocumentation`'s `isReadOnly` fallback.

### Enums

When a field has a fixed value set, declare a nested enum decorated with `[TikEnum("wire-value")]`, give it
a `DefaultValue`, and `<seealso cref="...">` it from the property. Pattern (copy from `InterfaceVlan.Arp` or
`FirewallFilter.ActionType`):

```csharp
public enum ArpMode
{
    /// <summary>disabled - the interface will not use ARP</summary>
    [TikEnum("disabled")] Disabled,
    [TikEnum("reply-only")] ReplyOnly,
}

/// <summary>arp - Address Resolution Protocol setting</summary>
/// <seealso cref="ArpMode"/>
[TikProperty("arp", DefaultValue = "enabled")]
public ArpMode Arp { get; set; }
```

### The add-path `DefaultValue` rule (critical — get this right or `Add` tests fail)

On **create** (`/add`), the mapper sends a field only when its current **wire value ≠ the property's
`DefaultValue`** (mandatory fields are always sent). `HasDefaultValue` compares
`Convert.ToString(<wire value>) == DefaultValue` as strings. So a *fresh* entity sends exactly those fields
whose CLR-default wire value differs from `DefaultValue` — and the router rejects any it considers invalid /
out-of-range. Two recurring traps (both cost a failed test run on Tier 1/2):

- **Optional ranged `int`/`long` (ports, counts, periods, power):** a fresh entity has CLR-default `0`. If you
  give it the *real* default (e.g. `DefaultValue = "1500"`), the mapper sees `0 != "1500"` and sends `0`, which
  the router rejects (`value out of range (1..255)`). **Fix: set `DefaultValue = "0"`** so an unset field
  (`0`) equals `DefaultValue` and is omitted on add; keep the real default in the `///` doc only.
  ```csharp
  // dtim-period valid range 1..255; 0 is only a CLR "not set" sentinel.
  // DefaultValue="0" makes the mapper skip it on add (it would otherwise send 0 and be rejected).
  [TikProperty("dtim-period", DefaultValue = "0")] public int DtimPeriod { get; set; }
  ```
  (Exception: if the field is genuinely `IsMandatory`, it is always sent regardless.)
- **Enums:** a fresh entity's enum is the **first (zero) member**. Order every enum so the **router default is
  the first member**, and set `DefaultValue` to that member's wire value. Otherwise the mapper sends the
  zero-member's wire value on add — and if that value is invalid for the router, the add fails
  (`input does not match any value of <field>`). Verify the router's default via `cli_complete` (`set <field>=`
  completion or the wiki "Default:" column).

Both reduce to the same rule: **a freshly-constructed entity must equal its `DefaultValue` on every optional
field, so nothing spurious is sent on add.** When in doubt, set `DefaultValue` to the CLR-default wire form.

- **Server-mandatory field despite a documented default:** some menus reject `/add` unless a field is sent
  *even though the wiki documents a default* (the router does not apply that default server-side on add). If
  add fails with `missing =field=` / a required-field trap, mark the property `IsMandatory = true` so the
  mapper always sends it (the create path sends mandatory fields regardless of `DefaultValue`). Example:
  `/interface/wifi/provisioning action` (default `none`, yet required on add) → `IsMandatory = true`.

## Step 4 — Read-only vs read-write

- Always R/O + mandatory: `.id`.
- Always R/O: `invalid`, `dynamic` (and read-only status fields like `running`, `*-status`, counters
  `rx-byte`/`tx-byte`/`bytes`/`packets`, `last-seen`, `uptime`, `mac-address` when reported, etc.).
- Everything in the wiki **"Read-only properties"** table → `IsReadOnly = true`.
- R/O properties use a **private setter**: `public string Foo { get; private set; }`.
- Mandatory: `.id` and usually `name`. Mark `IsMandatory = true` only for fields always present in the
  result set (the mapper expects them). `comment` is never mandatory.
- `DefaultValue` from the wiki "Default:" column. Add `UnsetOnDefault = true` only when setting the field
  back to its default must issue an `unset` (rare; the wiki/old tool fed these from a manual list).

## Step 5 — `[TikEntity(...)]` attribute parameters

| Situation                                                              | Set |
|-----------------------------------------------------------------------|-----|
| Default list menu                                                      | `[TikEntity("/path")]` |
| Full field set needed (almost always, when using detail)              | `IncludeDetails = true` |
| Ordered list where `move` is meaningful (firewall rules, queues)      | `IsOrdered = true` |
| Single-instance menu, no `.id` (`/system/resource`, `/ip/dns`)        | `IsSingleton = true` |
| Whole menu is read-only (`/log`, monitor outputs)                     | `IsReadOnly = true` |
| Live counter fields only present in CLI `print stats`                 | `IncludeCliStats = true` |
| Action-style command, not a list (ping, monitor, torch)              | `LoadCommand = "", LoadDefaultParameneterFormat = TikCommandParameterFormat.NameValue, IsReadOnly = true, IncludeProplist = false` (see `ToolPing`) |
| Need explicit `.proplist` field list                                  | `IncludeProplist = true` |

> **`IncludeDetails` exception — some singletons/legacy menus reject `=detail=`.** A few paths trap
> `=detail=` with `unknown parameter detail` (seen on the l2tp/sstp/pptp/ovpn **server singletons** and on
> **`/caps-man/manager`**). Their plain `print` already returns the full field set, so just **omit
> `IncludeDetails`** for these. If a `LoadAll`/`LoadSingle` smoke test traps on `detail`, drop `IncludeDetails`
> and add a one-line `//` note on the `[TikEntity]`. Relatedly, some singletons return `!empty` (no `!re`
> row) from `print` on certain ROS versions — `LoadSingle` then throws "no such item"; fall back to `LoadAll`
> (assert non-null) in the test and document why (see `Interface/Vpn/OvpnServer.cs`).

## Step 6 — Documentation, including WinBox-native names

- **Entity `///`**: the wiki "Summary" paragraph (multi-line allowed). If absent, at least put the path.
- **Property `///`**: the wiki description for that field. The legacy generator's bare style was
  `/// field-name: description` — prefer a real sentence from the wiki when you have it.
- **WinBox-native (GUI) names**: when a field's WinBox GUI label differs from the API name, note it in the
  `///` so users who know the GUI can find the property. To discover the mapping:
  - Run the same print over the `WinboxNative` transport with `includeRawTrace: true` and compare the
    `.jg`-mapped names (see the `mikrotik` skill's "WinboxNative RAW TRACE" section), **or**
  - Inspect the shipped catalogs under `../_notes/WinboxMessage/<version>/*.jg` (the `.jg` field catalogs the
    resolver uses — `tik4net/Winbox/WinboxJgCatalog.cs`).
  - Example doc line: `/// keepalive-timeout — WinBox: "Keepalive Timeout"`.
  GUI labels fold to API names by replacing space/underscore with `-` and dropping abbreviation dots
  (`WinboxFieldResolver`), so only call out labels that genuinely differ beyond that folding.

## Step 7 — Execute / connection-extension methods

Add these **in the same file** as the entity (after the class, in the same namespace), following
`ToolPingConnectionExtensions`:

- For **action/tool entities** (ping, monitor, torch, etc.) that aren't a plain CRUD list, add a
  `static class <Entity>ConnectionExtensions` with a discoverable verb method (`Ping`, `Monitor`, …) that
  builds the `NameValue` parameters and calls `LoadList<T>`/`LoadAll<T>`. Optionally add a
  `static Execute(ITikConnection, …)` on the entity that delegates to the extension (see `ToolPing.Execute`).
- For **ordinary CRUD entities**, you usually need **no** extra methods — `LoadAll<T>/Save<T>/Delete<T>`
  already work. Only add a helper when there's a natural typed query (e.g. `LoadByName`, a filtered list)
  or a domain action the menu exposes.
- Decide from the wiki/menu: if the path documents an **action** (parameters in, rows out, nothing
  persisted), it wants an Execute/extension method; if it's a **table**, it doesn't.

```csharp
public static class FooConnectionExtensions
{
    public static IEnumerable<Foo> GetFoo(this ITikConnection connection, string something)
        => connection.LoadList<Foo>(
            connection.CreateParameter("param", something, TikCommandParameterFormat.NameValue));
}
```

## Step 8 — `ToString()` override

Add a `ToString()` when there's an obvious human-readable identity (name, address, host→value). Keep it
short and null-safe. Skip it for entities with no natural single-line summary. Example (`ToolPing`):
`return string.Format("{0} ....... {1}", Host, TikTimeHelper.FromTikTimeToSeconds(Time));`

## Step 9 — Write the file and validate

1. Write the `.cs` into the namespace-matching folder. SDK-style project auto-includes new files — no
   `.csproj` edit needed (confirm the project uses globbed compile items; these do).
2. Build: `dotnet build tik4net.objects/tik4net.objects.csproj`.
3. If a live router is available, smoke-test with the `mikrotik-tests` skill: a `LoadAll<NewEntity>()`
   round-trip (and a create/delete if writable), cleaning up after.
4. Show the user the generated class and note any fields you left as `string` that could be tightened, plus
   anything you couldn't resolve from wiki/router.

## Step 10 — Add basic "List / Add WillNotFail" tests

Every new entity should get a minimal integration test so a router round-trip is exercised. Tests live in
`tik4net.integrationtests/` (MSTest, .NET 4.8, SDK-style project — new `.cs` files are auto-included, no `.csproj`
edit needed). One test class per entity (or per domain), named `<Entity>Test`, inheriting `TestBase`, in
namespace `tik4net.integrationtests`.

**Test file location mirrors the entity folder structure.** Place the test file in the subfolder of
`tik4net.integrationtests/` that matches the entity's domain folder in `tik4net.objects/`:

```
tik4net.objects/Ip/Firewall/FirewallFilter.cs  →  tik4net.integrationtests/Ip/Firewall/IpFirewallTest.cs
tik4net.objects/Interface/Bridge/BridgePort.cs →  tik4net.integrationtests/Interface/Bridge/InterfaceBridgeTest.cs
tik4net.objects/System/SystemScheduler.cs      →  tik4net.integrationtests/System/SystemSchedulerTest.cs
tik4net.objects/Ppp/PppSecret.cs               →  tik4net.integrationtests/Ppp/PppTest.cs
```

One test file per domain subfolder is fine (combine all entities from that folder into one test class rather
than splitting to one file per entity). Core/infra tests (`ConnectionTest`, `CrudTest`, `TestBase`,
`Protocols/`) stay at the root of `tik4net.integrationtests/`.

See `tik4net.integrationtests/Interface/Bridge/InterfaceBridgeTest.cs` for the canonical shape and the `mikrotik-tests`
skill for the full harness.

Two standard tests per entity:

```csharp
using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.<Domain>;

namespace tik4net.integrationtests
{
    [TestClass]
    public class <Entity>Test : TestBase
    {
        // 1) List — LoadAll must not throw and must return a (possibly empty) list.
        [TestMethod]
        public void List<Entity>sWillNotFail()
        {
            EnsureCommandAvailable("/<api/path>");          // skip (Inconclusive) if the package is absent
            var list = Connection.LoadAll<<Entity>>();
            Assert.IsNotNull(list);
        }

        // 2) Add — create, reload by id, assert, then delete (always clean up).
        [TestMethod]
        public void Add<Entity>WillNotFail()
        {
            EnsureCommandAvailable("/<api/path>");
            string marker = Guid.NewGuid().ToString();
            var entity = new <Entity> { /* mandatory fields */ Comment = marker };
            Connection.Save(entity);

            var loaded = Connection.LoadById<<Entity>>(entity.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Comment);

            Connection.Delete(loaded);
        }
    }
}
```

For a **read-only / singleton** entity, write only the List test (no Add/Delete) — e.g. `LoadSingle<T>()`
for singletons, `LoadAll<T>()` otherwise.

### Limiting a test to specific connection types

`TestBase` runs against whatever transport the runsettings/App.config selects (the test suite is run once
per transport). Guard a test that a transport can't satisfy so it is **skipped (Inconclusive), not failed** —
use the helper that matches the reason:

| Guard (call at top of the test) | Skips when |
|---|---|
| `EnsureCommandAvailable("/ip/...")`     | the API path/package isn't on this router |
| `EnsureCapability(TikConnectionCapability.Listen, "…")` | the active transport lacks a capability (Listen/Streaming/Tagging/… ⇒ non-API transports) |
| `EnsureMinRouterOsVersion(7, "…")` / `EnsureMaxRouterOsVersion(7, "…")` | RouterOS major version is out of range |
| `SkipOnNonApi("…")`                     | running over a non-binary-API transport (CLI family + WinBox) |
| `SkipOnWinboxNativeUnmappedPath("/ip/...")` | path isn't in the WinBox `.jg` catalog (native M2 can't reach it) |

Don't hand-roll transport checks; these emit a clear skip reason in Test Explorer. A plain CRUD entity
usually needs only `EnsureCommandAvailable`. Add capability/version guards only when the specific test
relies on that capability or feature.

### Running them

```powershell
dotnet test tik4net.integrationtests/tik4net.integrationtests.csproj --filter "ClassName=tik4net.integrationtests.<Entity>Test"
```

The file is auto-included (SDK-style project); just build and run the filter above (it needs the live test
router). See `mikrotik-tests` for running across all transports.

## Output skeleton

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.<Domain>
{
    /// <summary>
    /// <wiki summary, multi-line ok>
    /// </summary>
    [TikEntity("/<api/path>", IncludeDetails = true /*, IsOrdered/IsSingleton/IsReadOnly as needed */)]
    public class <EntityName>
    {
        /// <summary>.id — primary key of row</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>name</summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        // … writable properties (public get/set), enums where applicable …
        // … read-only properties (public get; private set;) …

        /// <summary>comment</summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => Name;
    }

    // Only when the menu is an action or needs a typed query:
    /// <summary>Connection extension class for <see cref="<EntityName>"/></summary>
    public static class <EntityName>ConnectionExtensions
    {
        // public static IEnumerable<EntityName> ... (this ITikConnection connection, ...) { ... }
    }
}
```

## Reference files

- Conventions: wiki `High-level-API-custom-entities`, `High-level-API-tools`.
- Attributes: `tik4net.objects/TikEntityAttribute.cs`, `TikPropertyAttribute.cs`, `TikEnumAttribute.cs`.
- Legacy generators: `Tools/tik4net.entitygenerator/` (router/value heuristics),
  `Tools/tik4net.entityWikiImporter/` (wiki HTML parsing).
- Good entity exemplars: `Interface/InterfaceVlan.cs` (enums, R/O, defaults),
  `Ip/Firewall/FirewallFilter.cs` (ordered, big enum), `Tool/ToolPing.cs` (action entity + Execute +
  extension + ToString), `Log.cs` (read-only singleton-ish).
- MCP/router access: `mikrotik` skill (`mikrotik_call`) and `mikrotik_cli_complete` (Tab-completion /
  field+menu enumeration, backed by `tik4net/Cli/ITikCliCompletion.cs`); WinBox names: `mikrotik` skill
  RAW-TRACE section + `../_notes/WinboxMessage/*.jg`.
- Tests: `mikrotik-tests` skill; `tik4net.integrationtests/TestBase.cs` (guards), `tik4net.integrationtests/Interface/Bridge/InterfaceBridgeTest.cs` (List/Add shape).
</content>
</invoke>
