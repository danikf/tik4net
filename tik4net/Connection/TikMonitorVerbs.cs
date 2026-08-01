using System;
using System.Collections.Generic;

namespace tik4net.Connection
{
    /// <summary>
    /// The RouterOS verbs that name a <b>monitor command</b> — one whose parameters are its own INPUTS
    /// (<c>/ping address=…</c>, <c>/interface monitor-traffic interface=…</c>) rather than a filter over a
    /// table, and which produces readings rather than records.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Transport-neutral on purpose. Every transport has to answer the same question — "is this a monitor?" —
    /// and each was answering it its own way: the CLI builder by falling through to <c>print</c>, REST by not
    /// having the name on any verb list, and the native transport by asking whether the resolved M2 window
    /// happens to be a <c>query</c> window. That last one is what makes a shared list necessary rather than
    /// merely tidy: <c>/interface</c> resolves to an autorefresh window too, so "the window is a monitor" is
    /// true for an ordinary interface listing as well, and keying on it sent every <c>LoadAll</c> of that
    /// table down the monitor path (caught by the full native suite, not by the monitor tests).
    /// </para>
    /// <para>
    /// The list is deliberately explicit rather than "anything that is not <c>print</c>": the two shapes
    /// disagree about what a parameter MEANS, and guessing wrong is silent in both directions (P2.51).
    /// Transports may narrow it — a CLI connection excludes <c>torch</c>, which has no working one-shot
    /// terminal form — but none may widen it.
    /// </para>
    /// </remarks>
    internal static class TikMonitorVerbs
    {
        private static readonly HashSet<string> Verbs =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ping",
                "traceroute",
                "monitor",           // /interface/ethernet/monitor, /interface/pppoe-client/monitor, …
                "monitor-traffic",
                "torch",
                "profile",           // /tool/profile — see the note below about the table menus of that name
            };

        // NOTE on 'profile': several TABLES are also called that (/ppp/profile, /ip/hotspot/profile,
        // /ip/ipsec/profile). They never collide, because a table read arrives with its own verb —
        // '/ppp/profile/print', whose last segment is 'print'. A verb-less '/ppp/profile' would be
        // misread as the monitor, but that form does not reach the router intact on any transport today
        // (over CLI it becomes ':put [/ppp profile as-value]' → "bad command name as-value"), so the
        // collision costs one error message rather than a wrong answer. Worth remembering before adding
        // an implicit-print fallback for verb-less paths.

        // Monitors that END BY THEMSELVES once their work is done, as opposed to running until cancelled.
        // A bound (count / max-hops) shortens them but is not what makes them finite: /ping and
        // /tool/traceroute both stop of their own accord, while torch, profile and monitor-traffic run until
        // someone stops them.
        private static readonly HashSet<string> SelfTerminatingVerbs =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ping", "traceroute" };

        /// <summary>True when <paramref name="verb"/> names a monitor command.</summary>
        public static bool Contains(string verb) => verb != null && Verbs.Contains(verb);

        /// <summary>
        /// True when the command finishes on its own, so a synchronous read should wait for the router to say
        /// it is done rather than treating the first batch of readings as the whole answer.
        /// </summary>
        /// <remarks>
        /// The distinction is what a synchronous <c>ExecuteList</c> means for each shape. A self-terminating
        /// monitor's answer is everything it produces up to its own end — a traceroute to an unreachable
        /// address reports one hop in the first second and adds another every second after — whereas a
        /// continuous monitor never ends, so its answer can only be one reading. Getting this wrong is quiet:
        /// the native transport returned traceroute's FIRST hop and stopped, which is a plausible-looking
        /// answer to a caller who cannot see that four more hops were coming (P2.52).
        /// </remarks>
        public static bool SelfTerminating(string verb) => verb != null && SelfTerminatingVerbs.Contains(verb);
    }
}
