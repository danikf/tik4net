# tik4net.package

Packaging-only project. It compiles nothing: it collects the [`tik4net`](../tik4net/README.md) and
[`tik4net.objects`](../tik4net.objects/README.md) assemblies (plus their XML docs) into
`lib/netstandard2.0/` of the single **`tik4net`** NuGet package.

```bash
dotnet pack tik4net.package/tik4net.package.csproj    # -> ./Build/
```

## Why this project exists

`tik4net.objects` references `tik4net`, so `tik4net` cannot reference it back in order to pack it. This
project sits above both and packs them together. Consequently both source projects are
`IsPackable=false`.

The satellites — [`tik4net.ssh`](../tik4net.ssh/README.md) and
[`tik4net.testing`](../tik4net.testing/README.md) — reference their compile-time projects with
`PrivateAssets="all"` and additionally reference **this** project, which is what puts the real
`tik4net` dependency into their `.nuspec`. Without that split they would declare a dependency on a
package ID that does not exist on nuget.org.

## After changing any packaging project

Verify by unzipping the produced `.nupkg` and checking `lib/` and the `.nuspec` dependencies. A wrong
`ProjectReference` silently produces a package that depends on a nonexistent ID — the build stays
green and the failure only appears when a consumer restores it. CI has a job that validates
`dotnet pack`, but the dependency-ID check is worth doing by hand.
