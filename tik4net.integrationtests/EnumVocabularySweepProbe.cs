// EnumVocabularySweepProbe.cs — probe: what values does the router accept for every enum-typed
// [TikProperty] in the mapper, and which of them can the entity not read?
//
// WHY THIS EXISTS. One value the mapper does not know does not cost a property: TikEnumMetadata.Parse
// throws FormatException and the mapper fails the read of the WHOLE menu. That is how mark-routing made
// every mangle table with a policy-routing rule unreadable since v1.2.0, and how mstp did the same to
// /interface/bridge — both reported by users, because nothing here read rows that used the missing value.
// Reading existing rows can never find these: the value has to be IN USE on the lab router.
//
// WHAT IT ASKS INSTEAD. Tab completion enumerates what a menu ACCEPTS. `<menu> add <field>=` (or `set`
// for a singleton) lists the vocabulary whether or not any row uses it.
//
// THREE THINGS THE LISTING WILL NOT TELL YOU, each of which has already produced a wrong answer:
//   * Where every value shares a prefix, RouterOS completes it inline and lists nothing: CompleteCli is
//     empty and CompleteCliRaw is the completed line. The probe takes the prefix from that line and
//     re-asks with it; a row that still yields one value proves nothing and is reported as such.
//   * Accepted is not sent. /interface/pppoe-client accepts add-default-route=yes|no and reads back
//     true|false on Api and Telnet alike, so its completion list is input spelling, not a read defect.
//   * A menu with no rows cannot answer a `set 0 …` probe (there is no row 0), and a read-only menu has
//     neither add nor set. Both come out as NO-COMPLETION, which is "unmeasured", not "clean".
//
// The measured lists live in tik4net.unittests EntityEnumVocabularyTests, which is where a regression
// fails. Re-run this on a new RouterOS, paste the emitted table, and read the DEFECT lines.
//
// It lives in namespace tik4net.integrationtests, not ...\.Objects: a nested Objects namespace here
// would shadow tik4net.Objects for every other file in this project.
//
// [Ignore] keeps it out of the matrix — run via --filter EnumVocabularySweepProbe.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Reflection;
using System.Text;
using tik4net;
using tik4net.Cli;
using tik4net.Objects;

namespace tik4net.integrationtests
{
    [Ignore("Vocabulary sweep — one Tab completion per enum-typed property against a live router. Remove the attribute to run.")]
    [TestClass]
    public class EnumVocabularySweepProbe
    {
        /// <summary>Tokens the completer prints that are syntax rather than values.</summary>
        private static readonly HashSet<string> Artifacts = new HashSet<string> { "!", ":" };

        /// <summary>
        /// Fields whose completion list is not evidence about reading, with the measurement behind each.
        /// An entry here is a field this probe does NOT cover.
        /// </summary>
        private static readonly Dictionary<string, string> NotEvidence = new Dictionary<string, string>
        {
            ["/interface/pppoe-client add-default-route"] = "accepts yes|no, reads back true|false on Api and Telnet alike",
            ["/interface/pppoe-client dial-on-demand"] = "accepts yes|no, reads back true|false on Api and Telnet alike",
            ["/interface/pppoe-client use-peer-dns"] = "accepts yes|no, reads back true|false on Api and Telnet alike",
            ["/interface/ethernet flow-control-tx"] = "not a RouterOS 7 field ('unknown parameter'); it is tx-flow-control, auto|on|off",
            ["/interface/ethernet flow-control-rx"] = "not a RouterOS 7 field ('unknown parameter'); it is rx-flow-control, auto|on|off",
        };

        [TestMethod]
        public void SweepEveryEnumTypedPropertyAgainstTheRouter()
        {
            string host = ConfigurationManager.AppSettings["host"];
            string user = ConfigurationManager.AppSettings["user"];
            string pass = ConfigurationManager.AppSettings["pass"] ?? "";

            var table = new StringBuilder();
            var defects = new List<string>();
            var unmeasured = new List<string>();

            using (var connection = new TikConnectionSetup(TikRouterAddress.Parse(host), user, pass)
                       .Create(TikConnectionType.Telnet))
            {
                var completion = (ITikCliCompletion)connection;

                foreach (var property in EnumProperties())
                {
                    string key = property.Menu + " " + property.Field;
                    if (NotEvidence.TryGetValue(key, out string why))
                    {
                        unmeasured.Add($"{key} ({property.EnumType.Name}): not covered — {why}");
                        continue;
                    }

                    var values = Complete(completion, property.Menu, property.Field, "", out string via);

                    // One value can be a genuine single-value enum, or the prefix every value shares, which
                    // the router completed inline instead of listing (Complete returns the completed value).
                    // Re-ask with it as the prefix; a retry whose values do not start with it walked into the
                    // NEXT parameter and is discarded.
                    if (values.Count == 1)
                    {
                        var retry = Complete(completion, property.Menu, property.Field, values[0], out via);
                        if (retry.Count > 1 && retry.All(v => v.StartsWith(values[0], StringComparison.Ordinal)))
                            values = retry;
                    }

                    // A long list is elided as 'stem-...'. Ask for the stem and splice the answer in.
                    for (int i = values.Count - 1; i >= 0; i--)
                    {
                        if (!values[i].EndsWith("...", StringComparison.Ordinal))
                            continue;
                        string stem = values[i].Substring(0, values[i].Length - 3);
                        var expanded = Complete(completion, property.Menu, property.Field, stem, out _);
                        values.RemoveAt(i);
                        values.InsertRange(i, expanded.Where(v => v.StartsWith(stem, StringComparison.Ordinal)));
                    }

                    values = values.Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToList();

                    if (values.Count == 0)
                    {
                        unmeasured.Add($"{key} ({property.EnumType.Name}): no completion");
                        continue;
                    }

                    table.AppendLine($"            new Vocabulary(\"{property.Menu}\", \"{property.Field}\", "
                                     + $"typeof({property.EnumType.FullName.Replace("+", ".")}),");
                    table.AppendLine("                " + string.Join(", ", values.Select(v => $"\"{v}\"")) + "),");

                    var known = new HashSet<string>(
                        property.EnumType.GetFields(BindingFlags.Public | BindingFlags.Static)
                            .Select(f => f.GetCustomAttribute<TikEnumAttribute>()?.Value)
                            .Where(v => !string.IsNullOrEmpty(v)),
                        StringComparer.OrdinalIgnoreCase);

                    var missing = values.Where(v => !known.Contains(v)).ToList();
                    if (missing.Count > 0)
                        defects.Add($"{property.Menu} {property.Field} ({property.EnumType.Name}) via {via}: "
                                    + $"MISSING {string.Join(", ", missing)}   router: {string.Join(", ", values)}");
                }
            }

            Console.WriteLine("=== DEFECTS (router offers a value the entity cannot read) ===");
            defects.ForEach(Console.WriteLine);
            Console.WriteLine();
            Console.WriteLine("=== UNMEASURED (completion could not answer — NOT the same as clean) ===");
            unmeasured.ForEach(Console.WriteLine);
            Console.WriteLine();
            Console.WriteLine("=== TABLE for EntityEnumVocabularyTests ===");
            Console.WriteLine(table.ToString());

            Assert.AreEqual(0, defects.Count, "See the DEFECTS block above.");
        }

        /// <param name="prefix">
        /// What was typed after the '=' (empty on the first ask). A token of the form
        /// <c>field=&lt;prefix&gt;&lt;value&gt;</c> only appears if the echo was not recognised, and then the prefix
        /// has to be SKIPPED, not re-prepended: doing that is how an earlier version of this probe invented
        /// values like <c>l2tpvl2tpv2</c> and then reported them as defects.
        /// </param>
        private static List<string> Complete(ITikCliCompletion completion, string menu, string field,
                                             string prefix, out string via)
        {
            string assignment = field + "=" + prefix;
            IReadOnlyList<string> tokens = Array.Empty<string>();
            via = null;
            foreach (string verb in new[] { "add", "set", "set 0" })
            {
                via = verb;
                string line = $"{menu} {verb} {assignment}";
                tokens = completion.CompleteCli(line);
                if (tokens.Count > 0)
                    break;

                // No listing: RouterOS may have completed the value inline instead — a unique value, or the
                // prefix every value shares. CompleteCliRaw returns that completed line; the value is what now
                // follows "field=", and the caller re-asks with it as the prefix.
                string completed = completion.CompleteCliRaw(line);
                string head = $"{menu} {verb} {field}=";
                if (completed.StartsWith(head, StringComparison.Ordinal) && completed.Length > head.Length)
                    return new List<string> { completed.Substring(head.Length) };
            }

            // RouterOS 7.24 prints the listing glued to the echo of the typed line; CliCompletionParser drops
            // exactly that echo, so the values arrive as plain tokens. The filters below are defensive: a
            // menu/verb word or a field=<value> token only appears if the echo was not recognised. A bare "0" is
            // NOT filtered, although the "set 0" line types one: it is a value (l2tpv3-cookie-length=0).
            var values = new List<string>();
            foreach (string token in tokens)
            {
                if (token == menu || token == "add" || token == "set" || Artifacts.Contains(token))
                    continue;

                int eq = token.IndexOf('=');
                if (eq >= 0)
                {
                    if (token.Substring(0, eq) == field && eq + 1 + prefix.Length < token.Length)
                        values.Add(token.Substring(eq + 1 + prefix.Length));
                    continue;
                }

                values.Add(token);
            }

            return values.Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToList();
        }

        private sealed class EnumProperty
        {
            public EnumProperty(string menu, string field, Type enumType)
            {
                Menu = menu; Field = field; EnumType = enumType;
            }

            public string Menu { get; }
            public string Field { get; }
            public Type EnumType { get; }
        }

        private static IEnumerable<EnumProperty> EnumProperties()
        {
            return typeof(TikEntityAttribute).Assembly.GetTypes()
                .Where(t => t.GetCustomAttribute<TikEntityAttribute>() != null)
                .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(p => new { Property = p, Attribute = p.GetCustomAttribute<TikPropertyAttribute>() })
                    .Where(x => x.Attribute != null)
                    .Select(x => new
                    {
                        x.Attribute,
                        Type = Nullable.GetUnderlyingType(x.Property.PropertyType) ?? x.Property.PropertyType
                    })
                    .Where(x => x.Type.IsEnum)
                    .Select(x => new EnumProperty(
                        t.GetCustomAttribute<TikEntityAttribute>().EntityPath, x.Attribute.FieldName, x.Type)))
                .OrderBy(x => x.Menu, StringComparer.Ordinal)
                .ThenBy(x => x.Field, StringComparer.Ordinal);
        }
    }
}
