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
    /// <para><c>/tool torch</c> needs a different treatment entirely: its <c>as-value</c> form (with either
    /// <c>once</c> or <c>duration</c>) prints NOTHING — confirmed live. Its plain (non-<c>as-value</c>) form
    /// does redraw real rows to the terminal, but not in a way the <c>once</c>/<c>as-value</c> machinery can
    /// use — UNLESS two further torch-specific parameters are added:
    /// <list type="bullet">
    ///   <item><c>proplist=…</c> fixes the field set — the default columns omit <c>tx-packets</c>/
    ///         <c>rx-packets</c> — see <see cref="CliCommandBuilder.TorchFields"/>. RouterOS still reorders the
    ///         response to its own canonical order regardless of the requested order, so
    ///         <see cref="CliOutputParser.ParseTorchFrame"/> reads the actual order back from each frame's own
    ///         <c>Columns:</c> declaration.</item>
    ///   <item><c>freeze-frame-interval=N</c> makes torch APPEND a new, terminated <c>Columns:</c>/data block
    ///         every N seconds instead of redrawing the previous one in place over VT100 — turning the output
    ///         into the same kind of discrete, parseable snapshot the other monitors produce. Confirmed live:
    ///         <c>duration</c> must be at least <c>2×freeze-frame-interval</c>, or zero frames are flushed
    ///         before the command self-terminates (a single interval is not enough).</item>
    /// </list>
    /// The one remaining wrinkle — a resolved <c>DST-PORT</c>/<c>SRC-PORT</c> value can embed a space
    /// (<c>"23 (telnet)"</c>) — is handled by <see cref="CliOutputParser.ParseTorchFrame"/> consuming the
    /// parenthesised annotation as an extra (discarded) token rather than by whitespace-splitting alone.
    /// <see cref="Kind.FreezeFrame"/> flags <c>torch</c> for this dedicated builder/parser pair (see
    /// <see cref="CliConnectionBase"/>'s <c>TorchFreezeFrameLoop</c>).</para>
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

        // Monitors driven by the dedicated freeze-frame builder/parser pair instead of once/as-value.
        private static readonly HashSet<string> FreezeFrame =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "torch" };

        /// <summary>How a monitor verb is driven over a polling terminal transport.</summary>
        public enum Kind
        {
            /// <summary>Continuous monitor — re-issue a one-shot snapshot on a timer until cancelled
            /// (<c>monitor-traffic</c>, <c>profile</c>, <c>/system resource monitor</c>, …).</summary>
            Poll,
            /// <summary>Self-terminating command — run once, emit rows, complete (<c>ping</c>, <c>traceroute</c>).</summary>
            Once,
            /// <summary>Driven by the torch-specific <c>freeze-frame-interval</c>+<c>proplist</c> builder/parser
            /// pair instead of <c>once</c>/<c>as-value</c> — currently just <c>torch</c>.</summary>
            FreezeFrame,
        }

        /// <summary>Classifies how <paramref name="verb"/> must be driven (see <see cref="Kind"/>).</summary>
        public static Kind Classify(string verb)
        {
            if (verb != null && FreezeFrame.Contains(verb)) return Kind.FreezeFrame;
            if (verb != null && Finite.Contains(verb)) return Kind.Once;
            return Kind.Poll;
        }

        /// <summary>The snapshot modifier token to append for <paramref name="verb"/> (defaults to <c>once</c>).</summary>
        public static string SnapshotModifier(string verb)
            => verb != null && Modifiers.TryGetValue(verb, out var m) ? m : DefaultModifier;
    }
}
