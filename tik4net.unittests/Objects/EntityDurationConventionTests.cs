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
    /// It was documented but nothing enforced it, and documentation on its own lost: 108 duration fields
    /// were still <c>string?</c> when this test was written, against 24 converted. So it is built as a
    /// <b>ratchet</b> rather than pass/fail: <see cref="PendingTikDuration"/> lists the known offenders and
    /// they are tolerated, while anything outside it fails. Converting one means deleting its line here —
    /// and the list is checked for staleness in both directions, so a converted field left in the list
    /// fails too. The backlog cannot grow and cannot silently stop shrinking.
    /// </para>
    /// <para>
    /// <b>Most of the backlog was cleared by configuring the router rather than by reading more
    /// documentation.</b> The menus were not empty; they simply had no rows, and a RouterOS field that is
    /// unset is not reported at all. Creating one row per menu and setting each field brought 56 of the
    /// remaining 71 within reach of a live measurement. The 14 still listed below each need a state this
    /// lab cannot produce, and are grouped by which one.
    /// </para>
    /// <para>
    /// <b>Two things that measurement settled, and neither was what the rule assumed.</b> First, the
    /// clock-form spelling is <b>per field, not per transport</b>: in one <c>/tool/netwatch</c> row the CLI
    /// writes <c>interval=1d00:00:00</c> and <c>packet-interval=00:00:00.100</c> while, three fields away in
    /// <c>/interface/eoip</c>, <c>arp-timeout=25s</c> is compact on both. Both kinds are converted — the
    /// type also exists to hold the words <c>none</c>/<c>auto</c>, which a compact-only field still uses —
    /// but the divergence is not the blanket rule the original wording implied. Second, the spelling must be
    /// read from the <b>raw</b> CLI: this library already normalises clock form back to compact on the way
    /// in, so probing through our own command path shows compact everywhere and proves nothing. That is how
    /// <c>/tool/netwatch</c> first looked like it did not diverge at all.
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

                // Measured: an OpenVPN server reports keepalive-timeout=60 — a bare integer of seconds —
                // and REFUSES '75s' with 'input does not match any value'. The name is the whole trap: the
                // field beside it on every other menu is a duration, this one is a count.
                ["OvpnServer"] = new[] { "keepalive-timeout" },
            };

        /// <summary>
        /// The backlog: real duration fields still declared <c>string?</c>. **Delete a line when you convert
        /// its field.** Nothing may be added.
        /// </summary>
        private static readonly Dictionary<string, string[]> PendingTikDuration =
            new Dictionary<string, string[]>
            {
                // What is left is not a matter of effort: every one of these needs a router state this lab
                // cannot reach. They fall into three groups.
                //
                // Runtime state that needs a live peer, session or client:
                ["HotspotActive"] = new[] { "idle-timeout" },
                ["IpsecActivePeers"] = new[] { "last-seen", "uptime" },
                ["IpsecInstalledSa"] = new[] { "expires-in" },
                ["OspfNeighbor"] = new[] { "adjacency", "timeout" },
                ["WifiRegistrationTable"] = new[] { "last-activity", "uptime" },
                // A read-only counter that only appears once a probe cycle has completed against a
                // reachable host (the writable threshold beside it, thr-tcp-conn-time, IS converted).
                ["ToolNetwatch"] = new[] { "tcp-connect-time" },
                //
                // Hardware or a real CAP:
                ["CapsManInterface"] = new[] { "arp-timeout" },
                ["InterfaceWifi"] = new[] { "arp-timeout" },
                //
                // The menu does not exist on this RouterOS at all — /interface/wireless answers "no such
                // command prefix", so neither entity can be checked here under any configuration:
                ["InterfaceWireless"] = new[] { "disconnect-timeout" },
                ["WirelessSecurityProfile"] = new[] { "interim-update" },
                //
                // Reported only when advertising is switched on, which needs a working hotspot:
                ["HotspotUserProfile"] = new[] { "advertise-timeout" },
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
            const int WhenThisTestWasWritten = 14;

            int pending = PendingTikDuration.Sum(kv => kv.Value.Length);
            Assert.IsTrue(pending <= WhenThisTestWasWritten,
                $"the string-duration backlog grew from {WhenThisTestWasWritten} to {pending}");
        }
    }
}
