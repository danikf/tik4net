using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;

namespace tik4net.integrationtests
{
    /// <summary>
    /// Copying a list from one RouterOS version to another with <c>SaveListDifferences</c>: rows read from the first lab
    /// router (CHR, RouterOS 7) are written to the second (CHR2, RouterOS 6), read back, and synced a second time. The
    /// second pass is the measurement: a row that reads back unequal to the row it was copied from is sent again on every
    /// sync, forever — a value one version spells differently, or a field one version does not print.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Needs both lab routers at once, so it reads both profiles itself (<see cref="LabConfig.GetFor"/>: the default keys and the
    /// <c>chr2.*</c> ones) rather than following <c>TIK4NET_ROUTER</c>; always over the binary API. The data it copies is
    /// fixed: TEST-NET addresses, an address list and a filter chain nothing jumps to, every row disabled, all named
    /// <c>t4n-copy-…</c> and removed from both routers at the end.
    /// </para>
    /// <para>
    /// <c>SaveListDifferences</c> pairs rows by <c>.id</c>, and ids are per router, so a copied row is created with no
    /// id (a row that kept the source's id would update whatever row the target has under it), and the second pass pairs
    /// by comment, taking the target's id.
    /// </para>
    /// </remarks>
    [TestClass]
    public class CrossVersionCopyProbe
    {
        private static readonly string[] Addresses = { "192.0.2.10", "192.0.2.0/28", "198.51.100.7", "203.0.113.0/24" };

        [TestMethod]
        [Ignore("measurement: needs both lab routers (CHR on RouterOS 7, CHR2 on RouterOS 6); comment out to run")]
        public void AddressListCopiedFrom7To6_SyncsToNoDifference()
        {
            string list = "t4n-copy-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Copy<FirewallAddressList>(
                seed: source =>
                {
                    for (int i = 0; i < Addresses.Length; i++)
                        source.Save(new FirewallAddressList
                        {
                            List = list, Address = Addresses[i], Comment = list + "-" + i, Disabled = true,
                        });
                },
                load: conn => conn.LoadList<FirewallAddressList>(conn.CreateParameter("list", list)),
                key: row => row.Comment.ValueOrDefault(null) ?? "");
        }

        [TestMethod]
        [Ignore("measurement: needs both lab routers (CHR on RouterOS 7, CHR2 on RouterOS 6); comment out to run")]
        public void FilterRulesCopiedFrom7To6_SyncToNoDifference()
        {
            string chain = "t4n-copy-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Copy<FirewallFilter>(
                seed: source =>
                {
                    source.Save(new FirewallFilter
                    {
                        Chain = chain, Action = FirewallFilter.ActionType.Accept, Protocol = "tcp", DstPort = "22,8291",
                        SrcAddress = Addresses[1], Comment = chain + "-0", Disabled = true,
                    });
                    source.Save(new FirewallFilter
                    {
                        Chain = chain, Action = FirewallFilter.ActionType.Drop, DstAddress = Addresses[3],
                        ConnectionState = FirewallFilter.ConnectionStateType.New | FirewallFilter.ConnectionStateType.Invalid,
                        Comment = chain + "-1", Disabled = true,
                    });
                    source.Save(new FirewallFilter
                    {
                        Chain = chain, Action = FirewallFilter.ActionType.Log, LogPrefix = "t4n", Protocol = "icmp",
                        Comment = chain + "-2", Disabled = true,
                    });
                },
                load: conn => conn.LoadList<FirewallFilter>(conn.CreateParameter("chain", chain)),
                key: row => row.Comment.ValueOrDefault(null) ?? "");
        }

        private static void Copy<TEntity>(Action<ITikConnection> seed, Func<ITikConnection, IEnumerable<TEntity>> load,
            Func<TEntity, string> key)
            where TEntity : new()
        {
            using (var source = Open(null))
            using (var target = Open("chr2"))
            {
                try
                {
                    seed(source);
                    var sourceRows = load(source).ToList();
                    Assert.IsTrue(sourceRows.Count > 0, "the seed made no rows on the source router");

                    // Pass 1: every row is new on the target.
                    var copies = sourceRows.Select(row => WithId(row.CloneEntity(), null)).ToList();
                    target.SaveListDifferences(copies, load(target).ToList());
                    var targetRows = load(target).ToDictionary(key);
                    Assert.AreEqual(sourceRows.Count, targetRows.Count, "rows on the target after the copy");

                    // Pass 2: the same list again, paired by key with the target's ids. Nothing may differ.
                    var report = new List<string>();
                    var again = new List<TEntity>();
                    foreach (var row in sourceRows)
                    {
                        var copy = WithId(row.CloneEntity(), IdOf(targetRows[key(row)]));
                        var fields = copy.GetDifferentFields(targetRows[key(row)]).ToList();
                        if (fields.Count > 0)
                            report.Add(key(row) + ": " + string.Join(", ", fields));
                        again.Add(copy);
                    }
                    target.SaveListDifferences(again, targetRows.Values.ToList());
                    Console.WriteLine("differences after the copy: " + (report.Count == 0 ? "none" : string.Join("; ", report)));
                    Assert.AreEqual(0, report.Count,
                        "rows that read back unequal on the target are sent again on every sync: " + string.Join("; ", report));
                }
                finally
                {
                    foreach (var conn in new[] { target, source })
                        foreach (var row in load(conn).ToList())
                            conn.Delete(row);
                }
            }
        }

        private static ITikConnection Open(string profile)
        {
            var setup = new TikConnectionSetup(LabConfig.GetFor(profile, "host"), LabConfig.GetFor(profile, "user"),
                LabConfig.GetFor(profile, "pass") ?? "");
            return setup.Create(TikConnectionType.Api);
        }

        private static string IdOf<TEntity>(TEntity entity)
            => (string)typeof(TEntity).GetProperty("Id").GetValue(entity);

        // The id is the router's own: a copy made for another router must not carry it.
        private static TEntity WithId<TEntity>(TEntity entity, string id)
        {
            typeof(TEntity).GetProperty("Id").SetValue(entity, id);
            return entity;
        }
    }
}
