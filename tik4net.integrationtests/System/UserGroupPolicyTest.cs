// UserGroupPolicyTest.cs — a bit set whose members come from a TABLE, not from a static map.
//
// /user/group's policy is the same bitmask shape as a firewall rule's src-address-type, except that its
// members are rows of the policy table: the bit index is the row's id, and the member name is what that
// window declares as the row's display value ('Alias' — 'read' — where its 'Name' holds the sentence
// "read router configuration").
//
// Over WinBox native it used to reach the caller as the raw number 654958, under the WinBox label
// 'policies' rather than the API's 'policy'. Writing was worse than impossible: with no member map the
// encoder matched none of the tokens and sent a clean, well-formed ZERO, so `policy=read,write` would have
// reported success and left the group allowed nothing.
//
// The '!' half matters as much as the plain one. `policy=read,write` over the binary API stores bits for
// read and write AND marks every other member of the table as denied — verified on 7.24 — which is what
// the API then prints back.

using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;

namespace tik4net.integrationtests
{
    [TestClass]
    public class UserGroupPolicyTest : TestBase
    {
        private const string GroupName = "tik4net-test-group";

        private static ITikConnection OpenSideApi()
            => LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api);

        [TestInitialize]
        public void CreateTestGroup()
        {
            using (var api = OpenSideApi())
            {
                RemoveTestGroup(api);
                api.CreateCommandAndParameters("/user/group/add",
                    "name", GroupName, "policy", "read,test").ExecuteNonQuery();
            }
        }

        [TestCleanup]
        public void DeleteTestGroup()
        {
            using (var api = OpenSideApi())
                RemoveTestGroup(api);
        }

        private static void RemoveTestGroup(ITikConnection conn)
        {
            foreach (var row in TestGroups(conn))
                conn.CreateCommandAndParameters("/user/group/remove",
                    TikSpecialProperties.Id, row.GetResponseField(TikSpecialProperties.Id)).ExecuteNonQuery();
        }

        private static List<ITikReSentence> TestGroups(ITikConnection conn)
            => conn.CreateCommand("/user/group/print").ExecuteList()
                   .Where(r => r.GetResponseFieldOrDefault("name", "") == GroupName).ToList();

        private static ITikReSentence TestGroup(ITikConnection conn) => TestGroups(conn).Single();

        [TestMethod]
        public void ATableBackedBitSetReadsAsTheApiSpellsIt()
        {
            // The API names the granted members and marks every other member of the table denied.
            string policy = TestGroup(Connection).GetResponseField("policy");

            using (var api = OpenSideApi())
                Assert.AreEqual(TestGroup(api).GetResponseField("policy"), policy,
                    "the transport under test must spell it exactly as the API does");

            Assert.IsTrue(policy.Split(',').Contains("read"), "the granted member, in: " + policy);
            Assert.IsTrue(policy.Split(',').Contains("!write"), "an explicitly denied member, in: " + policy);
        }

        [TestMethod]
        public void ATableBackedBitSetWrittenOverTheTransportUnderTestReachesTheRouter()
        {
            // A write settles the members it NAMES and leaves the rest of the row alone — the fixture's
            // `test` survives a write that does not mention it. That is the binary API's own behaviour for
            // this command, and it is what makes a three-member set different from a seventeen-member
            // rewrite: WinBox's editor always sends the whole checkbox state, and doing the same here would
            // silently revoke every permission the caller did not happen to list.
            string id = TestGroup(Connection).GetResponseField(TikSpecialProperties.Id);
            Connection.CreateCommandAndParameters("/user/group/set",
                "policy", "read,write,winbox", TikSpecialProperties.Id, id).ExecuteNonQuery();

            using (var api = OpenSideApi())
            {
                var members = TestGroup(api).GetResponseField("policy").Split(',');
                CollectionAssert.AreEquivalent(new[] { "read", "test", "write", "winbox" },
                    members.Where(m => !m.StartsWith("!")).ToArray(),
                    "the granted members, as the API reads them after the write");
                Assert.IsTrue(members.Contains("!ftp"),
                    "a member neither write named stays as the fixture left it");
            }
        }

        [TestMethod]
        public void AnExplicitlyDeniedMemberIsWrittenAsSuch()
        {
            string id = TestGroup(Connection).GetResponseField(TikSpecialProperties.Id);
            Connection.CreateCommandAndParameters("/user/group/set",
                "policy", "read,!write", TikSpecialProperties.Id, id).ExecuteNonQuery();

            using (var api = OpenSideApi())
            {
                var members = TestGroup(api).GetResponseField("policy").Split(',');
                Assert.IsTrue(members.Contains("read"));
                Assert.IsTrue(members.Contains("!write"));
            }
        }
    }
}
