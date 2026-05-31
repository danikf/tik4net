// CliParsing.cs — CLI output helpers
// Shared by all terminal-based clients.
// Extracted from WinboxM2CatalogTest.cs and MacLayerTest.cs.

using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace tik4net.tests
{
    internal static class CliParsing
    {
        // Strips ANSI/VT100 escape sequences (including private-mode ESC[?...h/l).
        internal static string StripAnsi(string s)
            => Regex.Replace(s ?? "", @"\x1B(\[[0-9;?]*[A-Za-z]|\][^\x07]*\x07|.)", "");

        // Parses /interface print text into InterfaceEntry list.
        internal static List<InterfaceEntry> ParseInterfaceOutput(string output)
        {
            var result = new List<InterfaceEntry>();
            var pattern = new Regex(@"^\s*(\d+)\s+([RDXS]*)\s+(\S+)\s+(\S+)",
                RegexOptions.Multiline);
            foreach (Match m in pattern.Matches(output))
            {
                string name = m.Groups[3].Value;
                // Skip header lines
                if (name == "NAME" || name == "Flags:") continue;
                result.Add(new InterfaceEntry
                {
                    Index = int.Parse(m.Groups[1].Value),
                    Flags = m.Groups[2].Value.Trim(),
                    Name  = name,
                    Type  = m.Groups[4].Value
                });
            }
            return result;
        }
    }
}
