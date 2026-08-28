using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;
using tik4net.Objects;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// Holds the line on the rule that a paired rate field is <see cref="TikRatePair"/>, never
    /// <c>string</c> — the counterpart of <see cref="EntityDurationConventionTests"/> one notation over.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule exists for the same reason: verified on RouterOS 7.24, <c>/queue/simple max-limit</c> reads
    /// <c>1000000/2000000</c> over the binary API and <c>1M/2M</c> over the CLI transports, while the
    /// single-valued <c>/queue/tree max-limit</c> reads the same on both and stays a plain <c>long</c>. It
    /// is the pairing that changes the spelling, not the magnitude.
    /// </para>
    /// <para>
    /// <b>Neither the field's name nor the shape of its value can decide this, which is why the
    /// classification below is two explicit lists.</b> One <c>/queue/simple</c> row, read live, carries
    /// eight fields written <c>a/b</c> and only four of them are rates:
    /// <c>queue=default-small/default-small</c> is a pair of queue-type NAMES, <c>priority=8/8</c> a pair of
    /// small integers, <c>bucket-size=0.1/0.1</c> a pair of DECIMALS that <see cref="TikDataRate"/> would
    /// truncate, and <c>burst-time=0s/0s</c> a pair of durations. In the other direction the rate-sounding
    /// names are mostly not pairs at all: measured on the lab router,
    /// <c>/interface/ethernet/monitor rate</c> is <c>1Gbps</c> — a link speed whose unit
    /// <see cref="TikDataRate.TryParse"/> rejects outright, since its suffixes are <c>k M G T</c> and not
    /// <c>Mbps</c> — and <c>/ip/settings icmp-rate-mask</c> is the hex bitmask <c>0x1818</c>.
    /// </para>
    /// <para>
    /// So the test is a <b>ratchet</b>, like the duration one: <see cref="PendingTikRatePair"/> lists real
    /// paired rate fields still declared <c>string?</c> and tolerates them, <see cref="NotRatePairs"/> lists
    /// the ones that must stay <c>string</c> permanently and why, and anything outside both fails. The
    /// backlog is short because most candidates turned out to belong on the second list — which is the
    /// finding, not an accident of effort.
    /// </para>
    /// <para>
    /// <b>Where each classification comes from.</b> The <c>/queue/simple</c>, <c>/ip/settings</c> and
    /// <c>/interface/ethernet</c> entries were read from a live router. The wireless, CapsMan and Hotspot
    /// ones could not be: this lab CHR has no wireless hardware and no <c>/interface/wireless</c> menu at
    /// all, so those are classified from the values the RouterOS menu accepts, which is enough to settle
    /// "is this one value or several" even when it cannot settle a magnitude.
    /// </para>
    /// </remarks>
    [TestClass]
    public class EntityRatePairConventionTests
    {
        /// <summary>
        /// Fields whose wire name reads like a rate but which are <b>not</b> a pair of rates, and must stay
        /// <c>string</c>. Grouped by what they actually are.
        /// </summary>
        private static readonly Dictionary<string, string[]> NotRatePairs =
            new Dictionary<string, string[]>
            {
                // ── Lists of modulation rates, not a pair ───────────────────────────────────────────
                // '6Mbps,9Mbps,12Mbps…' and MCS index lists: several values, each carrying a unit
                // TikDataRate does not read.
                ["CapsManConfiguration"] = new[]
                {
                    "rates", "rates.basic", "rates.supported",
                    "rates.ht-basic-mcs", "rates.ht-supported-mcs",
                    "rates.vht-basic-mcs", "rates.vht-supported-mcs",
                },
                ["CapsManInterface"] = new[]
                {
                    "rates", "rates.basic", "rates.supported",
                    "rates.ht-basic-mcs", "rates.ht-supported-mcs",
                    "rates.vht-basic-mcs", "rates.vht-supported-mcs",
                },
                // 'tx-rate-set' is a list; 'tx-rate'/'rx-rate' are ONE link rate each, spelled with the
                // modulation ('HT20-SGI 65Mbps') — not a pair, and not a bare magnitude either. Two
                // separate fields for the two directions is the router's own model here, and pairing them
                // ourselves would invent a field it does not have.
                ["CapsManRegistrationTable"] = new[] { "tx-rate", "rx-rate", "tx-rate-set" },
                ["WifiRegistrationTable"] = new[] { "tx-rate", "rx-rate" },
                // 'strength-at-rates' is a list of signal@rate readings.
                ["WirelessRegistrationTable"] = new[] { "tx-rate", "rx-rate", "strength-at-rates" },
                ["InterfaceWireless"] = new[]
                {
                    "basic-rates-a/g", "basic-rates-b", "supported-rates-a/g", "supported-rates-b",
                    // A single rate, or the word 'disabled'.
                    "tdma-override-rate",
                    // Enums. The name is the whole trap: neither is a rate.
                    "rate-selection",   // advanced | legacy
                    "rate-set",         // configured | default
                    // A byte count [0..8192], one value.
                    "ht-amsdu-limit",
                },

                // ── One string packing several values ───────────────────────────────────────────────
                // 'rx-rate[/tx-rate] [rx-burst-rate/tx-burst-rate] [rx-burst-threshold/tx-burst-threshold]
                // [rx-burst-time/tx-burst-time] [priority] [rx-rate-min/tx-rate-min]' — up to six pairs in
                // one field. A TikRatePair would read the first pair and silently discard the rest.
                ["DhcpServerLease"] = new[] { "rate-limit" },
                ["PppProfile"] = new[] { "rate-limit" },
                ["HotspotUserProfile"] = new[] { "rate-limit" },
                // 'limit' is count,time,burst and 'dst-limit' adds a mode — comma-separated, not a pair.
                ["FirewallFilter"] = new[] { "limit", "dst-limit" },
                ["FirewallRaw"] = new[] { "limit", "dst-limit" },

                // ── Durations, caught here only because their name contains 'limit' ─────────────────
                // Both belong to the duration rule instead — see EntityDurationConventionTests, which
                // carries them on its own backlog.
                ["HotspotServerProfile"] = new[] { "rate-limit", "trial-uptime-limit" },
                ["HotspotUser"] = new[] { "limit-uptime" },

                // ── Link speeds and a bitmask ───────────────────────────────────────────────────────
                // Measured: '/interface/ethernet/monitor rate' is '1Gbps'. TikDataRate.TryParse rejects
                // that — the suffix table is k/M/G/T, so 'Gbps' ends in 's' and fails the number parse.
                // Typing this as a rate would turn a working read into a FormatException.
                ["EthernetMonitor"] = new[] { "rate" },
                // 'speed' is the same 10Mbps|1Gbps spelling; 'sfp-rate-select' is high|low, an enum.
                ["InterfaceEthernet"] = new[] { "speed", "sfp-rate-select" },
                // Measured '0x1818' — a hex mask of which ICMP types the rate limit applies to.
                ["IpSettings"] = new[] { "icmp-rate-mask" },
            };

        /// <summary>
        /// The backlog: real paired rate fields still declared <c>string?</c>. <b>Delete a line when you
        /// convert its field.</b> Nothing may be added.
        /// </summary>
        private static readonly Dictionary<string, string[]> PendingTikRatePair =
            new Dictionary<string, string[]>
            {
                // A genuine rx/tx pair — but its default is 'unlimited/unlimited', and TikDataRate has no
                // room for a word the way TikDuration has for 'none'. Converting it means giving the type
                // that first, so this is a backlog entry rather than an oversight. Not verifiable on the
                // lab CHR either: its virtual interfaces do not report the field at all.
                ["InterfaceEthernet"] = new[] { "bandwidth" },
            };

        // A wire name is a candidate when one of its hyphen- or dot-separated tokens is one of these.
        // Deliberately wide: the point of the exercise is that the name proves nothing, so everything the
        // name could implicate gets classified by hand rather than filtered away by a cleverer pattern.
        private static readonly string[] RateTokens =
            { "rate", "rates", "limit", "bandwidth", "speed", "throughput" };

        private static IEnumerable<(Type Entity, PropertyInfo Property, TikPropertyAttribute Attribute)> Properties()
            => typeof(tik4net.Objects.Ip.IpAddress).Assembly
                .GetTypes()
                .Where(t => t.GetCustomAttribute<TikEntityAttribute>() != null)
                .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(p => (Entity: t, Property: p, Attribute: p.GetCustomAttribute<TikPropertyAttribute>()!))
                    .Where(x => x.Attribute != null));

        private static bool IsCandidateName(string wireName)
            => wireName.Split('-', '.').Any(token => RateTokens.Contains(token));

        private static bool Listed(Dictionary<string, string[]> list, Type entity, string wireName)
            => list.TryGetValue(entity.Name, out var names) && names.Contains(wireName);

        // ── The ratchet ───────────────────────────────────────────────────────

        [TestMethod]
        public void ANewPairedRateFieldIsTikRatePairAndNotString()
        {
            var offenders = Properties()
                .Where(x => x.Property.PropertyType == typeof(string))
                .Where(x => IsCandidateName(x.Attribute.FieldName))
                .Where(x => !Listed(NotRatePairs, x.Entity, x.Attribute.FieldName))
                .Where(x => !Listed(PendingTikRatePair, x.Entity, x.Attribute.FieldName))
                .Select(x => $"{x.Entity.Name}.{x.Property.Name} (\"{x.Attribute.FieldName}\")")
                .OrderBy(s => s)
                .ToList();

            Assert.AreEqual(0, offenders.Count,
                "these fields have a rate-sounding name and are declared 'string?'. If the router writes one "
                + "as an upload/download pair of magnitudes, declare it 'TikRatePair?' so it reads the same "
                + "over every transport. If it is anything else — a list of modulation rates, a link speed "
                + "with a 'Mbps' unit, a bitmask, several values packed into one string — add it to "
                + "NotRatePairs with the value the router reports (see ARCHITECTURE.md, 'Adding an entity'):"
                + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        [TestMethod]
        public void TheBacklogHasNoStaleEntries()
        {
            var stale = new List<string>();

            foreach (var kv in PendingTikRatePair)
                foreach (string wire in kv.Value)
                {
                    var match = Properties().FirstOrDefault(
                        x => x.Entity.Name == kv.Key && x.Attribute.FieldName == wire);

                    if (match.Property == null)
                        stale.Add($"{kv.Key}.\"{wire}\" — no such mapped field any more");
                    else if (match.Property.PropertyType != typeof(string))
                        stale.Add($"{kv.Key}.{match.Property.Name} — now {match.Property.PropertyType.Name}, "
                                  + "delete it from PendingTikRatePair");
                }

            Assert.AreEqual(0, stale.Count,
                "PendingTikRatePair is the remaining backlog and nothing else:" + Environment.NewLine
                + string.Join(Environment.NewLine, stale));
        }

        [TestMethod]
        public void TheNotRatePairListHasNoStaleEntries()
        {
            // Both directions, as with the durations: an entry naming a field that no longer exists, and an
            // entry naming one that has since been typed. Either way the list has stopped describing the
            // code, and would go on excusing the next field that happens to share the name.
            var stale = new List<string>();

            foreach (var kv in NotRatePairs)
                foreach (string wire in kv.Value)
                {
                    var match = Properties().FirstOrDefault(
                        x => x.Entity.Name == kv.Key && x.Attribute.FieldName == wire);

                    if (match.Property == null)
                        stale.Add($"{kv.Key}.\"{wire}\" — no such mapped field any more");
                    else if (match.Property.PropertyType != typeof(string))
                        stale.Add($"{kv.Key}.{match.Property.Name} — now {match.Property.PropertyType.Name}, "
                                  + "so it is not an exception to the rule any more");
                }

            Assert.AreEqual(0, stale.Count, string.Join(Environment.NewLine, stale));
        }

        [TestMethod]
        public void TheFieldsAlreadyTypedStayTyped()
        {
            // The converted set is small enough to name, so name it: a silent revert to string would
            // otherwise show up only as one more tolerated line on a backlog.
            var expected = new[]
            {
                ("QueueSimple", "limit-at"), ("QueueSimple", "max-limit"),
                ("QueueSimple", "burst-limit"), ("QueueSimple", "burst-threshold"),
                ("QueueSimple", "rate"), ("QueueSimple", "packet-rate"),
            };

            var wrong = expected
                .Select(e => (Entity: e.Item1, Wire: e.Item2, Match: Properties().FirstOrDefault(
                    x => x.Entity.Name == e.Item1 && x.Attribute.FieldName == e.Item2)))
                .Where(x => x.Match.Property == null
                            || x.Match.Property.PropertyType != typeof(TikRatePair?))
                .Select(x => $"{x.Entity}.\"{x.Wire}\" is "
                             + (x.Match.Property == null
                                 ? "no longer mapped"
                                 : "declared " + x.Match.Property.PropertyType.Name))
                .ToList();

            Assert.AreEqual(0, wrong.Count,
                "these are paired rate fields and must stay TikRatePair?:" + Environment.NewLine
                + string.Join(Environment.NewLine, wrong));
        }

        [TestMethod]
        public void TheBacklogIsSmallerThanItWas()
        {
            // A number to watch rather than a rule to satisfy — see the duration test, which started at 101.
            // This one starts at 1, because the classification work happened before the list was written
            // rather than after.
            const int WhenThisTestWasWritten = 1;

            int pending = PendingTikRatePair.Sum(kv => kv.Value.Length);
            Assert.IsTrue(pending <= WhenThisTestWasWritten,
                $"the string rate-pair backlog grew from {WhenThisTestWasWritten} to {pending}");
        }
    }
}
