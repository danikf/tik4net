// EntityTransportCheckTest.cs — TikEntityTransportCheck over every built-in entity: each transport reads every
// entity as the binary API does, or the finding names the option or the gap. The public form of the repository's own
// TransportPathMapAuditTest, through the O/R mapper rather than raw prints.
//
// Read-only. One transport per row against the binary API; the MAC-layer variants share their IP siblings' code and
// are left out (the lab's /queue/tree alone is ~680 rows). The full report is written beside the TRX
// (entity-transport-check-<transport>.txt); a finding fails the row, and a finding on a built-in entity is 5.0 work.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using tik4net.Objects;
using tik4net.Testing;

namespace tik4net.integrationtests
{
    [TestClass]
    [TestLock(TestLockScope.Router)]   // whole tables read over two connections: nothing may change in between
    [TestCategory(TestCategories.LegIndependent)]
    public class EntityTransportCheckTest : LockedTestBase
    {
        private static readonly MethodInfo CompareMethod = typeof(TikEntityTransportCheck).GetMethod(nameof(TikEntityTransportCheck.Compare));

        [DataTestMethod]
        [DataRow(TikConnectionType.Rest)]
        [DataRow(TikConnectionType.Telnet)]
        [DataRow(TikConnectionType.Ssh)]
        [DataRow(TikConnectionType.WinboxCli)]
        [DataRow(TikConnectionType.WinboxNative)]
        public void EveryBuiltInEntityReadsAsOverTheBinaryApi(TikConnectionType transport)
        {
            var findings = new StringBuilder();
            var clean = new List<string>();
            var notHere = new List<string>();
            int withFindings = 0;

            var noWay = new List<string>();
            using (var api = TestBase.LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api))
            {
                var other = TestBase.LabSetup(transport).Create(transport);
                try
                {
                    foreach (var entity in Entities())
                    {
                        // A read that failed may have closed the connection (a timeout does): the next entity gets a new one,
                        // or every entity after it would fail as "not open".
                        if (!other.IsOpened)
                        {
                            other.Dispose();
                            other = TestBase.LabSetup(transport).Create(transport);
                        }
                        TikEntityTransportReport report;
                        try
                        {
                            report = (TikEntityTransportReport)CompareMethod.MakeGenericMethod(entity)
                                .Invoke(null, new object[] { api, new[] { other } });
                        }
                        catch (TargetInvocationException ex) when (ex.InnerException is TikCommandException)
                        {
                            notHere.Add(entity.Name + " (" + ex.InnerException.Message.Split('\n')[0].Trim() + ")");
                            continue;
                        }

                        // A menu the transport says it has no way to reach (WinBox native's window-less menus) is the
                        // router's answer, not a gap: listed, not failed.
                        if (report.Findings.Count == 1 && report.Findings[0].Exception is TikPathNotMappedException notMapped)
                        {
                            noWay.Add(entity.Name + " (" + notMapped.Message.Split('.')[0] + ")");
                            continue;
                        }
                        if (report.HasFindings)
                        {
                            withFindings++;
                            findings.AppendLine(report.ToString());
                        }
                        else
                        {
                            clean.Add(entity.Name + " (" + report.ReferenceRowCount + ")");
                        }
                    }
                }
                finally
                {
                    other.Dispose();
                }
            }

            string text = "TikEntityTransportCheck, Api vs " + transport + Environment.NewLine
                + withFindings + " entities with findings, " + clean.Count + " clean, " + notHere.Count + " not readable over the API here"
                + Environment.NewLine + Environment.NewLine + findings
                + Environment.NewLine + "Clean: " + string.Join(", ", clean)
                + Environment.NewLine + Environment.NewLine + "Not readable over the API here: " + string.Join("; ", notHere)
                + Environment.NewLine + Environment.NewLine + "No way to reach over " + transport + ": " + string.Join("; ", noWay)
                + Environment.NewLine;
            string path = Path.Combine(TestContext.TestRunResultsDirectory ?? Path.GetTempPath(),
                "entity-transport-check-" + transport.ToString().ToLowerInvariant() + ".txt");
            File.WriteAllText(path, text, new UTF8Encoding(false));
            TestContext.AddResultFile(path);
            Console.WriteLine("report: " + path);
            Console.WriteLine(text);

            Assert.AreEqual(0, withFindings, withFindings + " entities read differently over " + transport + "; see " + path);
        }

        // Every list or singleton entity of tik4net.objects that is read with a plain print and is not obsolete.
        private static IEnumerable<Type> Entities()
            => typeof(TikEntityAttribute).Assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.IsPublic && t.GetConstructor(Type.EmptyTypes) != null)
                .Where(t => t.GetCustomAttribute<ObsoleteAttribute>() == null)
                .Where(t => t.GetCustomAttribute<TikEntityAttribute>() is TikEntityAttribute a && a.LoadCommand == "/print")
                .OrderBy(t => t.FullName, StringComparer.Ordinal);
    }
}
