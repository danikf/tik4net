using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using tik4net.Objects;

namespace tik4net.integrationtests
{
    /// <summary>
    /// A WinBox native <c>print</c> returns the API's fields only (WinBox review D6): for every <c>[TikEntity]</c> path, a
    /// plain <c>print</c> over this leg's native transport against the same <c>print</c> over the binary API, failing on
    /// any field name native returns and the API does not.
    /// </summary>
    /// <remarks>
    /// <para>One handler routinely serves several WinBox windows (<c>/ip/settings</c>, connection tracking and neighbour
    /// discovery share one; NTP client and server share another), and a record of that handler carries every window's
    /// keys. A native row is decoded by the window the path resolves to, so another window's fields stay out of it — the
    /// identity window's <c>version</c> line made <c>ExecuteScalar()</c> on <c>/system/identity/print</c> answer the
    /// RouterOS version.</para>
    /// <para>A name the API does print, but only under <c>detail</c> or <c>stats</c> (native reads ask for the window's
    /// live counters the way WinBox does), is listed in <see cref="AllowedExtras"/> with the reason. Paths that error on
    /// either transport (a package the router lacks) are reported and skipped. Large or private tables are not read.</para>
    /// <para>Native-only: the other transports' field sets are their own reviews' parity tests.</para>
    /// </remarks>
    [TestClass]
    [SafeInParallelLegs]
    public class WinboxNativeFieldScopeSweepTest : TestBase
    {
        /// <summary>Verb entities: a load runs a command (or streams) rather than reading a menu.</summary>
        private static readonly HashSet<string> VerbPaths = new HashSet<string>(StringComparer.Ordinal)
        {
            "/ping", "/tool/romon/ping", "/tool/torch", "/tool/traceroute", "/interface/monitor-traffic",
        };

        /// <summary>Tables too large to read twice per leg, or holding data the test must not print.</summary>
        private static readonly HashSet<string> SkippedPaths = new HashSet<string>(StringComparer.Ordinal)
        {
            "/queue/tree",                // the lab's real customer data (~680 rows)
            "/file",                      // print from= pulls every file's contents
            "/log",
            "/ip/firewall/connection",    // changes between the two reads by nature
            "/ip/dns/cache", "/ip/dns/cache/all",
            "/ip/route", "/ipv6/route", "/routing/route",
        };

        /// <summary>
        /// Path → (name → reason): fields native returns that the API's plain <c>print</c> does not, but its
        /// <c>print detail</c> / <c>print stats</c> does. Empty: on 7.25 neither flag adds any of the names native
        /// returns (singletons refuse <c>detail</c>; <c>/interface print stats</c> is the plain print). A failure line
        /// marks each name with <c>[D]</c> / <c>[S]</c> where the API's detail / stats print has it, which is what would
        /// put it here.
        /// </summary>
        private static readonly Dictionary<string, Dictionary<string, string>> AllowedExtras =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal)
        {
        };

        /// <summary>
        /// Path → (name → reason): native-only names that stay, each with the measurement that keeps it (WinBox review
        /// F11). Tolerated so that the sweep fails on a NEW leak; an entry no longer seen is reported, not failed, since
        /// some depend on the rows the lab holds.
        /// </summary>
        private static readonly Dictionary<string, Dictionary<string, string>> KnownOpen =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal)
        {
            ["/interface/vrrp"] = Open("the API prints it only once sync-connection-tracking=yes (a t4n VRRP interface, 7.25), "
                + "and the window declares no condition on it ('Conn. Tracking Port', u16, def 8275) to follow",
                "connection-tracking-port"),
            ["/ip/cloud"] = Open("licence-dependent: on a CHR free licence ('Cloud services not supported') the API's get "
                + "of each BTH VPN field and use-local-address traps and dns-name is empty and left out; what a licensed "
                + "router prints is not measurable on the lab",
                "back-to-home-vpn", "dns-name", "use-local-address", "vpn-dns-name", "vpn-interface", "vpn-peer-private-key",
                "vpn-peer-public-key", "vpn-port", "vpn-prefer-relay-code", "vpn-private-key", "vpn-public-key",
                "vpn-relay-ipv4-status", "vpn-relay-ipv6-status", "vpn-status"),
            ["/ip/service"] = Open("renamed between versions over one key and one label (address on 6.49.21 and 7.21.5, "
                + "available-from on 7.25): native reports both spellings (AlsoKnownAs), as it does not read the router's version",
                "address"),
            ["/tool/e-mail"] = Open("renamed between versions over one key and one label (address on 6.49.21, server on "
                + "7.21.5 and 7.25): native reports both spellings (AlsoKnownAs), as it does not read the router's version",
                "address"),
        };

        private static Dictionary<string, string> Open(string reason, params string[] names)
            => names.ToDictionary(n => n, n => "F11: " + reason, StringComparer.Ordinal);

        private static bool IsNative(TikConnectionType type)
            => type == TikConnectionType.WinboxNative || type == TikConnectionType.WinboxNativeMac;

        [TestMethod]
        public void NativePrint_ReturnsNoFieldTheApisPrintDoesNot()
        {
            var type = ResolveConnectionType();
            if (!IsNative(type))
                Assert.Inconclusive($"{type}: the field-scope sweep is the WinBox native transports' parity test (D6).");

            var paths = typeof(TikEntityAttribute).Assembly.GetTypes()
                .Select(t => t.GetCustomAttribute<TikEntityAttribute>())
                .Where(a => a != null && a.LoadCommand != "")
                .Select(a => a.EntityPath)
                .Where(p => !VerbPaths.Contains(p) && !SkippedPaths.Contains(p) && !p.EndsWith("/monitor", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            var failures = new StringBuilder();
            int compared = 0, skipped = 0, allowed = 0, open = 0;
            using (var api = LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api))
            {
                foreach (string path in paths)
                {
                    var apiNames = FieldNames(api, path, out string apiError);
                    var ownNames = apiNames == null ? null : FieldNames(Connection, path, out apiError);
                    if (apiNames == null || ownNames == null)
                    {
                        skipped++;
                        Console.WriteLine($"{path}: skipped ({apiError})");
                        continue;
                    }
                    compared++;
                    AllowedExtras.TryGetValue(path, out var allowList);
                    var extra = ownNames.Except(apiNames, StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToList();
                    KnownOpen.TryGetValue(path, out var openList);
                    var unexplained = extra.Where(n => (allowList == null || !allowList.ContainsKey(n))
                                                       && (openList == null || !openList.ContainsKey(n))).ToList();
                    allowed += extra.Count(n => allowList != null && allowList.ContainsKey(n));
                    open += extra.Count(n => openList != null && openList.ContainsKey(n));
                    if (openList != null && openList.Keys.Any(n => !extra.Contains(n)))
                        Console.WriteLine($"{path}: (known-open entries not seen) {string.Join(",", openList.Keys.Where(n => !extra.Contains(n)))}");
                    if (unexplained.Count > 0)
                    {
                        var detail = PrintNames(api, path, "detail");
                        var stats = PrintNames(api, path, "stats");
                        string Where(string n) => (detail.Contains(n) ? "D" : "") + (stats.Contains(n) ? "S" : "");
                        string line = $"{path}: native-only {unexplained.Count}: {string.Join(",", unexplained.Select(n => n + "[" + Where(n) + "]"))}";
                        Console.WriteLine(line);
                        failures.AppendLine(line);
                    }
                    var missing = apiNames.Except(ownNames, StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToList();
                    if (missing.Count > 0)
                        Console.WriteLine($"{path}: (reported, not asserted) API-only {missing.Count}: {string.Join(",", missing)}");
                }
            }

            Console.WriteLine($"{type}: {compared} paths compared, {skipped} skipped, {allowed} allow-listed and {open} known-open (F11) names seen.");
            Assert.IsTrue(compared > 50, $"only {compared} paths could be read on both transports");
            Assert.AreEqual("", failures.ToString(), "native prints fields the API's print does not");
        }

        private static HashSet<string> PrintNames(ITikConnection api, string path, string flag)
        {
            try
            {
                var rows = api.CreateCommandAndParameters(path + "/print", TikCommandParameterFormat.NameValue, flag, "").ExecuteList();
                return new HashSet<string>(rows.SelectMany(r => r.Words.Keys), StringComparer.Ordinal);
            }
            catch (Exception ex) when (!(ex is UnitTestAssertException)) { return new HashSet<string>(); }
        }

        // The union of the field names over every row of a plain print, or null (with the reason) when the read fails.
        private static HashSet<string> FieldNames(ITikConnection connection, string path, out string error)
        {
            error = null;
            try
            {
                var rows = connection.CreateCommand(path + "/print").ExecuteList();
                return new HashSet<string>(rows.SelectMany(r => r.Words.Keys).Where(k => k != TikSpecialProperties.Tag),
                    StringComparer.Ordinal);
            }
            catch (Exception ex) when (!(ex is UnitTestAssertException))
            {
                error = connection.GetType().Name + ": " + ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }
    }
}
