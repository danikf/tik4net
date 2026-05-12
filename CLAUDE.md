# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

tik4net is a .NET library for communicating with MikroTik routers via the MikroTik API protocol. It consists of two NuGet packages:
- **tik4net** — low-level ADO.NET-style API (targets `netstandard2.0`, `netstandard2.1`)
- **tik4net.objects** — high-level O/R mapper over tik4net (same targets)

## Build Commands

Build the solution in Visual Studio 2022 or via CLI:

```
dotnet build tik4net.sln
dotnet build tik4net/tik4net.csproj
dotnet build tik4net.objects/tik4net.objects.csproj
```

Pack NuGet packages (output goes to `./Build/`):
```
dotnet pack tik4net/tik4net.csproj
dotnet pack tik4net.objects/tik4net.objects.csproj
```

## Tests

Tests are in `tik4net.tests/` using **MSTest** targeting .NET 4.8. They require a live MikroTik router — connection settings are in `tik4net.tests/App.config`:

```xml
<add key="host" value="192.168.4.236"/>
<add key="user" value="admin"/>
<add key="pass" value=""/>
```

Run tests via Visual Studio Test Explorer (MSTest project format, not SDK-style). There is no mock-based test suite — all tests hit a real router.

## Architecture

### Two-layer design

**Layer 1 — `tik4net` (low-level)**

- `ITikConnection` / `ApiConnection` — TCP socket connection to the router. Supports plain API (port 8728) and SSL (port 8729). Manages the MikroTik sentence protocol (length-prefixed words), login (both legacy and v6.43+ challenge-response), and thread-safe read/write with locks.
- `ITikCommand` — ADO.NET-style command. Execute methods: `ExecuteNonQuery`, `ExecuteScalar`, `ExecuteSingleRow`, `ExecuteList`, `ExecuteAsync` (callback-based). Parameters use `ITikCommandParameter` with `Filter` (?name=value) or `NameValue` (=name=value) format.
- `ConnectionFactory` — entry point. Use `ConnectionFactory.OpenConnection(TikConnectionType, host, user, pass)`.
- Response sentences: `ITikReSentence` (!re), `ITikDoneSentence` (!done), `ITikTrapSentence` (!trap), `ITikFatalSentence` (!fatal).

**Layer 2 — `tik4net.objects` (high-level O/R mapper)**

Entity classes are decorated with attributes that drive all serialization:
- `[TikEntity("/ip/firewall/filter")]` — declares the API path, load command, and entity behaviour flags (`IsSingleton`, `IsOrdered`, `IsReadOnly`, etc.).
- `[TikProperty("src-address")]` — maps a C# property to a MikroTik field name.

CRUD via extension methods on `ITikConnection` (in `TikConnectionExtensions`):
- Load: `LoadAll<T>()`, `LoadList<T>()`, `LoadById<T>()`, `LoadSingle<T>()`
- Save (insert or update): `Save<T>()`
- Delete: `Delete<T>()`
- Bulk sync: `SaveListDifferences<T>()` — diffs two lists and applies minimal changes
- Ordered list: `Move<T>()`, `MoveToEnd<T>()`

Metadata is cached at first use in `TikEntityMetadataCache`.

### Adding a new entity

1. Create a class in `tik4net.objects/` under the relevant domain folder (e.g., `Ip/`, `Interface/`).
2. Decorate with `[TikEntity("/<api/path>")]`.
3. Add properties decorated with `[TikProperty("<field-name>")]`.
4. The entity is immediately usable with `LoadAll<T>()`, `Save<T>()`, etc.

Use the `tik4net.entitygenerator` tool (`Tools/tik4net.entitygenerator/`) to auto-generate entity code from a live router's metadata.

### Key conventions

- `Id` property (mapped to `.id`) is always `[TikProperty(".id", IsReadOnly = true, IsMandatory = true)]`.
- Enum-typed properties should match MikroTik string values via `[TikProperty(...)]` with the enum member name lowercased matching the wire value.
- Boolean MikroTik fields ("yes"/"false") are automatically converted.
- `TikEntityObjectsExtensions` provides `Clone<T>()`, `EntityDescription()`, and `EntityDifference()` helpers.
