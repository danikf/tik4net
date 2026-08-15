# tik4net.unittests

Router-free tests for the library itself. **This is where a new test belongs unless it genuinely needs
hardware.**

| | |
|---|---|
| Framework | MSTest |
| Target | `net8.0` |
| Router required | No |
| Runs in CI | **Yes**, on every push and pull request, on Windows and Linux |

```bash
dotnet test tik4net.unittests/tik4net.unittests.csproj
```

## What belongs here

Anything testable without a router: the sentence and word codecs, `CliOutputParser`,
`VtStripper`/`Vt100State`, `TikTimeHelper`, `EcSrp5`, `M2Message`, property and enum conversion,
change-tracker diffing, and `TikFakeConnection`-based consumer scenarios.

Internals of `tik4net` are visible here (`InternalsVisibleTo`, declared in
`tik4net/Properties/AssemblyInfo.cs`), so codec-level types can be tested directly without widening the
public API.

The project is SDK-style — new `.cs` files are picked up automatically, no `.csproj` edit needed.

## What does not belong here

Anything that needs a live router goes to [`tik4net.integrationtests`](../tik4net.integrationtests/README.md),
which never runs in CI. Before adding a test there, check whether it could be written here instead — a
test in this project runs on every pull request, and one in the integration suite runs when somebody
remembers to run it.

## Warnings are errors in CI

The solution is warning-clean, with no exclusions, and must stay that way.
