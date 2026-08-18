using System;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.Winbox
{
    /// <summary>
    /// Rides out a <see cref="TikConnectionLoginRefusedException"/> by opening again from scratch.
    /// </summary>
    /// <remarks>
    /// Measured on RouterOS 7.23.2 (P2.41): roughly one WinBox login in one to two hundred is refused
    /// with <c>"invalid user name or password (6)"</c> although the credentials are correct, and the
    /// identical request succeeds on the next attempt — every rejection recovered on the first retry,
    /// and replaying the same client key was accepted nine times out of nine. Left alone that is one
    /// spurious failure per ~400 test methods, which is what made whole suite runs look red.
    /// <para>Only that refusal is retried. A network error, a malformed frame or a mismatching
    /// confirmation digest all mean something else and are left to propagate untouched.</para>
    /// <para>It is not a WinBox mechanism despite living here: MAC-Telnet carries the same EC-SRP5
    /// handshake and is refused the same way, only reporting it as terminal text rather than as an M2
    /// error (P2.49). One router behaviour, one exception type, one retry.</para>
    /// <para><b>Credentials that really are wrong get retried too</b> — the router says exactly the same
    /// thing either way, so nothing in the answer can separate them and only persistence can. The cost
    /// is bounded and deliberate: a genuine failure takes <see cref="MaxAttempts"/> attempts and about
    /// <c>(MaxAttempts-1) × <see cref="DelayMs"/></c> ms longer, and leaves that many
    /// <c>login failure</c> lines in the router's log instead of one.</para>
    /// <para>The caller supplies a delegate that performs a <b>complete</b> open, because a refused
    /// handshake leaves the channel unusable — the retry has to build a new one, not reuse the old.</para>
    /// </remarks>
    internal static class RouterLoginRetry
    {
        /// <summary>Total attempts, including the first. Three leaves a ~1-in-8-million residual failure rate.</summary>
        internal const int MaxAttempts = 3;

        /// <summary>Pause between attempts. Every observed rejection cleared within one 50 ms gap.</summary>
        internal const int DelayMs = 100;

        /// <summary>Runs <paramref name="openOnce"/>, retrying only a transient router refusal.</summary>
        internal static void Run(Action openOnce)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    openOnce();
                    return;
                }
                catch (Exception ex) when (attempt < MaxAttempts && IsTransientRefusal(ex))
                {
                    NoteRetry(attempt, ex);
                    Thread.Sleep(DelayMs);
                }
            }
        }

        /// <summary>Async counterpart of <see cref="Run"/>.</summary>
        internal static async Task RunAsync(Func<Task> openOnce)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    await openOnce().ConfigureAwait(false);
                    return;
                }
                catch (Exception ex) when (attempt < MaxAttempts && IsTransientRefusal(ex))
                {
                    NoteRetry(attempt, ex);
                    await Task.Delay(DelayMs).ConfigureAwait(false);
                }
            }
        }

        // A retry that works is silent by construction — the caller's Open() simply succeeds — so
        // without this there is no way to tell "the router never refused" from "it refused and we
        // covered it up", and no way to notice the rate changing. It rides the existing wire trace
        // rather than a log of its own: a swallowed refusal belongs next to the frames that caused it.
        private static void NoteRetry(int attempt, Exception ex)
        {
            if (!Diagnostics.TikWireTrace.Enabled)
                return;
            string transport = Refusal(ex)?.Transport ?? "router";
            Diagnostics.TikWireTrace.Emit("login", Diagnostics.TikWireDir.Note,
                $"{transport} login refused by the router, retrying (attempt {attempt} of {MaxAttempts})");
        }

        // The refusal is wrapped by the time it gets here — TikConnectionLoginException at the connection
        // layer, and an AggregateException as well when it came out of a task — so the whole chain is
        // searched rather than the outermost type tested.
        private static bool IsTransientRefusal(Exception ex) => Refusal(ex) != null;

        private static TikConnectionLoginRefusedException? Refusal(Exception ex)
        {
            for (Exception? e = ex; e != null; e = e.InnerException)
            {
                if (e is TikConnectionLoginRefusedException refusal) return refusal;
                if (e is AggregateException aggregate)
                {
                    foreach (var inner in aggregate.InnerExceptions)
                    {
                        var found = Refusal(inner);
                        if (found != null) return found;
                    }
                }
            }
            return null;
        }
    }
}
