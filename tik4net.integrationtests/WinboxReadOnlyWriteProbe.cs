using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using tik4net.Objects;
using tik4net.Winbox;
using tik4net.WinboxNative;

namespace tik4net.integrationtests
{
    /// <summary>
    /// Measurements for WinBox review F14: which arguments of the API's <c>set</c>/<c>add</c> meet the read-only mark of
    /// the window native resolves them in, and which writable entity properties do; and whether RouterOS honours such a
    /// field written over M2.
    /// </summary>
    /// <remarks>
    /// <c>[Ignore]</c> keeps both out of the matrix — run via <c>--filter WinboxReadOnlyWriteProbe</c> on a native leg with
    /// the attribute removed. The first is read-only; the second writes throwaway <c>t4n</c> rows and removes them.
    /// </remarks>
    [TestClass]
    [TestCategory(TestCategories.AnyRouter)]
    public class WinboxReadOnlyWriteProbe : TestBase
    {
        private static void EnsureNative(TikConnectionType type)
        {
            if (type != TikConnectionType.WinboxNative && type != TikConnectionType.WinboxNativeMac)
                Assert.Inconclusive("native legs only");
        }

        [TestMethod]
        [Ignore("measurement (F14): run on a native leg with the attribute removed")]
        public void ListWritesThatMeetTheReadOnlyMark()
        {
            var type = ResolveConnectionType();
            EnsureNative(type);
            var host = (IWinboxNativeHost)Connection;
            Connection.CreateCommand("/system/identity/print").ExecuteList();

            var entities = typeof(TikEntityAttribute).Assembly.GetTypes()
                .Select(t => new { Type = t, Attribute = t.GetCustomAttribute<TikEntityAttribute>() })
                .Where(e => e.Attribute != null && e.Attribute.LoadCommand != "")
                .GroupBy(e => e.Attribute.EntityPath, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .ToList();

            int checkedPaths = 0, skipped = 0, apiNames = 0, apiHits = 0, entityHits = 0;
            using (var api = LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api))
            using (var grammar = GetMikrotikVersion().Major < 7 ? LabSetup(TikConnectionType.Telnet).Create(TikConnectionType.Telnet) : null)
            {
                foreach (var entity in entities)
                {
                    string path = entity.Key;
                    WinboxFieldResolver resolver;
                    IReadOnlyCollection<string> setArgs = null, addArgs = null;
                    try
                    {
                        resolver = host.ResolveHandlerAndFields(path).Item2;
                        checkedPaths++;
                    }
                    catch (Exception ex) when (!(ex is UnitTestAssertException))
                    {
                        skipped++;
                        Console.WriteLine($"{path}: skipped ({ex.GetType().Name})");
                        continue;
                    }
                    // RouterOS 6 has no /console/inspect: its grammar comes from Tab completion on a terminal.
                    try
                    {
                        var theirs = (grammar ?? api).DescribeMenu(path);
                        setArgs = theirs.SetArguments;
                        addArgs = theirs.AddArguments;
                    }
                    catch (Exception ex) when (!(ex is UnitTestAssertException))
                    {
                        Console.WriteLine($"{path}: no grammar ({ex.GetType().Name})");
                    }

                    var onApi = new HashSet<string>(setArgs ?? new string[0], StringComparer.Ordinal);
                    onApi.UnionWith(addArgs ?? new string[0]);
                    apiNames += onApi.Count;
                    var onEntity = new HashSet<string>(entity
                        .SelectMany(e => e.Type.GetProperties())
                        .Where(p => p.CanWrite)
                        .Select(p => p.GetCustomAttribute<TikPropertyAttribute>())
                        .Where(a => a != null && !a.IsReadOnly)
                        .Select(a => a.FieldName), StringComparer.Ordinal);

                    foreach (string name in onApi.Union(onEntity, StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal))
                    {
                        if (!resolver.IsReadOnlyForWrite(name)) continue;
                        bool api1 = onApi.Contains(name), entity1 = onEntity.Contains(name);
                        if (api1) apiHits++;
                        if (entity1) entityHits++;
                        Console.WriteLine($"{path} {name}: the API takes it={api1}, writable entity property={entity1}");
                    }
                }
            }
            Console.WriteLine($"{type}: {checkedPaths} paths, {skipped} skipped; {apiNames} API write arguments, "
                + $"{apiHits} meet the mark; {entityHits} writable entity properties meet it.");
        }

        [TestMethod]
        [Ignore("measurement (F14): run on a native leg with the attribute removed; writes throwaway rows")]
        public void WriteMarkedFieldsOverM2()
        {
            EnsureNative(ResolveConnectionType());
            string tag = "t4n" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Measure("/system/script", new[] { "name", tag + "s" }, "owner", "t4n-owner");
            Measure("/interface/bridge", new[] { "name", tag + "b" }, "l2mtu", "1600", "/interface");

            using (var api = LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api))
            {
                // A policy that encrypts needs a peer; a disabled one, so no IPsec negotiation starts.
                string peerId = api.CreateCommandAndParameters("/ip/ipsec/peer/add", "name", tag + "p", "address", "10.250.3.7/32",
                    "disabled", "yes").ExecuteScalar();
                try
                {
                    Measure("/ip/ipsec/policy", new[] { "src-address", "10.250.1.0/24", "dst-address", "10.250.2.0/24", "tunnel", "yes",
                        "action", "encrypt", "peer", tag + "p", "disabled", "yes" }, "sa-src-address", "10.250.3.1");
                    Measure("/ip/ipsec/policy", new[] { "src-address", "10.250.4.0/24", "dst-address", "10.250.5.0/24", "tunnel", "yes",
                        "action", "encrypt", "peer", tag + "p", "disabled", "yes" }, "sa-dst-address", "10.250.3.2");
                }
                finally { api.CreateCommandAndParameters("/ip/ipsec/peer/remove", ".id", peerId).ExecuteNonQuery(); }

                string vrfId = api.CreateCommandAndParameters("/ip/vrf/add", "name", tag + "v", "interfaces", "none").ExecuteScalar();
                try { Measure("/interface/wireguard", new[] { "name", tag + "w", "disabled", "yes" }, "vrf", tag + "v", "/interface/wireguard", "/interface"); }
                finally { api.CreateCommandAndParameters("/ip/vrf/remove", ".id", vrfId).ExecuteNonQuery(); }

                // A file row: the type goes in at add.
                string name = tag + "dir";
                try
                {
                    Connection.CreateCommandAndParameters("/file/add", "name", name, "type", "directory").ExecuteNonQuery();
                    Console.WriteLine("/file add type=directory over native: API reads type="
                        + api.CreateCommandAndParameters("/file/print", "name", name).ExecuteList()
                            .Select(r => r.Words.TryGetValue("type", out var v) ? v : "(absent)").FirstOrDefault());
                }
                catch (Exception ex) when (!(ex is UnitTestAssertException))
                {
                    Console.WriteLine($"/file add type=directory over native: {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    foreach (var row in api.CreateCommandAndParameters("/file/print", "name", name).ExecuteList())
                        api.CreateCommandAndParameters("/file/remove", ".id", row.Words[".id"]).ExecuteNonQuery();
                }

                // The same value back: a write that moves nothing, to see whether the router answers it over M2.
                var ether2 = api.CreateCommandAndParameters("/interface/ethernet/print", "name", "ether2").ExecuteList().Single();
                try
                {
                    Connection.CreateCommandAndParameters("/interface/ethernet/set", ".id", ether2.Words[".id"],
                        "orig-mac-address", ether2.Words["orig-mac-address"]).ExecuteNonQuery();
                    Console.WriteLine("/interface/ethernet orig-mac-address, its own value over native: success");
                }
                catch (Exception ex) when (!(ex is UnitTestAssertException))
                {
                    Console.WriteLine($"/interface/ethernet orig-mac-address over native: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        private void Measure(string path, string[] addArgs, string field, string value, string writePath = null, string readPath = null)
        {
            writePath = writePath ?? path;
            using (var api = LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api))
            {
                string id = api.CreateCommandAndParameters(path + "/add", addArgs).ExecuteScalar();
                try
                {
                    string Read() => api.CreateCommandAndParameters((readPath ?? writePath) + "/print", ".id", id).ExecuteList()
                        .Select(r => r.Words.TryGetValue(field, out var v) ? v : "(absent)").FirstOrDefault();
                    string before = Read();
                    string apiOutcome;
                    try
                    {
                        api.CreateCommandAndParameters(writePath + "/set", ".id", id, field, value).ExecuteNonQuery();
                        apiOutcome = "API took it, reads " + Read();
                    }
                    catch (TikCommandTrapException ex) { apiOutcome = "API trap: " + ex.Message; }
                    string afterApi = Read();

                    string m2Value = string.Equals(afterApi, value, StringComparison.Ordinal) ? Other(value) : value;
                    string m2Outcome;
                    try
                    {
                        Connection.CreateCommandAndParameters(writePath + "/set", ".id", id, field, m2Value).ExecuteNonQuery();
                        m2Outcome = "native answered success, API reads " + Read();
                    }
                    catch (Exception ex) when (!(ex is UnitTestAssertException))
                    {
                        m2Outcome = $"M2 {ex.GetType().Name}: {ex.Message}; API reads " + Read();
                    }
                    Console.WriteLine($"{writePath} {field}: before {before}; {apiOutcome}; wrote {m2Value} over M2: {m2Outcome}");
                }
                finally
                {
                    api.CreateCommandAndParameters(path + "/remove", ".id", id).ExecuteNonQuery();
                }
            }
        }

        private static string Other(string value)
            => value == "1600" ? "1700"
             : value == "t4n-owner" ? "t4n-owner2"
             : value.EndsWith(".1", StringComparison.Ordinal) ? "10.250.3.9"
             : value.EndsWith(".2", StringComparison.Ordinal) ? "10.250.3.8" : value;
    }
}
