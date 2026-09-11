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
    }
}
