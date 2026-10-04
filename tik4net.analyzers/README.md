# tik4net.analyzers

Compile-time checks for code that uses tik4net. They ship **inside** the `tik4net` NuGet package, in
`analyzers/dotnet/cs/`, so a consumer gets them by installing tik4net; nothing at runtime depends on them.

| | |
|---|---|
| Target | `netstandard2.0`, built against Microsoft.CodeAnalysis **3.8** |
| Ships as | part of the **`tik4net`** package (via [`tik4net.package`](../tik4net.package/README.md)) |
| Tests | `TikFieldObjectEqualityAnalyzerTests` in [`tik4net.unittests`](../tik4net.unittests/README.md) |

| Id | Severity | What it reports | Fix |
|---|---|---|---|
| `TIK001` | Warning | A `TikField<T>` or `TikValue<T>` compared with a plain value through `object`: `object.Equals("x", rule.Comment)`, `Assert.AreEqual(object, object)`, a plain value's `Equals(object)`. The two are different types there and never equal | compare `.Value` (code fix) |

The Roslyn version is the compiler's contract, not a preference: an analyzer built against a newer
Roslyn than the consumer's compiler is skipped with warning CS8032, an error under `-warnaserror`. 3.8
is VS 16.8 / the .NET 5 SDK, and the version Unity requires of an analyzer.

`tik4net.integrationtests` runs the analyzers over its own code, as a consumer would get them.

## Adding a rule

Take the next `TIK` number and add it to `AnalyzerReleases.Unshipped.md` (RS2008). At a release the
rows move to `AnalyzerReleases.Shipped.md`, and a shipped id keeps its meaning. Link the rule's
`helpLinkUri` to the wiki section that explains the mistake, and add the rule to the table above.
