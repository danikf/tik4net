using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects.Ip.Firewall;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// The firewall <c>action</c> enums must know every value the router will actually send.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An unknown action is not a missing property. <c>TikEnumMetadata.Parse</c> throws
    /// <c>FormatException</c>, and the mapper turns that into a failed read of the <b>whole</b> menu — so a
    /// single unmapped action makes <c>/ip/firewall/mangle</c> or <c>/ip/firewall/filter</c> unreadable for
    /// anyone whose router uses it. <c>fasttrack-connection</c> is in the DEFAULT RouterOS firewall.
    /// </para>
    /// <para>
    /// <b>The lists below are the router's, not a guess.</b> They were taken on RouterOS 7.24 by asking the
    /// device to Tab-complete <c>/ip/firewall/&lt;menu&gt; add action=</c> over the CLI, which enumerates the
    /// values the menu accepts rather than the values some row happens to hold. That distinction is the
    /// point: no test that reads existing rows could have caught this, because the lab router had no rule
    /// with the missing actions — which is exactly why they survived since v1.2.0.
    /// </para>
    /// <para>
    /// Re-measure on a new RouterOS version with
    /// <c>mikrotik_cli_complete("/ip/firewall/mangle add action=")</c> (the <c>mikrotik</c> skill), and add
    /// what it reports. A value the router drops is not a failure here — this asserts that we can read
    /// everything it offers, not that the two lists are identical.
    /// </para>
    /// </remarks>
    [TestClass]
    public class FirewallActionVocabularyTests
    {
        /// <summary>RouterOS 7.24, <c>/ip/firewall/mangle add action=</c>.</summary>
        private static readonly string[] MangleActions =
        {
            "accept", "add-dst-to-address-list", "add-src-to-address-list", "change-dscp", "change-mss",
            "change-ttl", "clear-df", "drop", "fasttrack-connection", "jump", "log", "mark-connection",
            "mark-packet", "mark-routing", "passthrough", "return", "route", "set-priority", "sniff-pc",
            "sniff-tzsp", "strip-ipv4-options",
        };

        /// <summary>RouterOS 7.24, <c>/ip/firewall/filter add action=</c>.</summary>
        private static readonly string[] FilterActions =
        {
            "accept", "add-dst-to-address-list", "add-src-to-address-list", "drop", "fasttrack-connection",
            "jump", "log", "passthrough", "reject", "return", "tarpit",
        };

        /// <summary>RouterOS 7.24, <c>/ip/firewall/raw add action=</c>.</summary>
        private static readonly string[] RawActions =
        {
            "accept", "add-dst-to-address-list", "add-src-to-address-list", "drop", "jump", "log",
            "notrack", "passthrough", "return",
        };

        [TestMethod]
        public void MangleKnowsEveryActionTheRouterOffers()
            => AssertAllParse<FirewallMangle.ActionType>("/ip/firewall/mangle", MangleActions);

        [TestMethod]
        public void FilterKnowsEveryActionTheRouterOffers()
            => AssertAllParse<FirewallFilter.ActionType>("/ip/firewall/filter", FilterActions);

        [TestMethod]
        public void RawKnowsEveryActionTheRouterOffers()
            => AssertAllParse<FirewallRaw.ActionType>("/ip/firewall/raw", RawActions);

        /// <summary>
        /// Reads the enum's own <c>[TikEnum]</c> wire values, which is what <c>TikEnumMetadata</c> builds its
        /// parse table from — so this fails for the same reason a real read would. Shared with the other
        /// vocabulary tests (<see cref="InterfaceBridgeVocabularyTests"/>).
        /// </summary>
        /// <param name="field">The field the values belong to, for the failure message.</param>
        internal static void AssertAllParse<TEnum>(string menu, string[] routerValues, string field = "action") where TEnum : struct
        {
            var known = new HashSet<string>(
                Enum.GetNames(typeof(TEnum))
                    .Select(n => typeof(TEnum).GetField(n))
                    .Select(f => (tik4net.Objects.TikEnumAttribute)Attribute.GetCustomAttribute(
                        f, typeof(tik4net.Objects.TikEnumAttribute)))
                    .Where(a => a != null && !string.IsNullOrEmpty(a.Value))
                    .Select(a => a.Value),
                StringComparer.OrdinalIgnoreCase);

            var missing = routerValues.Where(v => !known.Contains(v)).ToList();

            Assert.AreEqual(0, missing.Count,
                $"{menu} accepts {field} values the entity cannot read, so a router using any of them fails "
                + $"the whole LoadAll: {string.Join(", ", missing)}");
        }
    }
}
