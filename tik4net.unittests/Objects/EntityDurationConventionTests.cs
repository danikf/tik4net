using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// Holds the line on the rule that a duration field is <see cref="TikDuration"/>, never <c>string</c> —
    /// and pins the backlog of fields that are still the old way, so it can only shrink.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule exists because the router writes the same duration two ways depending on who asked:
    /// <c>10s</c> / <c>200ms</c> / <c>1d</c> over the API, REST and native WinBox, and <c>00:00:10</c> /
    /// <c>00:00:00.200</c> / <c>1d00:00:00</c> over the CLI transports, which read <c>print as-value</c>. A
    /// <c>string</c> property hands that difference to the caller: the same field compares unequal to itself
    /// across transports, and <c>Save</c> sees a default-valued field as changed. <see cref="TikDuration"/>
    /// reads both forms, writes the compact one, and keeps the words the router uses in place of a duration
    /// (<c>none</c>, <c>disabled</c>, <c>auto</c>) rather than flattening them to zero.
    /// </para>
    /// <para>
    /// It was documented but nothing enforced it, and documentation on its own lost: 108 duration fields were
    /// still <c>string?</c> when this test was written, against 24 converted. The source carries a
    /// <c>/*time*/</c> marker comment on many of them, and counting those alone gives 101 — the marker is not
    /// the measure either way, since it also sits on several fields that are not durations at all. Reflection
    /// over the wire names found 16 more offenders with no marker, and the three lists below were sorted out
    /// by hand from there. So the test is built
    /// as a <b>ratchet</b> rather than a pass/fail: <see cref="PendingTikDuration"/> lists the known offenders
    /// and they are tolerated, while anything outside it fails. Converting one means deleting its line here —
    /// and the list is checked for staleness in both directions, so a converted field left in the list fails
    /// too. The backlog cannot grow and cannot silently stop shrinking.
    /// </para>
    /// <para>
    /// <b>Not every field whose name reads like a duration is one</b>, which is why the classification is two
    /// explicit lists rather than a rule. <see cref="NotDurations"/> holds the ones that must stay
    /// <c>string</c> permanently: a time of day, a time-of-day range with weekdays, a UTC offset. Verified on
    /// RouterOS 7.x — <c>/system/clock</c> reports <c>time=22:26:19</c> and <c>gmt-offset=+02:00</c>, neither
    /// of which is an elapsed anything. The wider traps are in ARCHITECTURE.md: <c>build-time</c> and
    /// <c>last-link-up-time</c> are timestamps, firewall <c>ttl</c> is a hop count (DNS <c>ttl</c> is not),
    /// and <c>/queue/simple burst-time</c> is an upload/download <i>pair</i> while <c>/queue/tree</c>'s is a
    /// single duration.
    /// </para>
    /// </remarks>
    [TestClass]
    public class EntityDurationConventionTests
    {
        /// <summary>
        /// Fields whose wire name looks temporal but which are <b>not</b> durations and must stay
        /// <c>string</c>. Each is a time of day, a time-of-day range, or an offset.
        /// </summary>
        private static readonly Dictionary<string, string[]> NotDurations =
            new Dictionary<string, string[]>
            {
                // '22:26:19' — a wall-clock reading, and '+02:00' — a UTC offset. Both verified live.
                ["SystemClock"] = new[] { "time", "gmt-offset" },
                // 'HH:MM:SS' or the word 'startup' — when to run, not how long for. ('interval' beside it
                // IS a duration and is in the backlog below.)
                ["SystemScheduler"] = new[] { "start-time" },
                // A match condition: a time-of-day RANGE plus weekdays, e.g. '8h-17h,mon,tue,wed'.
                ["CapsManAccessList"] = new[] { "time" },
                ["FirewallRaw"] = new[] { "time" },
                ["WifiAccessList"] = new[] { "time" },
                // Not a duration at all: an enum, 'any' or 'long'. The name is the trap — it is the 802.11
                // guard interval as a MODE, not a length. (The lab CHR has no wireless to read it back from;
                // classified from the RouterOS menu's accepted values.)
                ["InterfaceWireless"] = new[] { "ht-guard-interval" },
                // A soft/hard PAIR, '0s/0s' — the same shape as /queue/simple burst-time, and a pair stays a
                // string for the reason ARCHITECTURE.md gives.
                ["IpsecInstalledSa"] = new[] { "add-lifetime" },
                // A comma-separated LIST of intervals ('30m,10m,5m'), not one duration.
                ["HotspotUserProfile"] = new[] { "advertise-interval" },
            };

        /// <summary>
        /// The backlog: real duration fields still declared <c>string?</c>. **Delete a line when you convert
        /// its field.** Nothing may be added.
        /// </summary>
        private static readonly Dictionary<string, string[]> PendingTikDuration =
            new Dictionary<string, string[]>
            {
                ["CapsManAccessList"] = new[] { "allow-signal-out-of-range" },
                ["CapsManChannel"] = new[] { "reselect-interval" },
                ["CapsManConfiguration"] = new[] { "channel.reselect-interval", "disconnect-timeout", "frame-lifetime", "security.group-key-update" },
                ["CapsManInterface"] = new[] { "arp-timeout" },
                ["CapsManSecurity"] = new[] { "group-key-update" },
                ["Certificate"] = new[] { "expires-after" },
                ["DhcpServerAlert"] = new[] { "alert-timeout" },
                ["ConnectionTracking"] = new[] { "generic-timeout", "icmp-timeout", "tcp-close-timeout", "tcp-close-wait-timeout", "tcp-established-timeout", "tcp-fin-wait-timeout", "tcp-last-ack-timeout", "tcp-syn-received-timeout", "tcp-syn-sent-timeout", "tcp-time-wait-timeout", "udp-stream-timeout", "udp-timeout" },
                ["DnsStatic"] = new[] { "ttl" },
                ["FirewallFilter"] = new[] { "address-list-timeout" },
                ["FirewallRaw"] = new[] { "address-list-timeout" },
                ["HotspotServerProfile"] = new[] { "http-cookie-lifetime", "radius-interim-update", "trial-uptime-limit", "trial-uptime-reset" },
                ["HotspotActive"] = new[] { "idle-timeout" },
                ["HotspotServer"] = new[] { "idle-timeout", "keepalive-timeout", "login-timeout" },
                ["HotspotUser"] = new[] { "limit-uptime" },
                ["HotspotUserProfile"] = new[] { "advertise-timeout", "idle-timeout", "keepalive-timeout", "mac-cookie-timeout", "session-timeout" },
                ["InterfaceBonding"] = new[] { "arp-interval", "arp-timeout", "down-delay", "mii-interval", "up-delay" },
                ["InterfaceBridge"] = new[] { "ageing-time", "forward-delay", "max-message-age" },
                ["InterfaceEoip"] = new[] { "arp-timeout", "loop-protect-disable-time", "loop-protect-send-interval" },
                ["InterfaceVlan"] = new[] { "loop-protect-disable-time", "loop-protect-send-interval" },
                ["InterfaceVrrp"] = new[] { "arp-timeout", "interval" },
                ["InterfaceWifi"] = new[] { "arp-timeout" },
                ["InterfaceWireless"] = new[] { "disconnect-timeout" },
                ["InterfaceVxlan"] = new[] { "arp-timeout", "loop-protect-disable-time", "loop-protect-send-interval" },
                ["IpCloud"] = new[] { "ddns-update-interval" },
                ["IpDhcpServer"] = new[] { "lease-time" },
                ["IpDns"] = new[] { "cache-max-ttl" },
                ["IpProxy"] = new[] { "max-fresh-time" },
                ["IpSettings"] = new[] { "arp-timeout" },
                ["IpSocks"] = new[] { "connection-idle-timeout" },
                ["IpTrafficFlow"] = new[] { "active-flow-timeout", "inactive-flow-timeout" },
                ["IpTrafficFlowTarget"] = new[] { "v9-template-timeout" },
                ["IpsecActivePeers"] = new[] { "last-seen", "uptime" },
                ["IpsecInstalledSa"] = new[] { "expires-in" },
                ["IpsecProfile"] = new[] { "dpd-interval", "lifetime" },
                ["IpsecProposal"] = new[] { "lifetime" },
                ["OvpnServer"] = new[] { "keepalive-timeout" },
                ["OspfInterfaceTemplate"] = new[] { "dead-interval", "hello-interval", "retransmit-interval", "transmit-delay" },
                ["OspfNeighbor"] = new[] { "adjacency", "timeout" },
                ["PppAaa"] = new[] { "interim-update" },
                ["PppProfile"] = new[] { "idle-timeout", "session-timeout" },
                ["Radius"] = new[] { "radsec-timeout", "timeout" },
                ["SystemScheduler"] = new[] { "interval" },
                ["SystemWatchdog"] = new[] { "ping-start-after-boot", "ping-timeout" },
                ["ToolNetwatch"] = new[] { "interval", "packet-interval", "start-delay", "startup-delay", "tcp-connect-time", "thr-avg", "thr-http-time", "thr-jitter", "thr-max", "thr-stdev", "timeout" },
                ["WifiAccessList"] = new[] { "allow-signal-out-of-range" },
                ["WifiChannel"] = new[] { "reselect-interval" },
                ["WifiConfiguration"] = new[] { "beacon-interval" },
                ["WifiRegistrationTable"] = new[] { "last-activity", "uptime" },
                ["WifiSecurity"] = new[] { "ft-r0-key-lifetime", "group-key-update" },
                ["WirelessSecurityProfile"] = new[] { "interim-update" },
            };

        // Wire-name shapes that mean "elapsed time" on RouterOS. Deliberately narrow: a suffix that also
        // spells timestamps ('-time' alone) would drag in build-time and last-link-up-time, so '-time' is not
        // here and the handful of genuine '…-time' durations are listed in the backlog by name instead.
        private static readonly string[] DurationSuffixes =
            { "-timeout", "-interval", "-delay", "-lifetime", "timeout", "interval" };

        private static IEnumerable<(Type Entity, PropertyInfo Property, TikPropertyAttribute Attribute)> Properties()
            => typeof(tik4net.Objects.Ip.IpAddress).Assembly
                .GetTypes()
                .Where(t => t.GetCustomAttribute<TikEntityAttribute>() != null)
                .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(p => (Entity: t, Property: p, Attribute: p.GetCustomAttribute<TikPropertyAttribute>()!))
                    .Where(x => x.Attribute != null));

        private static bool Listed(Dictionary<string, string[]> list, Type entity, string wireName)
            => list.TryGetValue(entity.Name, out var names) && names.Contains(wireName);

        // ── The ratchet ───────────────────────────────────────────────────────

        [TestMethod]
        public void ANewDurationFieldIsTikDurationAndNotString()
        {
            // The failure this prevents is silent and cross-transport: a string duration compares unequal to
            // itself between the API and a CLI transport, so a caller diffing two loads sees phantom changes
            // and Save writes fields nobody touched.
            var offenders = Properties()
                .Where(x => x.Property.PropertyType == typeof(string))
                .Where(x => DurationSuffixes.Any(s => x.Attribute.FieldName.EndsWith(s, StringComparison.Ordinal)))
                .Where(x => !Listed(NotDurations, x.Entity, x.Attribute.FieldName))
                .Where(x => !Listed(PendingTikDuration, x.Entity, x.Attribute.FieldName))
                .Select(x => $"{x.Entity.Name}.{x.Property.Name} (\"{x.Attribute.FieldName}\")")
                .OrderBy(s => s)
                .ToList();

            Assert.AreEqual(0, offenders.Count,
                "these duration fields are declared 'string?' — declare them 'TikDuration?' so they read the "
                + "same over every transport (see ARCHITECTURE.md, 'Adding an entity'). If one of them is not "
                + "actually a duration — a timestamp, a hop count, an upload/download pair — add it to "
                + "NotDurations with the value the router reports:" + Environment.NewLine
                + string.Join(Environment.NewLine, offenders));
        }

        [TestMethod]
        public void TheBacklogHasNoStaleEntries()
        {
            // Without this the ratchet only turns one way on paper: a converted field left listed would go on
            // excusing the next string field that happened to share its name.
            var stale = new List<string>();

            foreach (var kv in PendingTikDuration)
                foreach (string wire in kv.Value)
                {
                    var match = Properties().FirstOrDefault(
                        x => x.Entity.Name == kv.Key && x.Attribute.FieldName == wire);

                    if (match.Property == null)
                        stale.Add($"{kv.Key}.\"{wire}\" — no such mapped field any more");
                    else if (match.Property.PropertyType != typeof(string))
                        stale.Add($"{kv.Key}.{match.Property.Name} — now {match.Property.PropertyType.Name}, "
                                  + "delete it from PendingTikDuration");
                }

            Assert.AreEqual(0, stale.Count,
                "PendingTikDuration is the remaining backlog and nothing else:" + Environment.NewLine
                + string.Join(Environment.NewLine, stale));
        }

        [TestMethod]
        public void TheNotDurationListHasNoStaleEntries()
        {
            var stale = NotDurations
                .SelectMany(kv => kv.Value.Select(wire => (Entity: kv.Key, Wire: wire)))
                .Where(e => !Properties().Any(x => x.Entity.Name == e.Entity && x.Attribute.FieldName == e.Wire))
                .Select(e => $"{e.Entity}.\"{e.Wire}\" — no such mapped field any more")
                .ToList();

            Assert.AreEqual(0, stale.Count, string.Join(Environment.NewLine, stale));
        }

        [TestMethod]
        public void TheBacklogIsSmallerThanItWas()
        {
            // A number to watch rather than a rule to satisfy. It started at 101 (against 24 already
            // converted); lower it as the backlog shrinks so the direction stays visible in the diff.
            const int WhenThisTestWasWritten = 108;

            int pending = PendingTikDuration.Sum(kv => kv.Value.Length);
            Assert.IsTrue(pending <= WhenThisTestWasWritten,
                $"the string-duration backlog grew from {WhenThisTestWasWritten} to {pending}");
        }
    }
}
