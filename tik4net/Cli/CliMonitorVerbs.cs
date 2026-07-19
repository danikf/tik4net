using System;
using System.Collections.Generic;

namespace tik4net.Cli
{
    /// <summary>
    /// Per-verb knowledge for turning a continuous RouterOS <c>monitor</c>-style command into a pollable
    /// one-shot snapshot over a terminal. A monitor command (e.g. <c>/interface monitor-traffic</c>,
    /// <c>/tool profile</c>) normally runs until the user presses <c>Q</c>, repainting the screen with
    /// VT100 escapes — which a strictly request/response CLI transport cannot consume. Appending a
    /// per-verb "snapshot" modifier makes the command take ONE reading and return to the prompt, so the
    /// streaming transport can poll it on a timer (see <see cref="CliConnectionBase"/> <c>RunMonitorAsync</c>).
    ///
    /// <para>The modifiers below were confirmed live against RouterOS 7.21.4 (probe results in
    /// <c>_notes/connections/findings-cli.md</c>), not guessed:
    /// <list type="bullet">
    ///   <item><c>once</c> — RouterOS's convention for most monitors (<c>monitor-traffic</c>,
    ///         <c>/system resource monitor</c>, <c>/interface ethernet monitor</c>, …); the default.</item>
    ///   <item><c>ping</c> → <c>count=1</c> (ping has no <c>once</c>).</item>
    ///   <item><c>profile</c> → <c>duration=1</c> (profile rejects <c>once</c> with "expected end of command").</item>
    /// </list>
    /// </para>
    ///
    /// <para>Some monitors are genuinely interactive-only from a client's point of view. <c>/tool torch</c>
    /// is the confirmed example: its <c>as-value</c> form (with either <c>once</c> or <c>duration</c>)
    /// prints NOTHING — but the PLAIN interactive form (no <c>as-value</c>) does redraw real rows to the
    /// terminal (confirmed live, ROS 7.21.4, at both an 80-column and a 9000-column advertised width — the
    /// wrapping is RouterOS's own fixed layout, not a terminal-width truncation artifact). It is still not
    /// usable here because the row format is not reliably machine-parseable: the <c>Columns:</c> declaration
    /// omits fields the API exposes (no <c>tx-packets</c>/<c>rx-packets</c>), a resolved <c>DST-PORT</c> value
    /// embeds a space (<c>"23 (telnet)"</c>, breaking whitespace-tokenised splitting), and column widths
    /// self-adjust per redraw frame (breaking fixed-width slicing). <see cref="Kind.InteractiveOnly"/> flags
    /// it so the transport fails with guidance (use the binary API transport's Streaming capability instead)
    /// rather than shipping a parser that would silently misparse on those edge cases.</para>
    /// </summary>
    internal static class CliMonitorVerbs
    {
        /// <summary>RouterOS's default monitor snapshot modifier, used for any verb not specially mapped.</summary>
        public const string DefaultModifier = "once";

        // verb (last path segment, lower-case) → snapshot modifier token appended before 'as-value'.
        private static readonly Dictionary<string, string> Modifiers =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "ping",    "count=1"    },
                { "profile", "duration=1" },
            };

        // Finite commands that terminate themselves (a built-in count/duration bounds them): run ONCE,
        // emit the resulting rows, then complete — exactly like the binary API's async ping/traceroute
        // (one execution → N rows → !done). They must NOT be re-polled, or the row count would multiply.
        private static readonly HashSet<string> Finite =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ping", "traceroute" };

        // Monitors that produce no as-value output even with a snapshot modifier (interactive-only).
        private static readonly HashSet<string> InteractiveOnly =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "torch" };

        /// <summary>How a monitor verb is driven over a polling terminal transport.</summary>
        public enum Kind
        {
            /// <summary>Continuous monitor — re-issue a one-shot snapshot on a timer until cancelled
            /// (<c>monitor-traffic</c>, <c>profile</c>, <c>/system resource monitor</c>, …).</summary>
            Poll,
            /// <summary>Self-terminating command — run once, emit rows, complete (<c>ping</c>, <c>traceroute</c>).</summary>
            Once,
            /// <summary>Cannot be polled over a terminal (no as-value snapshot form) — e.g. <c>torch</c>.</summary>
            InteractiveOnly,
        }

        /// <summary>Classifies how <paramref name="verb"/> must be driven (see <see cref="Kind"/>).</summary>
        public static Kind Classify(string verb)
        {
            if (verb != null && InteractiveOnly.Contains(verb)) return Kind.InteractiveOnly;
            if (verb != null && Finite.Contains(verb)) return Kind.Once;
            return Kind.Poll;
        }

        /// <summary>The snapshot modifier token to append for <paramref name="verb"/> (defaults to <c>once</c>).</summary>
        public static string SnapshotModifier(string verb)
            => verb != null && Modifiers.TryGetValue(verb, out var m) ? m : DefaultModifier;
    }
}
