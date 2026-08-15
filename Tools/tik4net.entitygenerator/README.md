# tik4net.entitygenerator (legacy)

WinForms code generator that scaffolds an O/R mapper entity from a **live router**.

**Legacy and non-shipping.** Superseded by the **`entity-generator` skill**, which does the same job
with better sources (live router over any transport via the MCP server, plus the MikroTik wiki and CLI
reference) and finishes the class instead of emitting a draft for a human to clean up.

Kept in the tree because it is the record of the original heuristics. Read it when you need the exact
rules the skill reproduces:

- `EntityCodeGenaratorMainForm.Generate` — connects, runs `/path/print [detail]`, and takes the **union
  of field names across all returned rows** (different rows expose different optional fields).
- `GeneratorHelper.DetermineFieldType` — infers the C# type from the observed **values**.
- `GeneratorHelper.Camelize` — the MikroTik-name to PascalCase rule (drops `-` and `.`, title-cases).

The companion tool [`tik4net.entityWikiImporter`](../tik4net.entityWikiImporter/README.md) supplied the
other half: documented types, defaults and the read-only split.

Still a legacy non-SDK `.csproj`.
