using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;
using tik4net.Testing;

namespace tik4net.tests
{
    // ---------------------------------------------------------------------------
    // Sample service class — the kind of code a tik4net consumer would write.
    // Tests below verify its behaviour without touching a real router.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Manages a named MikroTik firewall address-list (e.g. "BLACKLIST").
    /// Demonstrates typical tik4net usage: load, insert, remove, bulk-sync.
    /// </summary>
    internal class FirewallListManager
    {
        private readonly ITikConnection _conn;
        private readonly string _listName;

        public FirewallListManager(ITikConnection conn, string listName)
        {
            _conn = conn;
            _listName = listName;
        }

        public IList<FirewallAddressList> GetAll()
            => _conn.LoadList<FirewallAddressList>(
                    _conn.CreateParameter("list", _listName, TikCommandParameterFormat.Filter))
                .ToList();

        public string Add(string address, string comment = null)
        {
            var entry = new FirewallAddressList
            {
                List    = _listName,
                Address = address,
                Comment = comment ?? "",
            };
            _conn.Save(entry);
            return entry.Id;
        }

        public void Remove(string address)
        {
            var entry = GetAll().FirstOrDefault(e => e.Address == address);
            if (entry == null)
                throw new KeyNotFoundException($"Address '{address}' not found in list '{_listName}'.");
            _conn.Delete(entry);
        }

        /// <summary>Replaces the entire list with <paramref name="addresses"/>.</summary>
        public void ReplaceAll(IEnumerable<string> addresses)
        {
            var current = GetAll();
            var backup  = current.CloneEntityList();

            // Build desired state — keep existing entries (same address) to avoid unnecessary churn
            var desired = new List<FirewallAddressList>();
            foreach (var addr in addresses)
            {
                var existing = current.FirstOrDefault(e => e.Address == addr);
                desired.Add(existing ?? new FirewallAddressList { List = _listName, Address = addr });
            }

            _conn.SaveListDifferences(desired, backup);
        }
    }


    // ---------------------------------------------------------------------------
    // Tests — no router required, all via TikFakeConnection
    // ---------------------------------------------------------------------------

    [TestClass]
    public class FakeConnectionSampleTest
    {
        private const string ListName = "BLACKLIST";

        // ── Helpers ────────────────────────────────────────────────────────────

        private static FirewallAddressList FakeEntry(string id, string address, string comment = "")
            => new FirewallAddressList { List = ListName, Address = address, Comment = comment }
                .WithId(id);

        private TikFakeConnection ConnWith(params FirewallAddressList[] entries)
            => new TikFakeConnection()
                .WithEntities(entries);

        // ── GetAll ─────────────────────────────────────────────────────────────

        [TestMethod]
        public void GetAll_EmptyList_ReturnsEmpty()
        {
            var conn = ConnWith(/* no entries */);
            var mgr  = new FirewallListManager(conn, ListName);

            var result = mgr.GetAll();

            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void GetAll_TwoEntries_ReturnsBoth()
        {
            var conn = ConnWith(
                FakeEntry("*1", "10.0.0.1"),
                FakeEntry("*2", "10.0.0.2", "bad actor"));
            var mgr = new FirewallListManager(conn, ListName);

            var result = mgr.GetAll();

            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.Any(e => e.Address == "10.0.0.1"));
            Assert.AreEqual("bad actor", result.First(e => e.Address == "10.0.0.2").Comment);
        }

        // ── Add ────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Add_NewAddress_SendsAddCommandAndReturnsId()
        {
            var conn = new TikFakeConnection()
                .WithEntities<FirewallAddressList>()              // /print → empty (used by Save's diff check — but new entity skips it)
                .WithScalarResponse(
                    rows => rows.First() == "/ip/firewall/address-list/add",
                    "*99");
            var mgr = new FirewallListManager(conn, ListName);

            string id = mgr.Add("192.168.1.100", "test host");

            Assert.AreEqual("*99", id);
            conn.AssertWasSent("/ip/firewall/address-list/add");
            conn.AssertWasSent(rows => rows.Any(r => r.Contains("address=192.168.1.100")));
            conn.AssertWasSent(rows => rows.Any(r => r.Contains("comment=test host")));
        }

        // ── Remove ─────────────────────────────────────────────────────────────

        [TestMethod]
        public void Remove_ExistingAddress_SendsRemoveCommand()
        {
            var conn = new TikFakeConnection()
                .WithEntities(FakeEntry("*3", "10.0.0.3"))
                .WithNonQuery(rows => rows.First() == "/ip/firewall/address-list/remove");
            var mgr = new FirewallListManager(conn, ListName);

            mgr.Remove("10.0.0.3");

            conn.AssertWasSent("/ip/firewall/address-list/remove");
            conn.AssertWasSent(rows => rows.Any(r => r.Contains(".id=*3")));
        }

        [TestMethod]
        public void Remove_MissingAddress_Throws()
        {
            var conn = ConnWith(FakeEntry("*1", "10.0.0.1"));
            var mgr  = new FirewallListManager(conn, ListName);

            Assert.ThrowsException<KeyNotFoundException>(() => mgr.Remove("99.99.99.99"));
        }

        // ── ReplaceAll ─────────────────────────────────────────────────────────

        [TestMethod]
        public void ReplaceAll_AddsNewAndRemovesOld()
        {
            // Router currently has *1=10.0.0.1, *2=10.0.0.2
            // Desired: 10.0.0.1 (keep), 10.0.0.3 (add), drop 10.0.0.2
            var conn = new TikFakeConnection()
                .WithEntities(
                    FakeEntry("*1", "10.0.0.1"),
                    FakeEntry("*2", "10.0.0.2"))
                .WithNonQuery(rows => rows.First() == "/ip/firewall/address-list/remove")
                .WithScalarResponse(
                    rows => rows.First() == "/ip/firewall/address-list/add",
                    "*10");
            var mgr = new FirewallListManager(conn, ListName);

            mgr.ReplaceAll(new[] { "10.0.0.1", "10.0.0.3" });

            conn.AssertWasSent("/ip/firewall/address-list/remove"); // *2 deleted
            conn.AssertWasSent("/ip/firewall/address-list/add");    // 10.0.0.3 added
            Assert.AreEqual(0, conn.GetCallCount("/ip/firewall/address-list/set")); // *1 unchanged
        }

        [TestMethod]
        public void ReplaceAll_EmptyDesired_DeletesAll()
        {
            var conn = new TikFakeConnection()
                .WithEntities(
                    FakeEntry("*1", "10.0.0.1"),
                    FakeEntry("*2", "10.0.0.2"))
                .WithNonQuery(rows => rows.First() == "/ip/firewall/address-list/remove");
            var mgr = new FirewallListManager(conn, ListName);

            mgr.ReplaceAll(Enumerable.Empty<string>());

            Assert.AreEqual(2, conn.GetCallCount("/ip/firewall/address-list/remove"));
        }

        // ── Error handling ─────────────────────────────────────────────────────

        [TestMethod]
        public void Add_RouterRejectsEntry_ThrowsTrapException()
        {
            var conn = new TikFakeConnection()
                .WithTrap(
                    rows => rows.First() == "/ip/firewall/address-list/add",
                    "bad address");
            var mgr = new FirewallListManager(conn, ListName);

            Assert.ThrowsException<TikCommandTrapException>(() => mgr.Add("not-an-ip"));
        }

        // ── Low-level / mid-level coverage ────────────────────────────────────

        [TestMethod]
        public void LowLevel_CallCommandSync_ReturnsRegisteredSentences()
        {
            var conn = new TikFakeConnection()
                .WithResponse(
                    rows => rows.First() == "/system/identity/print",
                    new ITikSentence[]
                    {
                        new TikFakeReSentence(new System.Collections.Generic.Dictionary<string, string>
                            { { "name", "TestRouter" } }),
                        new TikFakeDoneSentence(),
                    });

            var result = conn.CallCommandSync("/system/identity/print").ToList();

            Assert.AreEqual(2, result.Count);
            var re = result.OfType<ITikReSentence>().Single();
            Assert.AreEqual("TestRouter", re.GetResponseField("name"));
        }

        [TestMethod]
        public void MidLevel_ExecuteScalar_ReturnsRouterIdentity()
        {
            var conn = new TikFakeConnection()
                .WithResponse(
                    rows => rows.First() == "/system/identity/print",
                    new ITikSentence[]
                    {
                        new TikFakeReSentence(new System.Collections.Generic.Dictionary<string, string>
                            { { "name", "TestRouter" } }),
                        new TikFakeDoneSentence(),
                    });

            string identity = conn.CreateCommand("/system/identity/print").ExecuteScalar();

            Assert.AreEqual("TestRouter", identity);
        }

        [TestMethod]
        public void MidLevel_ExecuteAsync_DeliversCallbacksFromThread()
        {
            var received = new List<string>();
            var finished = new ManualResetEventSlim(false);

            var conn = new TikFakeConnection()
                .WithResponse(
                    rows => rows.First() == "/ip/address/print",
                    new ITikSentence[]
                    {
                        new TikFakeReSentence(new System.Collections.Generic.Dictionary<string, string>
                            { { ".id", "*1" }, { "address", "10.0.0.1/24" }, { "interface", "ether1" } }),
                        new TikFakeReSentence(new System.Collections.Generic.Dictionary<string, string>
                            { { ".id", "*2" }, { "address", "10.0.0.2/24" }, { "interface", "ether2" } }),
                        new TikFakeDoneSentence(),
                    });

            ITikCommand cmd = conn.CreateCommand("/ip/address/print");
            cmd.ExecuteAsync(
                oneResponseCallback: re   => received.Add(re.GetResponseField("address")),
                onDoneCallback:      ()   => finished.Set());

            Assert.IsTrue(finished.Wait(TimeSpan.FromSeconds(3)), "Async callbacks not delivered in time.");
            CollectionAssert.AreEqual(new[] { "10.0.0.1/24", "10.0.0.2/24" }, received);
        }
    }

}
