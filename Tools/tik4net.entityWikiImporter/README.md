# tik4net.entityWikiImporter (legacy)

WinForms tool that parses MikroTik **wiki property tables** into O/R mapper entity scaffolding.

**Legacy and non-shipping.** Superseded by the **`entity-generator` skill**, which reads the same
documentation (and the newer per-menu CLI reference) directly and reconciles it against a live router.

Kept in the tree as the record of the original parsing rules:

- `HtmlParserExtensions.ParsePropertyTable` — reads the wiki's **"Properties"** and **"Read-only
  properties"** tables, which is where the read-only split comes from.
- `HtmlParserExtensions.ParseFieldText` — splits a row's `field-name (type; Default: x)` into name,
  documented type and default.
- `DetermineFieldTypeFromDocumentation` — maps a documented type to a C# type, falling back to `string`
  for read-only properties.

The division of labour still holds and the skill preserves it: **the router is the source of truth for
field names**, the documentation is the source of truth for **types, defaults, the read-only split and
descriptions**.

Depends on `HtmlAgilityPack` (vendored under `Tools/packages/`). Still a legacy non-SDK `.csproj`.
