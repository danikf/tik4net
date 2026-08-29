// This file is nullable-enabled on its own: the test project as a whole is not (see the note in
// Directory.Build.props), but this code models "the wiki may not be checked out" as a null and should say so.
#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tik4net.unittests.Docs
{
    /// <summary>
    /// Compiles every C# block in the wiki and the repository README against the library as it is now.
    /// </summary>
    /// <remarks>
    /// Documentation samples rot silently. Nothing builds them, so a renamed member, a property that never
    /// existed, or a call on the wrong receiver type stays on the page until a reader pastes it and finds out
    /// — and by then the page is the thing they trusted. The wiki cleanup found three of exactly that shape
    /// (a helper's <c>Status</c> property that does not exist, a <c>CompleteAsync</c> that is really
    /// <c>CompleteCli</c>, and a quick-start whose <c>using (</c> was never closed), plus a false XML-doc
    /// claim that had been copied into samples on seven pages. Each was written in good faith and each
    /// compiled only in the author's head.
    /// <para>
    /// So the samples are compiled. Not run — a sample talks to a router — <b>compiled</b>, which is the
    /// half that catches "this member is not there".
    /// </para>
    /// <para>
    /// <b>Where the wiki is.</b> GitHub keeps a wiki in its own repository, so it is not checked out by CI
    /// and this test is Inconclusive there. That is a real limit and worth naming: the check runs for whoever
    /// has the wiki cloned beside the repository (which is how the maintainer works, see AGENTS.md), and
    /// <c>TIK4NET_WIKI_DIR</c> overrides the location. The README half runs everywhere.
    /// </para>
    /// </remarks>
    [TestClass]
    public class WikiSampleCompilationTests
    {
        /// <summary>
        /// Diagnostics that are an artefact of quoting a fragment, not a defect in the sample.
        /// </summary>
        /// <remarks>
        /// <b>What this test checks is narrower than "the sample is a program".</b> It is: <i>every name the
        /// sample resolves against tik4net must exist, on that receiver, with that shape.</i> That is the
        /// defect class documentation actually suffers from — a renamed member, a call on an interface that
        /// does not have it, an invented parameter name, a conversion that is not there.
        /// <para>
        /// Self-containedness is deliberately NOT checked, which is why <c>CS0103</c> is here. A wiki page is
        /// read top to bottom and a block half way down legitimately continues one above it; an undeclared
        /// <c>rule</c> or <c>conn</c> is that, and it says nothing at all about whether the library has the
        /// member being called on it. Forcing every block to stand alone would mean either rewriting samples
        /// for the tool's benefit or declaring the names here — and declaring a name GIVES it a type, which
        /// is how a wrong-receiver bug hides. The narrower check is the honest one.
        /// </para>
        /// <para>
        /// A missing member on a name the sample DID resolve is still caught, because that is CS1061 /
        /// CS0117 / CS1739 / CS0266 / CS0246 — none of which are on this list, and none of which can be
        /// produced by a fragment simply being a fragment.
        /// </para>
        /// </remarks>
        private static readonly HashSet<string> IgnoredDiagnostics = new HashSet<string>(StringComparer.Ordinal)
        {
            // The sample continues an earlier block, or names the reader's own code.
            "CS0103", // name does not exist in the current context
            "CS0128", // a local with this name is already declared (two blocks, both opening a connection)
            "CS0101", // the namespace already contains this type (a 3.x/4.0 pair shown side by side)
            "CS0116", // a namespace cannot directly contain members
            "CS0201", // only assignment/call/new can be used as a statement (an illustrative comparison)
            "CS1023", // embedded statement cannot be a declaration
            "CS8112", // local function must declare a body (a block listing signatures)

            // Untidy, and allowed to be: a sample is not production code.
            "CS0105", // duplicate using - the harness adds every namespace, the sample may repeat one
            "CS0162", // unreachable code
            "CS0168", // declared and never used
            "CS0219", // assigned and never used
            "CS0414", // field assigned, value never used
            "CS1998", // async method without await
            "CS8019", // unnecessary using
            "CS8321", // local function never used
        };

        /// <summary>
        /// The reference set this harness builds exists only on .NET Core.
        /// </summary>
        /// <remarks>
        /// <c>TRUSTED_PLATFORM_ASSEMBLIES</c> is a CoreCLR concept and is null on .NET Framework, so on the
        /// net48 leg the compilation has no references at all and every sample fails with "the namespace
        /// System could not be found" — a harness failure wearing the costume of 289 documentation defects.
        /// <para>
        /// Skipped rather than fixed, because fixing it would raise a real design question rather than
        /// settle one: the netstandard2.0 build genuinely lacks the <c>IAsyncEnumerable</c> surface, so a
        /// sample using <c>await foreach</c> compiles on net8.0 and could not on .NET Framework. Checking
        /// the samples per target framework means the wiki has to say which framework each sample needs —
        /// worth doing, and not something to decide inside a skip.
        /// </para>
        /// </remarks>
        private static void RequireCoreClrReferenceSet()
        {
            if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa && tpa.Length > 0)
                return;

            Assert.Inconclusive(
                "This check needs the CoreCLR reference set (TRUSTED_PLATFORM_ASSEMBLIES), which .NET "
                + "Framework does not provide - the net8.0 leg runs it. NOT checked here, and worth "
                + "knowing: whether every sample also compiles against the netstandard2.0 build, which "
                + "has no IAsyncEnumerable surface.");
        }

        [TestMethod]
        public void EveryWikiSampleCompiles()
        {
            RequireCoreClrReferenceSet();
            string? wiki = WikiSampleFinder.FindWikiDirectory();
            if (wiki == null)
                Assert.Inconclusive(
                    "The wiki is a separate repository and is not checked out beside this one. Clone it as "
                    + "a sibling 'tik4net.wiki' directory, or point TIK4NET_WIKI_DIR at it, to run this check.");

            var samples = new List<WikiSample>();
            foreach (string page in Directory.GetFiles(wiki!, "*.md").OrderBy(p => p, StringComparer.Ordinal))
                samples.AddRange(WikiSampleFinder.ReadSamples(page));

            Assert.IsTrue(samples.Count > 100,
                $"Only {samples.Count} C# blocks found in {wiki} - that does not look like the tik4net wiki.");

            AssertCompiles(samples, "the wiki");
        }

        /// <summary>
        /// The repository README is a documentation page too, and the one most readers see first.
        /// </summary>
        /// <remarks>
        /// Separate from the wiki because it is IN this repository, so unlike the wiki test this one runs in
        /// CI, on every push, with nothing to check out.
        /// </remarks>
        [TestMethod]
        public void EveryReadmeSampleCompiles()
        {
            RequireCoreClrReferenceSet();
            string? repo = WikiSampleFinder.FindRepositoryRoot();
            Assert.IsNotNull(repo, "Could not find the repository root (no tik4net.sln above the test assembly).");

            string readme = Path.Combine(repo!, "README.md");
            Assert.IsTrue(File.Exists(readme), readme + " does not exist.");

            AssertCompiles(WikiSampleFinder.ReadSamples(readme), "the repository README");
        }

        /// <summary>
        /// A <c>no-compile</c> marker has to still be needed.
        /// </summary>
        /// <remarks>
        /// The marker is an excuse, and an excuse nobody re-examines outlives the thing it excused: a block
        /// gets fixed, or the API grows the member it was missing, and the marker keeps the block out of the
        /// check forever. So a marked block that compiles is a failure telling you to delete the marker —
        /// the same ratchet the transport audit uses on its known-gap table.
        /// </remarks>
        [TestMethod]
        public void NoCompileMarkersAreStillNeeded()
        {
            RequireCoreClrReferenceSet();
            string? wiki = WikiSampleFinder.FindWikiDirectory();
            if (wiki == null)
                Assert.Inconclusive("The wiki is not checked out beside this repository - see EveryWikiSampleCompiles.");

            var marked = new List<WikiSample>();
            foreach (string page in Directory.GetFiles(wiki!, "*.md"))
                marked.AddRange(WikiSampleFinder.ReadSamples(page).Where(s => s.NoCompileReason != null));

            string? repoRoot = WikiSampleFinder.FindRepositoryRoot();
            if (repoRoot != null)
                marked.AddRange(WikiSampleFinder.ReadSamples(Path.Combine(repoRoot, "README.md"))
                                                .Where(s => s.NoCompileReason != null));

            var stale = new List<string>();
            foreach (WikiSample sample in marked)
            {
                if (Compile(new[] { sample }, out _)) stale.Add($"{sample}  (marker says: {sample.NoCompileReason})");
            }

            Assert.AreEqual(0, stale.Count,
                "These blocks carry a no-compile marker but compile fine now. Delete the marker so the block "
                + "is checked again:" + Environment.NewLine + string.Join(Environment.NewLine, stale));
        }

        private static void AssertCompiles(IReadOnlyList<WikiSample> samples, string what)
        {
            var checkable = samples.Where(s => s.NoCompileReason == null).ToList();
            Assert.IsTrue(Compile(checkable, out string report),
                $"C# samples in {what} do not compile against the current library."
                + Environment.NewLine + Environment.NewLine + report
                + Environment.NewLine
                + "Fix the sample. If the block is a deliberate fragment (an elided body, a fluent chain, "
                + "code shown BECAUSE it no longer compiles), put an HTML comment on the line above the "
                + "fence: <!-- no-compile: why -->");
        }

        /// <summary>Compiles the samples and renders whatever went wrong.</summary>
        /// <remarks>
        /// Two passes, because the C# compiler does not bind a compilation that fails to parse: a single
        /// unparseable block would suppress the semantic errors in all the others, and the run would report
        /// one fragment while hiding every missing member behind it. So the trees that do not parse are
        /// reported and then set aside, and the rest are bound on their own.
        /// </remarks>
        private static bool Compile(IReadOnlyList<WikiSample> samples, out string report)
        {
            var lines = new List<string>();
            var trees = new List<SyntaxTree>();

            for (int i = 0; i < samples.Count; i++)
            {
                SyntaxTree tree = WikiSampleHarness.Wrap(samples[i], i, out bool parsed);
                if (parsed) { trees.Add(tree); continue; }

                Diagnostic first = tree.GetDiagnostics().First(d => d.Severity == DiagnosticSeverity.Error);
                lines.Add($"{samples[i]}  does not parse in any harness shape - {first.Id}: {first.GetMessage()}");
            }

            var compilation = CSharpCompilation.Create(
                "WikiSamples",
                trees,
                ReferenceAssemblies(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    // The samples are not written nullable-clean and were never meant to be - a warning about
                    // a possible null on a page teaching LoadAll is noise, not a finding.
                    nullableContextOptions: NullableContextOptions.Disable,
                    // Only errors matter here; a sample is allowed to be untidy.
                    generalDiagnosticOption: ReportDiagnostic.Suppress,
                    specificDiagnosticOptions: null));

            foreach (Diagnostic d in compilation.GetDiagnostics())
            {
                if (d.Severity != DiagnosticSeverity.Error || IgnoredDiagnostics.Contains(d.Id)) continue;
                lines.Add($"{d.Location.SourceTree?.FilePath ?? "?"}  {d.Id}: {d.GetMessage()}");
            }

            report = string.Join(Environment.NewLine, lines.Distinct());
            return lines.Count == 0;
        }

        /// <summary>
        /// Everything the test process itself was loaded with.
        /// </summary>
        /// <remarks>
        /// The trusted-platform-assemblies list is the runtime's own reference set, which already contains
        /// tik4net and tik4net.objects (this project references them) alongside the framework. Building the
        /// list by hand from assembly locations misses whichever transport nobody remembered.
        /// </remarks>
        private static IEnumerable<MetadataReference> ReferenceAssemblies()
        {
            string paths = (string)(AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "");
            return paths.Split(Path.PathSeparator)
                        .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
                        .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));
        }
    }
}
