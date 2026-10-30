using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using tik4net.Diagnostics;
using tik4net.Objects;

namespace tik4net.integrationtests
{
    /// <summary>
    /// Measurement for WinBox review F10: does RouterOS fill a number-declared M2 key with text (or a text-declared key
    /// with a number) on any row? Reads every <c>[TikEntity]</c> path once over this leg's native transport with the
    /// wire trace on, and lists each <c>wbx.codec</c> wire-kind note next to what the binary API prints for that field.
    /// </summary>
    /// <remarks>
    /// Read-only. <c>[Ignore]</c> keeps it out of the matrix — run via <c>--filter WinboxWireKindContradictionProbe</c>
    /// on a native leg with the attribute removed.
    /// </remarks>
    [TestClass]
    public class WinboxWireKindContradictionProbe : TestBase
    {
        private static readonly HashSet<string> Skipped = new HashSet<string>(StringComparer.Ordinal)
        {
            "/ping", "/tool/romon/ping", "/tool/torch", "/tool/traceroute", "/interface/monitor-traffic",
            "/queue/tree", "/file", "/log", "/ip/firewall/connection", "/ip/dns/cache", "/ip/dns/cache/all",
        };

        private sealed class NoteSink : ITikWireTraceSink
        {
            internal readonly List<string> Notes = new List<string>();

            public void Emit(string channel, TikWireDir dir, byte[] data, int offset, int count, string note)
            {
                if (channel == "wbx.codec" && note != null && note.Contains(" is declared "))
                    lock (Notes) Notes.Add(note);
            }
        }

        [TestMethod]
        [Ignore("measurement (F10): run on a native leg with the attribute removed")]
        public void ListWireKindContradictions()
        {
            var type = ResolveConnectionType();
            if (type != TikConnectionType.WinboxNative && type != TikConnectionType.WinboxNativeMac)
                Assert.Inconclusive("native legs only");

            var paths = typeof(TikEntityAttribute).Assembly.GetTypes()
                .Select(t => t.GetCustomAttribute<TikEntityAttribute>())
                .Where(a => a != null && a.LoadCommand != "")
                .Select(a => a.EntityPath)
                .Where(p => !Skipped.Contains(p) && !p.EndsWith("/monitor", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToList();

            int read = 0, failed = 0, withNotes = 0;
            using (var api = LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api))
            {
                foreach (string path in paths)
                {
                    var sink = new NoteSink();
                    try
                    {
                        using (TikWireTrace.Capture(sink))
                            Connection.CreateCommand(path + "/print").ExecuteList();
                        read++;
                    }
                    catch (Exception ex) when (!(ex is UnitTestAssertException))
                    {
                        failed++;
                        Console.WriteLine($"{path}: read failed: {ex.GetType().Name}: {ex.Message}");
                    }
                    if (sink.Notes.Count == 0) continue;
                    withNotes++;
                    foreach (var note in sink.Notes.Distinct())
                    {
                        int q1 = note.IndexOf('\''), q2 = q1 < 0 ? -1 : note.IndexOf('\'', q1 + 1);
                        string field = q2 > q1 ? note.Substring(q1 + 1, q2 - q1 - 1) : "?";
                        string apiValues;
                        try
                        {
                            apiValues = string.Join(" | ", api.CreateCommand(path + "/print").ExecuteList()
                                .Select(r => r.Words.TryGetValue(field, out var v) ? "\"" + v + "\"" : "(absent)")
                                .Distinct().Take(8));
                        }
                        catch (Exception ex) when (!(ex is UnitTestAssertException)) { apiValues = "API: " + ex.Message; }
                        Console.WriteLine($"{path}: {note} ×{sink.Notes.Count(n => n == note)}; API prints {apiValues}");
                    }
                }
            }
            Console.WriteLine($"{type}: {read} paths read, {failed} failed, {withNotes} with a wire-kind note.");
        }
    }
}
