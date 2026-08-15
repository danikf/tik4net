# tik4net.objects (O/R mapper)

The high-level, attribute-driven O/R mapper over [`tik4net`](../tik4net/README.md) — strongly typed
entities with full CRUD, change tracking and list merging.

| | |
|---|---|
| Target | `netstandard2.0` |
| Ships as | part of the **`tik4net`** NuGet package (with `tik4net`) |
| Packable on its own | **No** — `IsPackable=false`; [`tik4net.package`](../tik4net.package/README.md) assembles the package |
| Runtime namespace | `tik4net.Objects` — note the capital **O**, which differs from the project folder name |

Until 4.0 this shipped as a separate `tik4net.objects` package. It is now part of `tik4net` itself;
consumers upgrading from 3.x must **remove** any `PackageReference` to `tik4net.objects`.

## What is in here

- Entity classes (169 of them) under domain folders — `Ip/`, `Interface/`, `System/`, `Tool/`, `Ppp/`, …
- `TikEntityAttribute`, `TikPropertyAttribute`, `TikEnumAttribute` — the mapping surface.
- `TikEntityMetadataCache` → `TikEntityMetadata` → `TikEntityPropertyAccessor` — reflection is done
  once and cached; value conversion lives in the accessor.
- `TikConnectionExtensions` — `LoadAll`/`LoadList`/`LoadSingle`/`LoadById`/`Save`/`Delete`/`Move`, the
  async and monitor variants, and the bulk `SaveListDifferences`/`CreateMerge` pair.
- `Tracking/` — `TikChangeTracker` and `TikSnapshot` attach proplist-aware snapshots to loaded entities
  through a `ConditionalWeakTable`, so `Save` sends only the fields that changed.

## Adding an entity

Prefer the **`entity-generator` skill** — it scaffolds from a live router and applies the conventions.
They are documented in [ARCHITECTURE.md](../ARCHITECTURE.md#adding-an-entity).

Two rules that cause failures when missed:

- `Id` is always `[TikProperty(".id", IsReadOnly = true, IsMandatory = true)]`.
- A `bool` property's `DefaultValue` must be the wire form `"no"`/`"yes"`, never `"false"`/`"true"` —
  the mapper emits `yes`/`no` and compares that against `DefaultValue`, so a wrong default never
  matches and the field is sent on every add.

## Careful

`TikChangeTracker` and the `Save` default-versus-unset rules encode deliberate, non-obvious semantics.
A tidy-up there changes observable behaviour.
