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
// Both are TikValueList properties: src-address-type of FirewallAddressType, tcp-flags of FirewallTcpFlag whose items
// carry their own '!' (and the whole list one more, spelled as a bare leading element: !,syn,!ack).
//
// The fixture is built and read back over a SIDE API CONNECTION: a write that echoed its own request would
// pass a native-write/native-read test while the router kept the old value.

using System;
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
        private static readonly string RuleComment = TestNames.Unique("tik4net-test-bitmask-rule");

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
                    TcpFlags = new TikValueList<FirewallTcpFlag>(FirewallTcpFlag.Syn, TikValue<FirewallTcpFlag>.Not(FirewallTcpFlag.Ack)),
                    SrcAddressType = new TikValueList<FirewallAddressType>(FirewallAddressType.Local),
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

            Assert.AreEqual("local", rule.SrcAddressType.ToString(), "a plain bitmask member");
            Assert.AreEqual(FirewallAddressType.Local, rule.SrcAddressType.Value[0].Value);
            // The negated member is not decoration: "syn" alone matches a different set of packets.
            Assert.AreEqual("syn,!ack", rule.TcpFlags.ToString(), "a bitmask with a negated member");
            Assert.IsTrue(rule.TcpFlags.Value.Single(f => f.Value == FirewallTcpFlag.Ack).IsNegated);
        }

        [TestMethod]
        public void AMixedTriStateReadsInTheOrderTheRouterPrintsIt()
        {
            // The router does not keep the order it was given: written "!fin,syn,!urg,ack", it prints the
            // plain members first and the negated ones after, each in bit order.
            var rule = TestRule(Connection);
            rule.TcpFlags = new TikValueList<FirewallTcpFlag>(TikValue<FirewallTcpFlag>.Not(FirewallTcpFlag.Fin),
                FirewallTcpFlag.Syn, TikValue<FirewallTcpFlag>.Not(FirewallTcpFlag.Urg), FirewallTcpFlag.Ack);
            Connection.Save(rule);

            using (var api = OpenSideApi())
                Assert.AreEqual("syn,ack,!fin,!urg", TestRule(api).TcpFlags.ToString(), "as the API reads it");
            var read = TestRule(Connection);
            Assert.AreEqual("syn,ack,!fin,!urg", read.TcpFlags.ToString(), "and as the transport under test reads it");
            Assert.AreEqual(rule.TcpFlags, read.TcpFlags, "the same list, in the router's order");
        }

        [TestMethod]
        public void BitmaskFieldsWrittenOverTheTransportUnderTestReachTheRouter()
        {
            var rule = TestRule(Connection);
            rule.SrcAddressType = new TikValueList<FirewallAddressType>(FirewallAddressType.Broadcast);
            rule.TcpFlags = new TikValueList<FirewallTcpFlag>(FirewallTcpFlag.Fin, FirewallTcpFlag.Rst, TikValue<FirewallTcpFlag>.Not(FirewallTcpFlag.Psh));
            Connection.Save(rule);

            using (var api = OpenSideApi())
            {
                var written = TestRule(api);
                Assert.AreEqual("broadcast", written.SrcAddressType.ToString(), "as the API reads it after the write");
                Assert.AreEqual("fin,rst,!psh", written.TcpFlags.ToString(), "as the API reads it after the write");
            }
        }

        [TestMethod]
        public void AWholeListNegation_RoundTrips()
        {
            // The API spells it as a bare leading element (!,syn,!ack); WinBox native as the field's 'not' flag.
            var rule = TestRule(Connection);
            rule.TcpFlags = TikValue<TikValueList<FirewallTcpFlag>>.Not(rule.TcpFlags.Value);
            rule.SrcAddressType = TikValue<TikValueList<FirewallAddressType>>.Not(rule.SrcAddressType.Value);
            Connection.Save(rule);

            using (var api = OpenSideApi())
            {
                var written = TestRule(api);
                Assert.AreEqual("!,syn,!ack", written.TcpFlags.ToString(), "as the API reads it after the write");
                Assert.AreEqual("!local", written.SrcAddressType.ToString(), "as the API reads it after the write");
            }
            var read = TestRule(Connection);
            Assert.IsTrue(read.TcpFlags.IsNegated, "and as the transport under test reads it");
            Assert.IsTrue(read.SrcAddressType.IsNegated);
        }

        [TestMethod]
        public void AnUpdateLeavingAHalfBehind_IsRefusedOverText_AndExactOverStructuredWrites()
        {
            // RouterOS's text set replaces only the half it names: syn,!ack + set rst → rst,!ack (API, REST, CLI).
            var rule = TestRule(Connection);
            rule.TcpFlags = new TikValueList<FirewallTcpFlag>(FirewallTcpFlag.Rst);

            if (Connection.Supports(TikConnectionCapability.StructuredWrites))
            {
                Connection.Save(rule);
                using (var api = OpenSideApi())
                    Assert.AreEqual("rst", TestRule(api).TcpFlags.ToString(), "nothing of the negated half is left");
            }
            else
            {
                Assert.ThrowsException<InvalidOperationException>(() => Connection.Save(rule));
                using (var api = OpenSideApi())
                    Assert.AreEqual("syn,!ack", TestRule(api).TcpFlags.ToString(), "nothing was sent");
            }
        }
    }
}
