// EntityEmptyWordProbe.cs — probe: which typed fields does the router print with an EMPTY value, and which duration or
// rate fields with a word TikSpecialWords does not know?
//
// WHY THIS EXISTS. An empty word on a typed field reads Absent, except on a presence flag, where being printed is the
// value (/routing/table fib=). A bool field the router prints empty and the entity does not declare as a presence flag
// would read Absent where the router said "set", so this names every such field. A duration or rate word the table does
// not know reads Unparsed.
//
// WHAT IT DOES NOT PROVE. Only rows the lab routers hold, over the transport of the leg. It reads through the mapper,
// so a CLI print's flags and details are asked for as an application's read would ask. It prints no values except the
// words of duration and rate fields, and nothing but field names for /queue/tree (real customer data on the lab router).
//
// Output: TestResults/v5/emptywords-<router>-<transport>.txt under the solution.
//
// [Ignore] keeps it out of the matrix — run via --filter EntityEmptyWordProbe with the attribute removed.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using tik4net.Objects;

namespace tik4net.integrationtests
{
    [Ignore("Empty-word audit — reads every entity menu of a live router. Remove the attribute to run.")]
    [TestClass]
    [TestCategory(TestCategories.AnyRouter)]
    public class EntityEmptyWordProbe : TestBase
    {
        [TestMethod]
        public void AuditEmptyWords()
        {
            string router = Environment.GetEnvironmentVariable("TIK4NET_ROUTER") ?? "default";
            string outPath = Path.Combine(SolutionDir(), "TestResults", "v5", $"emptywords-{router}-{ResolveConnectionType()}.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));

            var findings = new StringBuilder();
            var errors = new StringBuilder();
            int menus = 0, rowsRead = 0;

            var entities = typeof(TikEntityAttribute).Assembly.GetTypes()
                .Select(t => new { Type = t, Attr = t.GetCustomAttribute<TikEntityAttribute>() })
                .Where(x => x.Attr != null && !x.Type.IsAbstract && x.Attr.LoadCommand != "" && x.Type.GetConstructor(Type.EmptyTypes) != null)
                .Where(x => !x.Attr.EntityPath.EndsWith("/monitor", StringComparison.Ordinal) && x.Attr.EntityPath != "/ping"
                            && x.Attr.EntityPath != "/tool/romon/ping" && x.Attr.EntityPath != "/tool/torch"
                            && x.Attr.EntityPath != "/tool/traceroute" && x.Attr.EntityPath != "/interface/monitor-traffic")
                .OrderBy(x => x.Attr.EntityPath, StringComparer.Ordinal)
                .ToList();

            var loadAll = typeof(EntityEmptyWordProbe).GetMethod(nameof(LoadRows), BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var entity in entities)
            {
                IList rows;
                try
                {
                    rows = (IList)loadAll.MakeGenericMethod(entity.Type).Invoke(this, null);
                }
                catch (TargetInvocationException ex)
                {
                    errors.AppendLine($"{entity.Attr.EntityPath} ({entity.Type.Name}): {ex.InnerException?.GetType().Name}: {FirstLine(ex.InnerException?.Message)}");
                    continue;
                }
                menus++;
                rowsRead += rows.Count;
                bool isPrivate = entity.Attr.EntityPath == "/queue/tree";
                foreach (PropertyInfo p in entity.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    var attribute = p.GetCustomAttribute<TikPropertyAttribute>();
                    if (attribute == null || !p.PropertyType.IsGenericType)
                        continue;
                    Type inner = p.PropertyType.GetGenericArguments()[0];
                    Type valueType = Nullable.GetUnderlyingType(inner) ?? inner;
                    if (valueType == typeof(string) || (valueType.IsGenericType && valueType.GetGenericTypeDefinition() == typeof(TikValueList<>)))
                        continue;

                    int printedEmpty = 0, printed = 0;
                    var unknownWords = new System.Collections.Generic.SortedSet<string>(StringComparer.Ordinal);
                    foreach (object row in rows)
                    {
                        object field = p.GetValue(row);
                        var state = (TikFieldState)p.PropertyType.GetProperty("State").GetValue(field);
                        if (state == TikFieldState.Absent)
                            continue;
                        printed++;
                        if (state == TikFieldState.Unparsed)
                        {
                            string raw = (string)p.PropertyType.GetProperty("RawValue").GetValue(field);
                            if (raw.Length == 0)
                                printedEmpty++;
                            else if (valueType == typeof(TikDuration) || valueType == typeof(TikDataRate) || valueType == typeof(TikRatePair))
                                unknownWords.Add(raw);
                            continue;
                        }
                        object value = p.PropertyType.GetProperty("Value").GetValue(field);
                        if (value == null)
                            printedEmpty++;
                        else if (value is TikDuration duration && duration.Kind == TikValueKind.Unknown)
                            unknownWords.Add(duration.Token);
                        else if (value is TikDataRate rate && rate.Kind == TikValueKind.Unknown)
                            unknownWords.Add(rate.Token);
                        else if (value is TikRatePair pair)
                        {
                            if (pair.Upload.Kind == TikValueKind.Unknown) unknownWords.Add(pair.Upload.Token);
                            if (pair.Download.Kind == TikValueKind.Unknown) unknownWords.Add(pair.Download.Token);
                        }
                    }

                    if (printedEmpty > 0)
                        findings.AppendLine($"EMPTY   {entity.Attr.EntityPath} {attribute.FieldName} : {valueType.Name}"
                            + $"{(attribute.IsPresenceFlag ? " [presence flag]" : "")} — empty on {printedEmpty} of {printed} printed, {rows.Count} rows");
                    if (unknownWords.Count > 0)
                        findings.AppendLine($"UNKNOWN {entity.Attr.EntityPath} {attribute.FieldName} : {valueType.Name} — "
                            + (isPrivate ? $"{unknownWords.Count} word(s)" : string.Join(" | ", unknownWords)));
                }
            }

            var report = new StringBuilder();
            report.AppendLine($"# empty-word audit — router={router}, {ResolveConnectionType()}, RouterOS {GetMikrotikVersion()} — menus read {menus}, rows {rowsRead}");
            report.Append(findings);
            report.AppendLine();
            report.AppendLine("## Menus that could not be read");
            report.Append(errors);
            File.WriteAllText(outPath, report.ToString());
            Console.WriteLine(report.ToString());
        }

        private IList LoadRows<TEntity>() where TEntity : new()
            => Connection.LoadAll<TEntity>().ToList();

        private static string FirstLine(string text)
        {
            if (text == null)
                return "";
            int nl = text.IndexOfAny(new[] { '\r', '\n' });
            return nl < 0 ? text : text.Substring(0, nl);
        }

        private static string SolutionDir()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "tik4net.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? Directory.GetCurrentDirectory();
        }
    }
}
