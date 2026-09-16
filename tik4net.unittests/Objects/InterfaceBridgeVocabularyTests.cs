using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// <see cref="InterfaceBridge"/>'s enums must know every value the router will send — the same defect
    /// class as <see cref="FirewallActionVocabularyTests"/>, and for the same reason: one unmapped value fails
    /// the <b>whole</b> <c>LoadAll&lt;InterfaceBridge&gt;()</c>, not just the property.
    /// </summary>
    /// <remarks>
    /// Lists taken on RouterOS 7.24 by Tab-completing <c>/interface bridge add protocol-mode=</c> and
    /// <c>… arp=</c> over the CLI (<c>mikrotik_cli_complete</c>). Both enums predate RouterOS 7's
    /// <c>mstp</c> and <c>local-proxy-arp</c>, so any router with an MSTP bridge, or a bridge answering ARP
    /// as a local proxy, could not have its bridges read at all.
    /// </remarks>
    [TestClass]
    public class InterfaceBridgeVocabularyTests
    {
        /// <summary>RouterOS 7.24, <c>/interface bridge add protocol-mode=</c>.</summary>
        private static readonly string[] ProtocolModes = { "mstp", "none", "rstp", "stp" };

        /// <summary>RouterOS 7.24, <c>/interface bridge add arp=</c>.</summary>
        private static readonly string[] ArpModes =
            { "disabled", "enabled", "local-proxy-arp", "proxy-arp", "reply-only" };

        [TestMethod]
        public void ProtocolModeKnowsEveryModeTheRouterOffers()
            => FirewallActionVocabularyTests.AssertAllParse<InterfaceBridge.ProtocolModeModes>(
                "/interface/bridge", ProtocolModes, "protocol-mode");

        [TestMethod]
        public void ArpKnowsEveryModeTheRouterOffers()
            => FirewallActionVocabularyTests.AssertAllParse<InterfaceBridge.ArpMode>(
                "/interface/bridge", ArpModes, "arp");

        [TestMethod]
        public void AnMstpBridgeWithLocalProxyArpCanBeRead()
        {
            // The vocabulary tests above check the attribute tables; this one is the symptom a user sees —
            // the read of the whole menu — so it fails the way LoadAll failed, with the FormatException.
            var connection = new TikFakeConnection()
                .WithResponse(rows => rows.First() == "/interface/bridge/print", new ITikSentence[]
                {
                    new TikFakeReSentence(new Dictionary<string, string>
                    {
                        { ".id", "*1" }, { "name", "bridge-mstp" },
                        { "protocol-mode", "mstp" }, { "arp", "local-proxy-arp" },
                    }),
                    new TikFakeDoneSentence(),
                });

            var bridge = connection.LoadAll<InterfaceBridge>().Single();

            Assert.AreEqual(InterfaceBridge.ProtocolModeModes.Mstp, bridge.ProtocolMode);
            Assert.AreEqual(InterfaceBridge.ArpMode.LocalProxyArp, bridge.Arp);
        }

        [TestMethod]
        public void EveryFieldOfARouterOs724BridgeIsRead()
        {
            // The row RouterOS 7.24.3 prints for an MSTP bridge with VLAN filtering, IGMP and DHCP snooping on —
            // the widest print the menu has. Before the 4.0 upgrade the entity mapped 13 of these.
            var row = new Dictionary<string, string>
            {
                { ".id", "*665" }, { "name", "br-full" }, { "mtu", "auto" }, { "actual-mtu", "1500" },
                { "l2mtu", "65535" }, { "arp", "enabled" }, { "arp-timeout", "auto" },
                { "mac-address", "AA:BB:CC:DD:EE:FF" }, { "protocol-mode", "mstp" }, { "fast-forward", "true" },
                { "igmp-snooping", "true" }, { "multicast-router", "temporary-query" },
                { "multicast-querier", "false" }, { "querier-uses-bridge-address", "true" },
                { "startup-query-count", "2" }, { "last-member-query-count", "2" }, { "last-member-interval", "1s" },
                { "membership-interval", "4m20s" }, { "querier-interval", "4m15s" }, { "query-interval", "2m5s" },
                { "query-response-interval", "10s" }, { "startup-query-interval", "31s250ms" },
                { "igmp-version", "2" }, { "mld-version", "1" }, { "auto-mac", "true" }, { "ageing-time", "5m" },
                { "priority", "0x8000" }, { "max-message-age", "20s" }, { "forward-delay", "15s" },
                { "transmit-hold-count", "6" }, { "region-name", "" }, { "region-revision", "0" },
                { "max-hops", "20" }, { "vlan-filtering", "true" }, { "ether-type", "0x8100" }, { "pvid", "1" },
                { "frame-types", "admit-all" }, { "ingress-filtering", "true" }, { "dhcp-snooping", "true" },
                { "dhcpv6-snooping", "false" }, { "ra-guard", "false" }, { "port-cost-mode", "long" },
                { "mvrp", "false" }, { "max-learned-entries", "auto" }, { "mlag-peer-port", "none" },
                { "mlag-priority", "128" }, { "mlag-heartbeat", "5s" }, { "managed", "false" },
                { "dynamic", "false" }, { "running", "true" }, { "disabled", "false" }, { "comment", "probe" },
            };
            var connection = new TikFakeConnection()
                .WithResponse(rows => rows.First() == "/interface/bridge/print",
                    new ITikSentence[] { new TikFakeReSentence(row), new TikFakeDoneSentence() });

            var mapped = new HashSet<string>(typeof(InterfaceBridge).GetProperties()
                .Select(p => p.GetCustomAttributes(typeof(TikPropertyAttribute), false)
                    .Cast<TikPropertyAttribute>().SingleOrDefault()?.FieldName)
                .Where(n => n != null));
            CollectionAssert.AreEquivalent(new string[0], row.Keys.Where(k => !mapped.Contains(k)).ToList(),
                "fields RouterOS 7.24 prints that the entity does not map");

            var bridge = connection.LoadAll<InterfaceBridge>().Single();

            Assert.AreEqual("br-full", bridge.ToString());
            Assert.AreEqual("probe", bridge.Comment);
            Assert.AreEqual(false, bridge.Disabled);
            Assert.AreEqual("auto", bridge.ArpTimeout?.Token);
            Assert.AreEqual(TimeSpan.FromMilliseconds(31250), bridge.StartupQueryInterval?.Value);
            Assert.AreEqual(6, bridge.TransmitHoldCount);
            Assert.AreEqual(20, bridge.MaxHops);
            Assert.AreEqual(1, bridge.Pvid);
            Assert.AreEqual(InterfaceBridge.FrameTypesMode.AdmitAll, bridge.FrameTypes);
            Assert.AreEqual(InterfaceBridge.MulticastRouterMode.TemporaryQuery, bridge.MulticastRouter);
            Assert.AreEqual(InterfaceBridge.PortCostModeType.Long, bridge.PortCostMode);
            Assert.AreEqual(true, bridge.IgmpSnooping);
            Assert.AreEqual(true, bridge.DhcpSnooping);
            Assert.AreEqual("1500", bridge.ActualMtu);
            Assert.IsTrue(bridge.Running);
        }

        [TestMethod]
        public void AFreshBridgeSendsOnlyWhatWasAssigned()
        {
            // The pre-4.0 entity seeded nine router defaults in its constructor and had a non-nullable
            // TransmitHoldCount, so every add also sent ageing-time, arp, auto-mac, forward-delay, …
            var connection = new TikFakeConnection()
                .WithScalarResponse(rows => rows.First() == "/interface/bridge/add", "*9");

            connection.Save(new InterfaceBridge { Name = "br-new", VlanFiltering = true });

            var add = connection.SentCommands.Single(c => c[0] == "/interface/bridge/add");
            CollectionAssert.AreEquivalent(new[] { "=name=br-new", "=vlan-filtering=yes" }, add.Skip(1).ToArray());
        }
    }
}
