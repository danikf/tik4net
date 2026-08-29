// This file is nullable-enabled on its own: the test project as a whole is not (see the note in
// Directory.Build.props), but this code models "the wiki may not be checked out" as a null and should say so.
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace tik4net.unittests.Docs
{
    /// <summary>
    /// Wraps a wiki code block in just enough C# to be compilable, and says which wrapping worked.
    /// </summary>
    /// <remarks>
    /// A wiki sample is a fragment: it has no <c>using</c> block, no class, and it talks to a
    /// <c>connection</c> nobody declared. That is right for a reader and useless to a compiler, so the
    /// fragment is placed into a harness.
    /// <para>
    /// Which harness depends on what the fragment IS — statements go in a method body, a property or method
    /// declaration goes in a class body, a whole class goes at namespace level — and guessing that from a
    /// regular expression was wrong often enough to be worth abandoning. So all three are <b>parsed</b> and
    /// the first that yields no syntax error is used. The parser is the only thing that actually knows.
    /// </para>
    /// </remarks>
    internal static class WikiSampleHarness
    {
        /// <summary>
        /// Every namespace of the two shipping assemblies, plus the satellites and the framework basics.
        /// </summary>
        /// <remarks>
        /// All of them, unconditionally: a sample says <c>LoadAll&lt;QueueSimple&gt;()</c> without telling
        /// the reader which namespace that is, because on a wiki page the reader's IDE resolves it. Adding a
        /// namespace here therefore does not weaken the check — the members still have to exist.
        /// </remarks>
        private static readonly string[] Usings =
        {
            "System", "System.Collections", "System.Collections.Generic", "System.IO", "System.Linq",
            "System.Net", "System.Text", "System.Threading", "System.Threading.Tasks",
            // The testing pages assert, and they are pages about writing tests - MSTest is the right
            // vocabulary there, not a shortcut around a missing member.
            "Microsoft.VisualStudio.TestTools.UnitTesting",
            "tik4net", "tik4net.Api", "tik4net.Cli", "tik4net.Connection", "tik4net.Diagnostics",
            "tik4net.MacTelnet", "tik4net.Mndp", "tik4net.Rest", "tik4net.Ssh", "tik4net.Telnet",
            "tik4net.Testing",
            "tik4net.Winbox", "tik4net.WinboxCli", "tik4net.WinboxCliMac", "tik4net.WinboxNative",
            "tik4net.WinboxNativeMac",
            "tik4net.Objects", "tik4net.Objects.CapsMan", "tik4net.Objects.Certificate",
            "tik4net.Objects.Interface", "tik4net.Objects.Interface.Bridge",
            "tik4net.Objects.Interface.Ethernet", "tik4net.Objects.Interface.Tunnel",
            "tik4net.Objects.Interface.Vpn", "tik4net.Objects.Interface.Wifi",
            "tik4net.Objects.Interface.Wireless", "tik4net.Objects.Ip", "tik4net.Objects.Ip.DhcpServer",
            "tik4net.Objects.Ip.Dns", "tik4net.Objects.Ip.Firewall", "tik4net.Objects.Ip.Hotspot",
            "tik4net.Objects.Ip.Ipsec", "tik4net.Objects.Ip.Proxy", "tik4net.Objects.Ip.TrafficFlow",
            "tik4net.Objects.Ip.Upnp", "tik4net.Objects.Ppp", "tik4net.Objects.Queue",
            "tik4net.Objects.Radius", "tik4net.Objects.Routing", "tik4net.Objects.Routing.Bgp",
            "tik4net.Objects.Routing.Filter", "tik4net.Objects.Routing.Ospf", "tik4net.Objects.Snmp",
            "tik4net.Objects.System", "tik4net.Objects.Tool", "tik4net.Objects.Tool.Graphing",
            "tik4net.Objects.Tool.MacServer", "tik4net.Objects.Tool.Romon", "tik4net.Objects.Tracking",
            "tik4net.Objects.User", "tik4net.Objects.Wireguard",
        };

        /// <summary>
        /// The identifiers wiki samples use without introducing them.
        /// </summary>
        /// <remarks>
        /// Deliberately short, and it is the one part of this harness that can WEAKEN the check rather than
        /// enable it. A name declared here is given a type by this file, so every sample using that name is
        /// from then on checked against the type chosen here instead of the one the page meant.
        /// <para>
        /// That is not hypothetical: a <c>conn</c> declared here as <see cref="ITikConnection"/> made the
        /// fake-connection samples compile — <c>conn.AssertWasSent(…)</c> bound against an interface that has
        /// no such member, because the ambient field shadowed what the page had actually built. Calling a
        /// member on the wrong receiver type is the exact defect this test exists to catch, so the ambient
        /// name that caused it was removed. The same field then reported a defect that was NOT there, on a
        /// page whose snippets continue from an <c>ITikApiConnection</c> opened once at the top — the mirror
        /// image of the same mistake: this file deciding a type the page had already decided.
        /// </para>
        /// <para>
        /// So <c>conn</c> is gone (a page meaning a concrete connection has to show it), and
        /// <c>connection</c> takes its type from <see cref="WikiSample.ConnectionType"/> — the page's own
        /// <c>sample-connection</c> declaration, defaulting to the least capable <see cref="ITikConnection"/>
        /// so that a facet member called on it still fails.
        /// </para>
        /// <para>
        /// The rule for adding anything else to this list: only a name whose type is the same on every page
        /// that uses it. Anything else belongs in the sample, where the reader needs to see it anyway.
        /// </para>
        /// </remarks>
        private const string Ambient = @"
        static {CONNECTION} connection = null!;
        static TikConnectionSetup setup = null!;
        static string host = """", user = """", pass = """", password = """";
        static int port = 0;
        static System.Threading.CancellationToken ct = default;
        static System.Threading.CancellationToken cancellationToken = default;

        // The wiki's placeholders for 'put your own router here'. They are strings on every page that uses
        // them, which is the bar for being declared here. 'User' also names an entity type - that is fine,
        // because C# resolves a name in TYPE position to the type and only an expression to this field.
        static string HOST = """", USER = """", PASS = """";
        static string Host = """", User = """", Pass = """", Password = """";
        static string id = """";
";

        /// <summary>A <c>using X;</c> line, with or without a trailing comment.</summary>
        private static readonly Regex UsingLine =
            new Regex(@"^\s*using\s+(?:static\s+)?[A-Za-z0-9_.]+\s*;\s*(//.*)?$", RegexOptions.Compiled);

        private static readonly CSharpParseOptions ParseOptions =
            new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Regular);

        /// <summary>Wraps <paramref name="sample"/> and parses it, trying each harness shape in turn.</summary>
        /// <returns>
        /// The first tree with no syntax errors. When no shape parses, the tree of the LAST shape tried is
        /// returned with <paramref name="parsed"/> false — its diagnostics are what gets reported, because a
        /// block that fits none of the shapes is a fragment, and saying so is more useful than three
        /// competing error lists.
        /// </returns>
        public static SyntaxTree Wrap(WikiSample sample, int ordinal, out bool parsed)
        {
            // The block may open with its own usings; they have to reach the top of the file, and a duplicate
            // of one already in the list is dropped rather than emitted twice (CS0105).
            var own = new List<string>();
            var body = new StringBuilder();
            foreach (string line in sample.Code.Split('\n'))
            {
                string trimmed = line.TrimEnd('\r');
                if (UsingLine.IsMatch(trimmed))
                {
                    string directive = trimmed.Trim();
                    int comment = directive.IndexOf("//", StringComparison.Ordinal);
                    if (comment >= 0) directive = directive.Substring(0, comment).Trim();
                    if (!Usings.Contains(directive.TrimEnd(';').Replace("using ", "").Trim()))
                        own.Add(directive);
                }
                else body.AppendLine(trimmed);
            }

            string head = string.Join(Environment.NewLine, Usings.Select(u => "using " + u + ";"))
                        + Environment.NewLine
                        + string.Join(Environment.NewLine, own.Distinct())
                        + Environment.NewLine;
            string ns = "WikiSample" + ordinal.ToString("D4");
            string text = body.ToString();
            string ambient = Ambient.Replace("{CONNECTION}", sample.ConnectionType);

            SyntaxTree? last = null;
            foreach (string shaped in new[]
                     {
                         // statements
                         head + "\nnamespace " + ns + "\n{\n    internal static class Sample\n    {\n"
                              + ambient + "\n        static async Task Run()\n        {\n" + text
                              + "\n        }\n    }\n}\n",
                         // a whole type, or several. Ahead of the member shape on purpose: a class nested
                         // inside the member harness parses perfectly well and then fails to BIND, because
                         // an extension method has to live in a top-level static class (CS1109). Trying the
                         // outer placement first puts a declared type where the page meant it to be.
                         head + "\nnamespace " + ns + "\n{\n" + text + "\n}\n",
                         // member declarations
                         head + "\nnamespace " + ns + "\n{\n    internal static class Sample\n    {\n"
                              + ambient + "\n" + text + "\n    }\n}\n",
                     })
            {
                SyntaxTree tree = CSharpSyntaxTree.ParseText(shaped, ParseOptions, path: sample.ToString());
                last = tree;
                if (!tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
                {
                    parsed = true;
                    return tree;
                }
            }

            parsed = false;
            return last!;
        }
    }
}
