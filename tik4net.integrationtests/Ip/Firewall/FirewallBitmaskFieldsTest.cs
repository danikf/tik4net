// FirewallBitmaskFieldsTest.cs — the two bitmask field shapes must read and write as the API spells them.
//
// A firewall rule carries both kinds:
//   src-address-type  a plain bitmask (webfig 'multibits') — one u32, one bit per member.
//   tcp-flags         a bitmask whose members can each be NEGATED (webfig 'multitristate') — the plain
//                     members in the field's own key and the '!' ones in its maskid sibling.
//
// Over WinBox native neither could be WRITTEN (both were refused as list types the encoder does not know),
// and tcp-flags could not be READ either: with no case of its own it fell through to the scalar enum
// branch and reached the caller as the bare number 2 where the API prints "syn". Every other transport
// carries the text the API prints, so this test is about the two agreeing.
//
// The fixture is built and read back over a SIDE API CONNECTION: a write that echoed its own request would
// pass a native-write/native-read test while the router kept the old value.

using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;

namespace tik4net.integrationtests.Ip.Firewall
{
    [TestClass]
    public class FirewallBitmaskFieldsTest : TestBase
    {
        private const string RuleComment = "tik4net-test-bitmask-rule";

        private static ITikConnection OpenSideApi()
            => LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api);

        [TestInitialize]
        public void CreateTestRule()
        {
            using (var api = OpenSideApi())
            {
                RemoveTestRule(api);
                api.Save(new FirewallFilter
                {
                    Chain = "forward",
                    Action = FirewallFilter.ActionType.Accept,
                    Protocol = "tcp",
                    TcpFlags = "syn,!ack",
                    SrcAddressType = "local",
                    Comment = RuleComment,
                    // Disabled: the lab router must not change how it forwards because a test ran.
                    Disabled = true,
                });
            }
        }

        [TestCleanup]
        public void DeleteTestRule()
        {
            using (var api = OpenSideApi())
                RemoveTestRule(api);
        }

        private static void RemoveTestRule(ITikConnection conn)
        {
            foreach (var r in conn.LoadList<FirewallFilter>().Where(r => r.Comment == RuleComment).ToList())
                conn.Delete(r);
        }

        private static FirewallFilter TestRule(ITikConnection conn)
            => conn.LoadList<FirewallFilter>().Single(r => r.Comment == RuleComment);

        [TestMethod]
        public void BitmaskFieldsReadAsTheApiSpellsThem()
        {
            var rule = TestRule(Connection);

            Assert.AreEqual("local", rule.SrcAddressType, "a plain bitmask member");
            // The negated member is not decoration: "syn" alone matches a different set of packets.
            Assert.AreEqual("syn,!ack", rule.TcpFlags, "a bitmask with a negated member");
        }

        [TestMethod]
        public void BitmaskFieldsWrittenOverTheTransportUnderTestReachTheRouter()
        {
            var rule = TestRule(Connection);
            rule.SrcAddressType = "broadcast";
            rule.TcpFlags = "fin,rst,!psh";
            Connection.Save(rule);

            using (var api = OpenSideApi())
            {
                var written = TestRule(api);
                Assert.AreEqual("broadcast", written.SrcAddressType, "as the API reads it after the write");
                Assert.AreEqual("fin,rst,!psh", written.TcpFlags, "as the API reads it after the write");
            }
        }
    }
}
