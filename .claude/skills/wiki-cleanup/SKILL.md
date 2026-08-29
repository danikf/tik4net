---
name: wiki-cleanup
description: >
  Review and clean up the human-facing tik4net GitHub wiki (the clone next to the repo) page by page, so
  that it does not scare off a newcomer and still answers a returning reader's technical question. Use when
  the user asks to review/audit/clean/tidy the wiki or a wiki page, to check wiki examples against the
  code, to check a page for stale or superseded statements, for history narrative that belongs in
  History.md, for Czech text or leaked credentials, for pages that duplicate or wander outside their
  subject, or to decide where a page belongs in the wiki structure. Also use before a release, when the
  wiki has to match what actually shipped. For AI-facing docs in the repository itself (AGENTS.md,
  ARCHITECTURE.md, Docs/, skills, memory) use project-docs-cleanup instead.
---

# Wiki cleanup

The wiki is the **end-user documentation** — humans, not agents. It lives in a separate git repository
cloned next to the main repo (`../tik4net.wiki`, also present in `tik4net.sln` under the `wiki` folder).
For the repository's own agent-facing docs, use the `project-docs-cleanup` skill instead; the two do not
overlap and neither should copy the other.

## What the wiki is for

Two readers, and the wiki has to serve both:

* **A newcomer** who has never used tik4net. They must reach a running program in a couple of minutes,
  and they must not be made to care about M2 handler keys, EC-SRP5 or transport capability flags to get
  there. **The main way we lose this reader is complexity presented too early.**
* **A returning reader** hunting one technical detail — which transport supports `listen`, what `Save`
  sends on an unchanged entity, how a duration is parsed. They need the depth to exist and to be
  findable.

These pull against each other, and the resolution is **ordering, not deletion**. Depth is welcome; depth
above the basics is the defect. A page is not improved by throwing away what the second reader came for —
it is improved by putting it after what the first reader needs.

## The page contract

Every page should read in this order. Missing pieces and out-of-order pieces are both findings.

1. **H1 title**, then one to three sentences: *what this is* and *when you would use it*. A page that
   opens mid-thought ("Second option is to use…") fails here — it only makes sense to someone who arrived
   from one specific link.
2. **Status banner**, only if the subject is alpha/experimental. One line, once per page.
3. **The smallest complete example that works.** Copy-paste-runnable, with the `using` directives.
4. **The body** — what most readers of this page need.
5. **Details, edge cases, per-transport quirks, internals, performance notes** — at the end, under their
   own heading, or moved behind a link.
6. **See also** — the neighbouring pages.

## The repository README is in scope

The GitHub landing page (`README.md` in the code repo) is the wiki's front door — most readers meet it
before they meet `Home`. It gets the same checks and the same premise, with two differences:

* It **owns the per-transport capability matrix** (`## Connection types`). `ARCHITECTURE.md` and the
  `mikrotik-tests` skill link to that anchor, so keep the heading and keep the matrix here; the wiki
  links to it rather than copying it.
* It is a **landing page, not a reference.** The failure mode to watch for is footnote sprawl: a table
  grows a tail of `*` `**` `†` `‡` `§` notes carrying protocol depth, and the page a newcomer lands on
  becomes half specification. Before deleting such a note, check that a wiki page already owns the fact —
  then compress it to one line plus the link. If no page owns it, that is a move, not a delete.

## The premise — what a reader should come away believing

This is a **validation lens, not text to publish.** Never paste it into a page. Use it to ask, of the
wiki as a whole and of every entry page: *would a reader who read only this come away believing it?*
Where the answer is no, the wiki is missing something — that is a finding.

> tik4net is the best C# way to talk to a MikroTik router.
>
> * **You pick the connection, not the router's configuration.** Every way into a RouterOS box is
>   available — binary API, REST, Telnet, SSH, the WinBox channel, and the MAC-layer variants that need
>   no IP address at all — so you work with whatever the router already has enabled instead of
>   reconfiguring it first. Switching between them is one enum value; the rest of your code is unchanged.
> * **You pick the level you work at.** Raw sentences, an ADO.NET-shaped command/parameter API, or a
>   typed O/R mapper with real entity classes — and they interoperate on one connection, so you can drop
>   a level for one call without leaving the model.
> * **You can also speak the transport's own language.** A raw command goes out verbatim — API words on
>   the API, a real CLI line on the CLI transports — which reaches scripting, `/export` and menus no
>   entity covers.
> * **Even at the low level you are not doing the dirty work.** Response parsing, TTY and paging
>   behaviour, quoting, the two API login handshakes, tagging replies to their caller, error semantics —
>   the library handles them. What it genuinely cannot do on a given transport it says so through
>   capabilities, rather than quietly doing the wrong thing.
> * **Comprehensive, and still trivial to start.** The full surface is large; the first working program
>   is a few lines.

Honest limits belong in the same lens, because overselling costs more than it gains — but state each one
once, where it matters, and move on. **The 4.0 features are finished work, not a preview**: they are
tested against a live router on every transport, and the prerelease label is about the API being allowed
to still move, not about whether they work. Documenting them defensively undersells them and is the
mirror-image error of overselling. The two native WinBox transports are the real caveat, and it is a
caveat about *them*, not about 4.0.

Improve the premise as the library changes; it is a claim about today's tik4net, and if a bullet stops
being true the fix is in the code or in the bullet, not in the wiki's tone.

## What the entry pages have to say

`Home`, `Getting-started` and `How-to-use-tik4net-library` carry one more job than the rest: a reader
lands there deciding **whether tik4net is worth learning at all**. Two things have to come across, and
neither survives being buried:

* **Starting is easy.** A handful of lines, one package, a program that runs. The reader should see that
  before they see any transport matrix, capability flag or protocol detail.
* **The coverage is real.** tik4net is not a thin API wrapper: 12 transports behind one connection
  contract (binary API, REST, and the CLI / WinBox / MAC-layer family), a typed O/R mapper over a large
  set of RouterOS menus, change tracking, Safe Mode, streaming reads, unit-testing support without a
  router, MNDP discovery, an MCP server. That breadth is the reason to invest in learning it, and it
  should read as **one coherent library**, not as a list of features that happen to ship together.

Get both across without contradicting the newcomer rule: the *evidence* of breadth belongs near the top,
the *detail* of it does not. A one-line claim plus a link beats a matrix. Say what it covers; let the
per-transport pages say how.

## The checks

Checks 1–8 are per page: work one page at a time, and run every one of them on it before moving on.
Check 9 is a pass over the whole wiki.

### 1. Is it true?

Do not trust the page. Verify against the source in the main repo, which is the ground truth:

```bash
grep -rn "public .*MethodName" tik4net/ tik4net.objects/     # signatures, overloads, nullability
grep -rn "class EntityName" tik4net.objects/                 # entity names, namespaces, property names
grep -rn "MemberName" tik4net/ --include=*.cs                # defaults live in the source, not in prose
```

* **Capability and transport claims** → `README.md`'s connection matrix and `TikConnectionCapability`.
  The README owns that matrix; a wiki page states what a reader needs and links, it does not keep a
  second copy that will drift.
* **Wire-level / protocol claims** → `Docs/` in the main repo.
* **Facts that need a live router** → the `mikrotik` skill, and for raw CLI output the
  `mikrotik-cli-probe` skill. Prefer the source; probe only when the source cannot answer.
* Every claimed **default value** is a claim about today's code. Check it.

### 2. Is it as-is, or is it a story?

Reference pages describe **how it works now**. They do not narrate how it used to be broken.

* Delete: "used to", "previously", "before 4.0 this was…", "we found that…", "this was fixed by…",
  "(was `X`)", "no longer".
* Keep: "since 4.0" and "in 4.0 this changed" **only where a reader on 3.x would otherwise write broken
  code** — that is a fact about the current version, not a story.
* The only two pages that may narrate the past are **`History.md`** and
  **`Upgrading-from-3.x-to-4.0.md`**. If a removed paragraph carries a fact those pages lack, move it
  there rather than dropping it.

```bash
grep -n -iE "(used to|previously|in earlier|was (changed|renamed|fixed)|no longer|we (fixed|found|discovered)|turned out)" *.md
```

### 3. Is anything superseded?

The worst case is a page that documents a workaround for a problem that no longer exists — plausible,
confident and wrong. Watch for a described limitation, then check whether the code still has it. If it
doesn't, the workaround goes and the current behaviour is stated plainly.

### 4. Are the examples right?

* Every type, member and namespace exists, spelled as in the source.
* **Would it compile — is the member on the *receiver's declared type*?** This is the single most common
  defect in this wiki, and it slips past "the member exists" because the member does exist, just not on
  `ITikConnection`. The optional facets (`ITikRawSentenceConnection`, `ITikSafeModeConnection`,
  `ITikTaggedConnection`, …) are deliberately not folded into the main interface, so a sample that
  declares `ITikConnection connection` and then calls `connection.CallCommandSync(…)` or
  `connection.SafeModeTake()` does not build. There are no compatibility extension methods that make it
  build — do not believe a source comment that says there are; check for the method.

  ```bash
  grep -nE '\b(connection|conn)\.(CallCommandSync|SafeModeTake|SafeModeRelease|SafeModeUnroll|SafeModeGet)' *.md
  ```

  Then confirm the variable's declaring line names a facet type, not `ITikConnection`.
* **The entry point is `TikConnectionSetup`.** `ConnectionFactory` is a compatibility shim; a sample that
  opens with it while the page's own next paragraph calls it a shim contradicts itself. Show the setup
  form unless the page is specifically about the shim.
* The `using` directives are present and sufficient.
* Nullability matches — the shipping projects are `<Nullable>enable</Nullable>`, entity reference
  properties are `string?`, and a sample that assigns a `T?` return into a `T` local teaches a warning.
* The example does what the surrounding prose says it does.
* Placeholders are generic (`192.168.88.1`, `admin`, `HOST`/`USER`/`PASS`) — never a real lab address,
  MAC or password.

### 5. Does the page stay on its subject?

A page that wanders is one of two things, and they are handled differently:

* **Duplication** — the material is already owned by another page. Cut it, link to the owner. Pick as
  owner the page a maintainer would update when the code changes.
* **A gap** — the material belongs to a page that does not exist. Do **not** silently grow this page into
  that page. Note it as a finding for the maintainer.

### 6. Is it at the right altitude?

Ask what fraction of this page's readers need each section. Anything a minority needs belongs at the
bottom or behind a link. Internals, reverse-engineered protocol detail and diagnostic war stories are the
usual offenders near the top.

### 7. Language and secrets

English only — no Czech anywhere. No credentials, real router addresses or MACs, and no machine-local
absolute paths: the wiki is public. Terminology and casing are consistent: **tik4net** (never "tik4Net"),
**MikroTik** (never "mikrotik" in prose), **RouterOS**.

"No real lab address" is unactionable on its own — you have to know which address. Read the coordinates
from `tik4net.integrationtests/App.config` (never restate them anywhere) and grep the wiki for those exact
values, plus anything that travels with them: the router MAC, its link-local IPv6, the identity, and the
**software ID**. A pasted terminal transcript or MCP tool output is the usual carrier — the leak arrives
as evidence in an example, not as a credential someone typed.

### 8. Is it in the right place?

* Is the page reachable? `Home.md` is the only navigation this wiki has — there is no `_Sidebar.md`.
* Do inbound links point at it under the name a reader would look for?
* Does its name match its siblings (`*-connection` for transports, `High-level-API-*` for the O/R mapper)?

**Renaming a page breaks every external link and every search result pointing at it.** Propose a rename;
do not perform one without the user's agreement, and when agreed, update every inbound link in the same
commit.

### 9. Is the library actually documented? (a whole-wiki pass, not a per-page one)

The other eight checks can all pass on every page while the wiki still fails, because they only ever ask
about text that exists. This one asks what is **missing**. Run it once — before the page-by-page work, so
the findings are known, and again at the end.

Take the library's main surface and ask, for each item, *where does a reader learn this exists?*

```bash
cd ../tik4net                # the code repo
grep -rn "public interface ITik" tik4net/ | sed 's/.*public interface //'   # the contracts
grep -rn "public static .* this ITikConnection" tik4net.objects/            # the extension surface
grep -rn "^\s*[A-Za-z]* = 0x\|public enum TikConnectionCapability" -A40 tik4net/TikConnectionCapability.cs
ls tik4net.objects/*/                                                       # entity families
```

Three kinds of gap, in descending order of harm:

* **A central idea nobody states.** Not a missing method, but a missing *concept* — that one contract
  covers every transport, that capabilities are fail-closed and why, that the three levels share a
  connection, that raw means the transport's own language. A reader who never meets the idea will not go
  looking for the page that explains it.
* **A main type or interface with no page and no mention.** `ITikConnection` and its optional interfaces,
  `TikConnectionSetup`, the exception tree, `TikFakeConnection`, the entity attributes.
* **A feature documented only in passing** — a bullet on `Home` and nothing behind it.

Judge by prominence, not by counting: the wiki is not an API reference and does not need one page per
type. It needs every idea a reader must hold, every type they will type, and an honest map of the entity
families. Record what is missing as a finding; writing new pages is separate work.

## Working rules

* **One page per commit**, in the wiki repo's existing style: `Subject: what changed`, e.g.
  `Custom entities: when a slash is not a rate pair`. Commit in the wiki clone, not the main repo.
* **Never push.** Leave the commits local unless the user asks for a push.
* **A cleanup should not grow the wiki.** If a page comes out meaningfully longer, that is a signal to
  re-read what was added: cleanup moves and cuts, it does not accumulate. Filling a genuine gap is the
  exception, and worth saying out loud.
* **Do not churn a page that is already right** just to unify its voice. Prose style differences are not
  findings; wrong, stale, misplaced and scary are.
* **Report, don't invent.** A missing page, a missing example, an API that has no documentation at all —
  these are findings for the maintainer, not work to improvise in a cleanup pass.
* If a check keeps producing the same kind of finding, or a page needs a rule this skill does not have,
  say so — the skill is meant to grow.

## Maturity markers vs. capability caveats

Two things look alike on the page and behave completely differently over time. Keeping them apart is
what makes a release sweep safe.

* A **maturity marker** says how settled the *release* is — `🆕 4.0.0-alpha`, "prerelease", "the API may
  still change". It is temporary and gets swept the day the next release ships. Keep it to **one line
  per page**, in one wording, so the sweep is one `grep` and one `sed`. Do not let it seep into body
  prose as hedging ("this may not be reliable yet", "for now", "eventually"): that is the same claim
  restated where no sweep will ever find it, and it quietly tells the reader the feature is not ready.
* A **capability caveat** says what the *thing itself* does — the native WinBox transports are
  experimental, REST buffers the whole response, a transport has no terminal. These are properties of
  the subject and survive every release. They are not swept.

**A maturity marker never belongs inside a page's H1.** A title carrying `⚠️ Alpha` cannot be swept by
the grep+sed the whole scheme is built on without rewriting the title, and it makes the page's name read
as its status. Put the marker on its own line under the title.

Version strings (`4.0.0-alpha`, release-tag URLs, the alpha badge) are the sweepable set. Spell them
identically everywhere rather than paraphrasing, and prefer one marker plus a link to
`Connection-types-and-capabilities` over repeating the status in every table row.

If the wiki is being reviewed **ahead of a release**, the maturity markers still describe what is
published today — do not pre-announce the next one — but the cleanup should leave them in a state where
flipping them is mechanical, and should make sure nothing in the body prose treats shipped features as
tentative.

## Suggested order

Beginner path first (a fix there pays the most), then reference, then deep material:

1. `Home`, `Getting-started`, `How-to-use-tik4net-library`, `CRUD-examples-for-all-APIs`,
   `VB-trivial-example`
2. The three API levels: `Low-level-API`, `ADO.NET-like-API`, `High-level-API-with-O-R-mapper` and the
   `High-level-API-*` family, `TikListMerge`, `Change-tracking`, `Exception-handling`
3. Transports: `Connection-types-and-capabilities`, then the per-transport pages, `SSL-connection`,
   `login-versions`, `MNDP`, `Safe-Mode`
4. Big reference: `Command-translation-on-non-API-transports`,
   `One-task-on-every-transport-and-API-level`, `WinBox-Native-connection`, `MCP-server`
5. Testing: `Communication-debugging-&-testing`, `Testing-*-API`
6. Meta: `History`, `Upgrading-from-3.x-to-4.0`, `Roadmap-4x` — these three follow different rules
   (they *are* the history and the plan) and mostly need accuracy checks only.

## Inventory commands

```bash
cd ../tik4net.wiki
ls -la *.md                                  # page list and size
for f in *.md; do printf "%-45s %s\n" "$f" "$(head -1 "$f")"; done   # a page not starting with "# " has no title
grep -c "4.0.0-alpha" *.md | grep -v ":0"    # status banners — keep to one line per page so the
                                             # release-day removal is one grep
```

Link graph — broken links, orphans, and pages `Home.md` does not reach:

```bash
python - <<'EOF'
import re, os, glob
pages = {os.path.splitext(f)[0] for f in glob.glob("*.md")}
links = {}
for f in sorted(glob.glob("*.md")):
    t = open(f, encoding='utf-8').read()
    links[os.path.splitext(f)[0]] = {
        u.split('#')[0] for u in re.findall(r'\]\(([^)\s]+)\)', t)
        if not u.startswith(('http', '#', 'mailto')) and u.split('#')[0]}
linked = set().union(*links.values())
print("broken:", [(p, r) for p, refs in links.items() for r in refs if r not in pages])
print("orphans:", sorted(p for p in pages if p not in linked and p != 'Home'))
print("not on Home:", sorted(p for p in pages if p not in links['Home'] and p != 'Home'))
EOF
```

## State as of 2026-08-29

Verify rather than assume — this is a snapshot, and the point of the cleanup is to change it.

* 41 pages, ~630 KB. No broken internal links. A full pass over all 41 ran on 2026-08-29; the two
  generations of pages have been levelled (every page now has an H1 and a what/when opening), the
  maturity markers are consolidated, and the maintainer's lab coordinates — which had reached ten pages,
  including a full router dump with MAC, link-local IPv6 and software ID — are scrubbed.
* **Open, and the top item:** `One-task-on-every-transport-and-API-level` — nine of the eleven Level-1
  (low-level) programs do not work. All eleven declare `ITikConnection` and call `CallCommandSync`; the
  five CLI tabs send API sentence words where that level takes a RouterOS CLI line; and `Rest`,
  `RestSsl`, `WinboxNative`, `WinboxNativeMac` do not declare `RawCommand`, so the level does not exist
  there at all. The page carries an accurate warning at the head of the Level-1 section; rewriting the
  CLI programs needs live-router verification. Levels 2 and 3 (22 tabs) are correct.
* **Open, from check 9** — the wiki still never gives a reader: a map of the entity families (169
  entities in 15 folders, and the wiki names one), the entity helper surface (`ExecuteWol`,
  `ToolPing.Execute`, `SystemPackage.Enable`, the `Log*` helpers, …), or a home for the typed value types
  (`TikDuration`, `TikDataRate`, `TikRatePair`, `MacAddress`, documented only where you go to *write* an
  entity). The entity map is the strongest available evidence for the premise's "the coverage is real".
* Four pages are large enough that a newcomer landing on them needs a way out:
  `One-task-on-every-transport-and-API-level` (~170 KB), `WinBox-Native-connection` (~48 KB),
  `Command-translation-on-non-API-transports` (~38 KB), `Connection-types-and-capabilities` (~25 KB).
* The published packages are `4.0.0-alpha5`, and the wiki is being cleaned **ahead of the beta**. The
  alpha markers still describe what is published, so they stay — but the 4.0 transports are finished,
  live-tested work, and the wiki should read that way. Consolidate the markers so the beta flip is
  mechanical (see *Maturity markers vs. capability caveats*), and treat hedging in body prose as a
  finding.
