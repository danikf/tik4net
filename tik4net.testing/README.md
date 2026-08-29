# tik4net.testing

Unit-testing support for **consumers** of tik4net: `TikFakeConnection` lets an application's tests run
without a live MikroTik router.

| | |
|---|---|
| Target | `netstandard2.0` |
| Ships as | the **`tik4net.testing`** NuGet package |
| Dependency | `tik4net` (via [`tik4net.package`](../tik4net.package/README.md)) |

```bash
dotnet add package tik4net.testing
```

`TikFakeConnection` implements `ITikConnection`, so anything written against the contract — including
the O/R mapper — works against it. It also implements `ITikRawSentenceConnection` and
`ITikSafeModeConnection`, and declares `TikConnectionCapability.SafeMode` in its default capability
set, so code that drives `CallCommandSync`/`SafeModeTake`/`SafeModeRelease`/`SafeModeUnroll`/`SafeModeGet`
against it works the same as against a real transport. It does not implement `ITikTaggedConnection` —
a fake has no wire to tag, so `SendTagWithSyncCommand` does not exist on it. Register the responses your
code expects and assert on what it sent.

See [Unit testing without a router](https://github.com/danikf/tik4net/wiki/Unit-testing-without-a-router)
in the wiki for usage.

## Not to be confused with

- [`tik4net.unittests`](../tik4net.unittests/README.md) — this repository's **own** router-free tests,
  which run in CI. That is where tests of tik4net itself belong.
- [`tik4net.integrationtests`](../tik4net.integrationtests/README.md) — this repository's own tests
  that require a live router.

This package is the thing shipped to users; those two are internal test projects.
