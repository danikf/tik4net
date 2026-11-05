using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace tik4net.Winbox
{
    /// <summary>
    /// The set of <c>.jg</c> plugins a router serves, as the router's mproxy <c>list</c> catalog spells it (<see cref="Parse"/>)
    /// and as the catalog cache remembers it between runs (<see cref="Format"/>, <see cref="ParseRemembered"/>).
    /// </summary>
    internal static class WinboxPluginList
    {
        // The list catalog is a JS object literal, one { … } per served file. Only entries carrying a
        // `unique` are fetchable plugins; the icon PNGs have none.
        private static readonly Regex ListEntryRegex = new Regex(@"\{([^}]*)\}", RegexOptions.CultureInvariant);
        private static readonly Regex ListNameRegex = new Regex(@"\bname\s*:\s*""([^""]*)""", RegexOptions.CultureInvariant);
        private static readonly Regex ListUniqueRegex = new Regex(@"\bunique\s*:\s*""([^""]*)""", RegexOptions.CultureInvariant);

        // The `unique` value is router-supplied and is used both as an mproxy filename and as a local cache
        // filename, so it is constrained to a plain name here — no separators, no "..", nothing that could
        // walk out of the cache directory.
        private static readonly Regex SafePluginNameRegex =
            new Regex(@"^[A-Za-z0-9][A-Za-z0-9._-]*\.jg$", RegexOptions.CultureInvariant);

        private static bool IsSafePluginName(string name) =>
            !string.IsNullOrEmpty(name) && name.Length <= 128 &&
            !name.Contains("..") && SafePluginNameRegex.IsMatch(name);

        /// <summary>
        /// Parses the mproxy <c>list</c> catalog into the fetchable <c>.jg</c> plugins. Entries without a
        /// <c>unique</c> (the icon PNGs) and entries whose names are not plain filenames are skipped — the
        /// names come from the router and are used as local cache filenames.
        /// </summary>
        internal static List<WinboxJgCatalog.PluginEntry> Parse(string? text)
        {
            var result = new List<WinboxJgCatalog.PluginEntry>();
            foreach (Match m in ListEntryRegex.Matches(text ?? ""))
            {
                string body = m.Groups[1].Value;
                Match name = ListNameRegex.Match(body), unique = ListUniqueRegex.Match(body);
                if (!name.Success || !unique.Success) continue;
                if (!IsSafePluginName(name.Groups[1].Value) || !IsSafePluginName(unique.Groups[1].Value)) continue;
                result.Add(new WinboxJgCatalog.PluginEntry { Name = name.Groups[1].Value, Unique = unique.Groups[1].Value });
            }
            return result;
        }

        /// <summary>Serializes a resolved plugin set as <c>name TAB unique</c> lines.</summary>
        internal static string Format(IEnumerable<WinboxJgCatalog.PluginEntry> plugins) =>
            string.Join("\n", plugins.Select(p => p.Name + "\t" + p.Unique).ToArray());

        /// <summary>
        /// Reads back a set written by <see cref="Format"/>. Names are re-validated on the way in:
        /// the file survives between runs and its contents become mproxy filenames and cache filenames again,
        /// so it is treated as untrusted exactly like the router's own <c>list</c>.
        /// </summary>
        internal static List<WinboxJgCatalog.PluginEntry> ParseRemembered(string? text)
        {
            var result = new List<WinboxJgCatalog.PluginEntry>();
            foreach (string line in (text ?? "").Split('\n'))
            {
                string trimmed = line.Trim();
                int tab = trimmed.IndexOf('\t');
                if (tab <= 0) continue;
                string name = trimmed.Substring(0, tab).Trim(), unique = trimmed.Substring(tab + 1).Trim();
                if (!IsSafePluginName(name) || !IsSafePluginName(unique)) continue;
                result.Add(new WinboxJgCatalog.PluginEntry { Name = name, Unique = unique });
            }
            return result;
        }
    }
}
