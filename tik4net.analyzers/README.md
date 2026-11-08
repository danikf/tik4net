# tik4net.analyzers

Compile-time checks for code that uses tik4net. They ship **inside** the `tik4net` NuGet package, in
`analyzers/dotnet/cs/`, so a consumer gets them by installing tik4net; nothing at runtime depends on them.

| | |
|---|---|
| Target | `netstandard2.0`, built against Microsoft.CodeAnalysis **3.8** |
| Ships as | part of the **`tik4net`** package (via [`tik4net.package`](../tik4net.package/README.md)) |
| Tests | `TikFieldObjectEqualityAnalyzerTests`, `TikFieldNullCheckAnalyzerTests`, `TikValueListCommaItemAnalyzerTests`, `TikEntityTypeArgumentAnalyzerTests`, `TikEntityTypeArgumentCoverageTests` in [`tik4net.unittests`](../tik4net.unittests/README.md) |

| Id | Severity | What it reports | Fix |
|---|---|---|---|
| `TIK001` | Warning | A `TikField<T>`, `TikNegatableField<T>`, `TikValue<T>` or `TikNegatableValue<T>` — or a `Nullable<>` of one — compared with a plain value or `null` through `object`: `object.Equals("x", rule.Comment)`, `object.Equals(null, rule.Comment)`, `Assert.AreEqual(object, object)`, a plain value's `Equals(object)`. The two are different types there and never equal, and a boxed struct is never null. A `TikValueList<T>` compared with its text the same way | compare `.Value` (code fix; `?.Value` for a `Nullable<>`; none for a list) |
| `TIK002` | Warning | A `TikField<T>`, `TikNegatableField<T>`, `TikValue<T>` or `TikNegatableValue<T>` handed to a null check that takes `object` — `Assert.IsNull(rule.Comment)`, `IsNotNull`, xUnit's `Null`/`NotNull`. The struct is boxed and never null: `IsNull` always fails, `IsNotNull` always passes | check `.Value` (code fix) |
| `TIK003` | Warning | A string constant containing `,` handed to a `TikValueList<T>` as one item — `new TikValueList<string>("a,b")`, `list.With("c,d")`, `TikValueList<T>.Not("a,b")`, an array or collection initializer passed whole, or inside `TikNegatableValue<T>.Not` / `TikValue<T>.FromWire`. The router separates items with `,`, and the constructor refuses such an item with `ArgumentException` at run time | one argument per item (code fix, for a plain literal; a part keeps its `!`, which a `TikNegatableValue<string>` item reads as the negation), or `TikValueList<T>.Parse` |
| `TIK004` | Warning | A mapper generic called with a type that has no `[TikEntity]`: `connection.LoadAll<MyDto>()`, `"abc".EntityToString()`, `42.EnsureStrict()`, `fake.WithEntities(dto)`. It compiles — the constraint is at most `new()` — and throws `ArgumentException` at run time. Checked: the type parameter named `TEntity` of every public generic method in `tik4net.Objects*` and `tik4net.Testing*`, inferred or explicit, extension receivers and method groups included; `[TikEntity]` on a base class counts. Not reported: a type parameter of the caller's own generic code, and a method that takes a `TikEntityMetadata` (the `TikChangeTracker` verbs, which never read the type argument) | pass an entity class, or decorate the type with `[TikEntity]` |

Neither TIK001 nor TIK002 sees NUnit's constraint model: in `Assert.That(rule.Comment, Is.EqualTo("x"))` or
`Assert.That(rule.Comment, Is.Null)` the wrapper goes to a generic `actual` parameter and the constraint is
built apart from it, so no call receives the wrapper as `object`.

The Roslyn version is the compiler's contract, not a preference: an analyzer built against a newer
Roslyn than the consumer's compiler is skipped with warning CS8032, an error under `-warnaserror`. 3.8
is VS 16.8 / the .NET 5 SDK, and the version Unity requires of an analyzer.

`tik4net.integrationtests` runs the analyzers over its own code, as a consumer would get them.

TIK004 keys on the name `TEntity`. `TikEntityTypeArgumentCoverageTests` walks the IL of the shipping assemblies and fails
when a public generic method that reaches `TikEntityMetadataCache` (or reads `[TikEntity]` itself) names its entity type
parameter otherwise or lives outside those namespaces, so a new mapper method is covered or the build says why not.

## Adding a rule

Take the next `TIK` number and add it to `AnalyzerReleases.Unshipped.md` (RS2008). At a stable release
the rows move to `AnalyzerReleases.Shipped.md` (an alpha, beta or RC tag leaves them where they are), and a
shipped id keeps its meaning. Link the rule's
`helpLinkUri` to the wiki section that explains the mistake, and add the rule to the table above.
