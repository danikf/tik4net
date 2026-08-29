// This file is nullable-enabled on its own: the test project as a whole is not (see the note in
// Directory.Build.props), but this code models "the wiki may not be checked out" as a null and should say so.
#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace tik4net.unittests.Docs
{
    /// <summary>One C# code block lifted out of a markdown page.</summary>
    internal sealed class WikiSample
    {
        /// <summary>
        /// The type the page's snippets assume their <c>connection</c> already is.
        /// </summary>
        /// <remarks>
        /// Most pages mean a plain <see cref="ITikConnection"/>, and that default is what keeps the check
        /// sharp: a facet member called on it — <c>SafeModeTake</c>, <c>CallCommandSync</c> — fails, exactly
        /// as it would for the reader.
        /// <para>
        /// A few pages legitimately open a more capable connection <b>once</b>, at the top, and let every
        /// later snippet continue from it. There the default would report a defect the page does not have,
        /// so such a page says so with <c>&lt;!-- sample-connection: ITikApiConnection --&gt;</c>. It is
        /// declared rather than guessed, because guessing it is what would quietly disable the check.
        /// </para>
        /// </remarks>
        public string ConnectionType = "ITikConnection";

        /// <summary>File name of the page the block came from, e.g. <c>Low-level-API.md</c>.</summary>
        public string Page = "";

        /// <summary>1-based line of the opening fence, so a failure can be clicked to.</summary>
        public int Line;

        /// <summary>The block's own text, fences removed.</summary>
        public string Code = "";

        /// <summary>
        /// The reason given on a <c>&lt;!-- no-compile: … --&gt;</c> marker, or <c>null</c> when the block
        /// carries none and is therefore expected to compile.
        /// </summary>
        public string? NoCompileReason;

        public override string ToString() => Page + ":" + Line;
    }

    /// <summary>
    /// Finds the wiki working copy and reads the C# blocks out of it.
    /// </summary>
    /// <remarks>
    /// The wiki is a <b>separate repository</b> (GitHub keeps it that way), cloned next to this one. So this
    /// looks for it rather than assuming a path: <c>TIK4NET_WIKI_DIR</c> wins, otherwise a sibling
    /// <c>tik4net.wiki</c> beside the repository root. Nothing here hard-codes a machine-local path — CI
    /// checks out only this repository and legitimately finds nothing.
    /// </remarks>
    internal static class WikiSampleFinder
    {
        /// <summary>A block the wiki marks as not-compilable, and the reason it gives.</summary>
        /// <remarks>
        /// The reason is mandatory, and <see cref="WikiSampleCompilationTests"/> also checks the marker is
        /// still <i>needed</i>. A marker nobody re-examines is how an excuse outlives the thing it excused.
        /// </remarks>
        private static readonly Regex NoCompileMarker =
            new Regex(@"^\s*<!--\s*no-compile\s*:\s*(?<reason>.+?)\s*-->\s*$", RegexOptions.Compiled);

        private static readonly Regex Fence =
            new Regex(@"^```(?<lang>cs|csharp|c\#)[ \t]*\r?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex FenceEnd = new Regex(@"^```[ \t]*\r?$", RegexOptions.Compiled);

        /// <summary>The page-level declaration of what its snippets assume <c>connection</c> to be.</summary>
        private static readonly Regex ConnectionTypeMarker =
            new Regex(@"^\s*<!--\s*sample-connection\s*:\s*(?<type>[A-Za-z0-9_.]+)\s*-->\s*$",
                RegexOptions.Compiled);

        /// <summary>
        /// The wiki working copy, or <c>null</c> when it is not checked out beside this repository.
        /// </summary>
        public static string? FindWikiDirectory()
        {
            string? fromEnv = Environment.GetEnvironmentVariable("TIK4NET_WIKI_DIR");
            if (!string.IsNullOrWhiteSpace(fromEnv))
                return Directory.Exists(fromEnv) ? Path.GetFullPath(fromEnv) : null;

            string? repo = FindRepositoryRoot();
            if (repo == null) return null;

            string? parent = Path.GetDirectoryName(repo.TrimEnd(Path.DirectorySeparatorChar));
            if (parent == null) return null;

            string sibling = Path.Combine(parent, "tik4net.wiki");
            return Directory.Exists(sibling) ? sibling : null;
        }

        /// <summary>The directory holding <c>tik4net.sln</c>, walking up from the test assembly.</summary>
        public static string? FindRepositoryRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "tik4net.sln"))) return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }

        /// <summary>Every C# block on one markdown file, in document order.</summary>
        public static List<WikiSample> ReadSamples(string markdownPath)
        {
            var result = new List<WikiSample>();
            string[] lines = File.ReadAllLines(markdownPath);
            string page = Path.GetFileName(markdownPath);

            // One declaration per page, anywhere on it, applying to every block on it.
            string connectionType = "ITikConnection";
            foreach (string line in lines)
            {
                Match declared = ConnectionTypeMarker.Match(line);
                if (declared.Success) { connectionType = declared.Groups["type"].Value; break; }
            }

            for (int i = 0; i < lines.Length; i++)
            {
                if (!Fence.IsMatch(lines[i])) continue;

                // The marker sits on the line before the fence, optionally separated by blanks - a blank line
                // between an HTML comment and a fence is normal markdown formatting, not a different block.
                string? reason = null;
                for (int back = i - 1; back >= 0 && back >= i - 3; back--)
                {
                    if (lines[back].Trim().Length == 0) continue;
                    Match m = NoCompileMarker.Match(lines[back]);
                    if (m.Success) reason = m.Groups["reason"].Value;
                    break;
                }

                var body = new List<string>();
                int j = i + 1;
                for (; j < lines.Length && !FenceEnd.IsMatch(lines[j]); j++)
                    body.Add(lines[j]);

                result.Add(new WikiSample
                {
                    Page = page,
                    Line = i + 1,
                    Code = string.Join(Environment.NewLine, body),
                    NoCompileReason = reason,
                    ConnectionType = connectionType,
                });
                i = j;
            }
            return result;
        }
    }
}
