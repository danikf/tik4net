---
name: pre-release-check
description: >
  Run the pre-release review of tik4net before tagging a version — the scans that can be automated and the
  judgement reviews that cannot. Use when the user is preparing a release (alpha, beta, RC or stable),
  asks what is left before shipping, asks whether the version is ready, wants the version markers swept,
  wants the NuGet package metadata checked, or wants a pre-tag audit of the public API surface, thread
  safety, timeouts, cancellation, culture independence, or documentation coverage. Also use after a large
  merge when the user wants to know what the change did to the shipping surface. For running the
  integration suite use mikrotik-tests; for cleaning up wiki prose use wiki-cleanup.
---

# Pre-release check

A release is the moment the library stops being ours and starts being somebody's dependency. This skill is
the list of things worth knowing **before** that, split by whether a machine can decide them.

Two rules govern everything below.

**A check that could not have failed has measured nothing.** Before believing a green result, ask what
outcome would have been red. This is not paranoia — it is how a whole class of green runs turn out to be
runs that never executed. See the traps section; each entry there is a real one from this repository.

**Severity is about the user, not about the finding.** A wrong answer the caller cannot detect outranks a
crash, which outranks anything cosmetic. A library that silently returns different data on a Czech machine
is worse than one that throws.

## The two lanes

Keep them apart. Mixing them is how a release checklist becomes a wall nobody reads.

| | **Gates** | **Reviews** |
|---|---|---|
| Decided by | a command, deterministically | judgement |
| Failure means | do not tag | write it down, then decide |
| Where they live | CI where possible, this skill otherwise | this skill, usually delegated to an agent |

Run the gates first — they are cheap and they can invalidate the reviews.

---

# Part 1 — Gates

## 1.1 Build, tests, packages

```bash
dotnet build tik4net.sln --configuration Release -warnaserror
```

```bash
dotnet test tik4net.unittests/tik4net.unittests.csproj --configuration Release
```

Then the integration suite per the `mikrotik-tests` skill: a full pass on the binary API, plus the smoke
subset on the other transports. For a release, run the **full 11-transport matrix** — this is one of the
two occasions that justifies it.

Packaging is validated by CI (`.github/workflows/build.yml` packs all three and asserts both TFMs exist),
but before a tag also unzip the `.nupkg` and check by eye:

* `lib/netstandard2.0/` and `lib/net8.0/` each hold **both** `tik4net.dll` and `tik4net.objects.dll`, and
  the `.xml` doc file beside each.
* `.nuspec` `<dependencies>` names no internal id. `tik4net.core.internal` appearing there means a
  `ProjectReference` lost its `PrivateAssets="all"`, and the package will fail to restore for everyone.
* `LICENSE` and `README.md` are inside.

## 1.2 Version markers

The version lives in exactly one place — `VersionPrefix` in `Directory.Build.props`. Bump it there and
nowhere else; six projects used to declare it and a missed one shipped silently stamped with the previous
release.

The **prose** markers are the ones that rot. Sweep both repositories:

```bash
grep -rn "alpha\|beta\|preview\|experimental\|RC[0-9]" --include=*.md . ../tik4net.wiki | grep -v "/bin/\|/obj/"
```

Every hit is a decision: does this still describe what ships? Two distinctions matter and are easy to get
backwards:

* A **stability marker on the release** (`4.0.0-alpha5`) moves with the version.
* A **stability marker on a feature** (WinboxNative is *experimental*) does not. It changes only when the
  feature does.

## 1.3 Culture independence

RouterOS has one fixed wire format. Nothing the library reads from it or writes to it may depend on the
caller's regional settings — a locale-dependent parse produces a **wrong answer with no error**, which is
the worst failure mode there is.

```bash
dotnet test tik4net.unittests/tik4net.unittests.csproj --filter "CultureIndependence|WireCultureIndependence"
```

Two test classes, deliberately: `Objects.CultureIndependenceTests` covers the O/R mapper's accessor,
`Connection.WireCultureIndependenceTests` covers the wire layer. They exercise three different axes —
negative sign (sv-SE), decimal separator (cs-CZ, de-DE), casing (tr-TR) — because each breaks different
code. **A new parse or format on the wire path gets a case in the matching class, in the same change.**

When adding one, use a value that can actually tell the cultures apart. An integer parses identically
everywhere, so a test using one passes against broken code and proves nothing; it takes a fraction to
separate `10.5 > 9.5` from an ordinal string compare.

The globalization analyzers can find candidates, but **do not turn them on as a gate**:

```bash
dotnet build tik4net/tik4net.csproj -c Release -f net8.0 -p:EnableNETAnalyzers=true -p:AnalysisMode=None -p:AnalysisModeGlobalization=All
```

Measured 2026-08-29 across the four shipping projects: 2008 warnings — CA1305 954, CA1307 642, CA1310 256,
CA1308 128, the rest single digits. CA1305 is dominated by `string.Format` in exception messages, where the
culture is irrelevant, and CA1307 by intent-clarity on ASCII tokens. Mass mechanical edits days before a
tag are a worse risk than the bug they chase. Treat the analyzer as a **periodic review** that produces
candidates for the test matrix, not as a release gate.

## 1.4 Documentation samples compile

```bash
dotnet test tik4net.unittests/tik4net.unittests.csproj --filter WikiSampleCompilationTests
```

Compiles every C# block on the wiki and in `README.md` against the current library. The README half runs in
CI; the wiki half needs the wiki cloned as a sibling (or `TIK4NET_WIKI_DIR`), so **run it locally before a
tag** — CI cannot. `NoCompileMarkersAreStillNeeded` fails when a `<!-- no-compile: why -->` marker has
outlived its reason. Details in `wiki-cleanup`.

## 1.5 Links and anchors

Nothing checks these; a renamed heading silently breaks every link into it.

```bash
cd ../tik4net.wiki && python - <<'PY'
import os,re,glob,collections
def slug(t):
    t=re.sub(r'<[^>]+>','',t).strip().lower()
    return re.sub(r'[^a-z0-9 _\-]','',t).replace(' ','-')
anchors=collections.defaultdict(set)
for p in glob.glob("*.md"):
    fence=False
    for line in open(p,encoding='utf-8',errors='replace'):
        if line.startswith('```'): fence=not fence; continue
        if fence: continue
        m=re.match(r'^#{1,6}\s+(.*?)\s*#*$',line)
        if m: anchors[os.path.splitext(p)[0]].add(slug(m.group(1)))
pages=set(anchors); bad=[]
for p in sorted(glob.glob("*.md")):
    src=os.path.splitext(p)[0]
    for i,line in enumerate(open(p,encoding='utf-8',errors='replace'),1):
        for m in re.finditer(r'\[[^\]]*\]\(([^)\s]+)\)',line):
            t=m.group(1)
            if t.startswith(('http','mailto:')): continue
            page,_,anc=t.partition('#'); page=page.rstrip('/').replace('.md','')
            tgt=page or src
            if page and page not in pages: bad.append(f"{p}:{i} missing page -> {t}")
            elif anc and tgt in anchors and anc not in anchors[tgt]: bad.append(f"{p}:{i} dead anchor -> {t}")
print(f"{len(bad)} broken"); [print("  ",b) for b in bad]
PY
```

**The slug algorithm is the trap.** GitHub lowercases, strips everything outside `[a-z0-9 _-]`, then
replaces **each** space with a hyphen. A naive version that collapses whitespace runs turns `A — B` into
`a-b` instead of `a--b` and reports every em-dash heading as broken. Measured: the collapsing version
claimed 32 broken anchors where 2 were real.

## 1.6 Secrets and machine-local paths

The repository and the wiki are both public.

```bash
grep -rniE "password *= *[\"'][^\"']|[0-9]{1,3}(\.[0-9]{1,3}){3}|([0-9A-F]{2}:){5}[0-9A-F]{2}|C:\\\\Users\\\\" \
  --include=*.md --include=*.cs --include=*.json . ../tik4net.wiki | grep -v "/bin/\|/obj/"
```

Expected, not findings: `tik4net.integrationtests/App.config` holds RouterOS **defaults** (the stock host,
`admin`, empty password) — generic, not a leak. Documentation-only addresses (`10.0.0.1`, `192.168.88.1`)
are fine. Anything else — a real router, a MAC, a software id, a path only one machine has — is a finding.
Docs and skills must **read** router coordinates from `App.config`, never restate them.

---

# Part 2 — Reviews

These need judgement. Each is a good agent task: give it the question, tell it to read only, and require
`file:line` evidence with VERIFIED and SUSPECTED kept apart. Run them **one at a time** — three concurrent
Opus agents exhausted a session limit and returned nothing.

## 2.1 Public API surface — what did this version actually add?

Inventory the public types and members added since the previous release (`git diff` against the last tag).
For each: are the XML docs complete, and does a wiki page cover it? **Public + new + undocumented is the
worst combination in a release** — it is a promise nobody explained.

Then judge coherence, which no diff shows:

* Naming consistency across the three API levels — `Load` vs `Get` vs `Read` vs `Print`, the `Async` suffix.
* Two ways to do one thing where one should win.
* Anything public that only the library has a reason to touch.
* Whether the facets (`ITikRawSentenceConnection`, `ITikSafeModeConnection`, `ITikTaggedConnection`,
  `ITikCliCompletion`, `ITikCancellationModeConnection`, `ITikMacLayerConnection`, `ITikTlsConnection`)
  still form a scheme rather than a pile.

And the question that only gets asked at a release: **does this still match what the library is for?**
Managing RouterOS from .NET, at a low level and through an O/R mapper. Diagnostics, discovery and the
WinBox catalog machinery are means to that end — check that none of them has leaked internals into the
public surface. Be willing to conclude "this is fine"; manufactured criticism wastes the review.

## 2.2 Thread safety

Establish per transport what is **actually** true, not what the docs claim: can one connection be used from
several threads, and what protects it? `ApiConnection` holds a write lock and multiplexes replies by tag;
the terminal transports drive a stateful byte stream where interleaved commands corrupt each other. That
difference is observable to a caller and must be written down.

Also: the process-wide and shared state — the WinBox `.jg` catalog cache, `TikTypeConverters`, the entity
metadata cache, `TikWireTrace`. And whether `Dispose` is safe concurrently with an in-flight command, and
safe twice.

The outcome is not just a report: **the guarantee belongs in the XML docs on the connection types and on
the wiki.** An undocumented threading model is one every user has to discover by being burned.

## 2.3 Timeouts

Inventory every timeout knob: default, unit, which transports honour it, and whether sync and async agree.

The sharp question is not whether a timeout fires but **what it says when it does**. A bare "operation
timed out" throws away the diagnosis; the partial output — what the other side managed to say — is the
whole value. Check it on every transport and name the ones that discard it. Then look for the opposite
failure: a read loop, prompt wait, login handshake or discovery with no bound at all.

## 2.4 Cancellation

Every public async member: does it take a `CancellationToken`, and if so **is the token honoured or merely
accepted?** A token accepted and dropped is worse than none, because it promises something it does not do —
trace each one to the actual socket call.

Then check the flag against reality: a transport declaring `TikConnectionCapability.CancelInFlight` must
genuinely abandon a command safely, and one not declaring it must not appear to. Finally, confirm
`OperationCanceledException` reaches the caller as itself and is not wrapped into a tik4net exception,
which would break every `catch (OperationCanceledException)` written against it.

## 2.5 Nullable annotations on the public surface

Compiling nullable-clean is not the same as being annotated correctly — it can equally mean somebody added
`?` until the warnings stopped. Two directions, and the first is the dangerous one:

* Non-nullable returns that **can** return null on some path. The compiler is telling the caller it is safe.
* `?` where null is unreachable, forcing pointless checks on every caller.

Audit every `null!` and `!` on a public path individually; each is a place where someone told the compiler
to trust them. Mapped `[TikEntity]` reference properties are `string?` **by convention** (see
ARCHITECTURE.md) — not findings, but a mapped reference property **missing** the `?` is one.

## 2.6 NuGet metadata

The package description is the first thing a prospective user reads and the last thing anyone remembers to
update. Read `<Description>`, `<PackageTags>`, `<PackageProjectUrl>` in the three packable projects
(`tik4net.package`, `tik4net.ssh`, `tik4net.testing`) and the `README.md` that ships inside them, and ask:

* Does it describe what the library **is now**, or what it was two releases ago? A version that adds a
  transport family or an API level and leaves the description alone is invisible on nuget.org.
* Are the counts in it still true? Transport count, entity count — these are stated as facts and go stale
  silently. Verify against the code (`Docs/entity-catalog.md`, the `TikConnectionType` enum), do not trust
  the existing number.
* Do the tags match how someone would actually search?
* Is it factual? No marketing adjectives, and no claim not verified against the code.

Also check the packages still describe **the right split**: which assemblies are in `tik4net`, why `ssh`
and `testing` are separate, and that nothing tells a user to reference a package that no longer exists.

## 2.7 Trimming and AOT

The O/R mapper is reflection-driven and net8.0 consumers will try to trim. Decide and **declare** —
`IsTrimmable`, `RequiresUnreferencedCode`, or a documented statement that it is unsupported. Silence means
each user discovers it separately, at publish time.

---

# Part 3 — Traps

Each of these produced a confidently wrong answer in this repository. They are here because they all have
the same shape: **a tool that did not look reports the same green as a tool that looked and found nothing.**

* **`[Ignore]` is applied before `--filter`.** Naming an ignored test on the command line reports it
  skipped and the run passes, having measured nothing.
* **csc does not bind a compilation that fails to parse.** One unparseable file suppresses every semantic
  diagnostic in all the others, and the output reads as "just a few syntax errors, the rest is fine". Parse
  and bind in two passes.
* **A markdown anchor checker is only as good as its slugger** — see 1.5.
* **An audit only checks what it compares.** The path-map audit read field names and not values; teaching it
  values found 26 defects under a green tally. `CultureIndependenceTests` covered one layer and one axis,
  and the wire layer below it was wrong the whole time.
* **A capability flag can hide a whole path.** A wrong gate turned 10 of 11 transports Inconclusive and left
  three defects uncovered. Tie a gate to an actual refusal by the router, never to an assumption.
* **Run a new test against the old code first.** The ones that go red are the defect. Anything green before
  the fix cannot be cited as proof the fix works — say so explicitly when it happens, and keep the test as a
  pin rather than claiming it as evidence.
* **A pre-existing failure is not an outcome.** Either fix it in this change or write up the diagnosis — and
  check for orphaned router state first, because residue from an earlier run looks exactly like a code
  defect.

---

# Output

A release review ends with a written verdict, not a feeling. Per item: **pass / fail / not run**, and for
every failure a one-line diagnosis rather than a symptom.

Then split what is left into two lists and be explicit about which is which:

* **Blocks the tag** — anything that gives a wrong answer silently, leaks a credential, or promises a
  behaviour the code does not have.
* **Ships with it, tracked** — everything else, with the reason it was accepted.

The audit output belongs **outside the repository** (`../_notes/<name>-<date>/`), because it names a live
router. Only the conclusions come back into git.
