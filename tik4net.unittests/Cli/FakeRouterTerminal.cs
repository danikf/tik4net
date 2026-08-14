using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// A scripted RouterOS PTY peer: the CLI counterpart of <c>FakeWinboxServer</c> and
    /// <c>FakeRouterServer</c> (P2.24). It replays a <b>recorded</b> terminal byte stream and supplies the
    /// three I/O delegates <see cref="RouterOsCliLogin"/> is built around, so the login/prompt/nag state
    /// machine can be driven without a router.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why this exists: everything that machine keys on — the banner, the <c>Login:</c>/<c>Password:</c>
    /// wording, the change-password nag, the prompt shape, the refusal text — is version- and
    /// state-dependent, and we have exactly one lab router on one version. A mismatch there does not throw;
    /// it waits for the receive deadline and then reports something plausible, which is how the safe-mode
    /// prompt (P2.31) and the refusal wording (P2.24) both survived. Recording the stream turns "works on
    /// 7.23.2" into data, so a second version is a new transcript rather than another live campaign.
    /// </para>
    /// <para>
    /// The read side mirrors a real transport: accumulate what the router has emitted, ANSI-strip it with the
    /// production <see cref="VtStripper"/>, test the predicate, and when the script runs dry without
    /// satisfying it, return what accumulated — a transport hitting its receive deadline, not a hang.
    /// </para>
    /// </remarks>
    internal sealed class FakeRouterTerminal
    {
        private readonly Queue<string> _pending = new Queue<string>();
        private readonly StringBuilder _screen = new StringBuilder();

        /// <summary>Everything the client sent, in order, tagged by how it was sent.</summary>
        internal List<SentItem> Sent { get; } = new List<SentItem>();

        /// <summary>Number of times a read gave up with the predicate unmet (i.e. hit the "deadline").</summary>
        internal int DeadlineHits { get; private set; }

        /// <summary>One thing the client sent, together with what was on screen at that moment.</summary>
        internal sealed class SentItem
        {
            internal string Line;            // non-null for sendLine
            internal byte[] Bytes;           // non-null for sendBytes
            internal string ScreenBefore;    // ANSI-stripped text the client had seen when it sent this

            public override string ToString()
                => Line != null ? $"line:{Line}" : "bytes:" + BitConverter.ToString(Bytes);
        }

        /// <summary>
        /// Queues one chunk of router output. Each chunk is released by <b>one</b> read, which models the
        /// real thing closely enough for this machine: a transport reads what has arrived so far, and the
        /// router's next burst follows whatever the client sends back.
        /// </summary>
        internal FakeRouterTerminal Emits(string routerOutput)
        {
            _pending.Enqueue(routerOutput);
            return this;
        }

        /// <summary>Queues the same chunk <paramref name="count"/> times — for "the nag comes back again".</summary>
        internal FakeRouterTerminal EmitsRepeatedly(string routerOutput, int count)
        {
            for (int i = 0; i < count; i++) Emits(routerOutput);
            return this;
        }

        // ── the three delegates RouterOsCliLogin drives ───────────────────────

        internal Task<string> ReadUntilAsync(Func<string, bool> predicate, CancellationToken ct)
        {
            // A read starts from a clean screen: the transports build the accumulated text per read call,
            // so text consumed by an earlier read cannot satisfy a later predicate.
            _screen.Clear();

            while (true)
            {
                string stripped = VtStripper.StripAnsi(_screen.ToString());
                if (predicate(stripped))
                    return Task.FromResult(stripped);

                if (_pending.Count == 0)
                {
                    DeadlineHits++;
                    return Task.FromResult(stripped);   // receive deadline: hand back what arrived
                }

                _screen.Append(_pending.Dequeue());
            }
        }

        internal Task SendLineAsync(string text, CancellationToken ct)
        {
            Sent.Add(new SentItem { Line = text, ScreenBefore = CurrentScreen() });
            return Task.FromResult(0);
        }

        internal Task SendBytesAsync(byte[] bytes, CancellationToken ct)
        {
            Sent.Add(new SentItem { Bytes = bytes, ScreenBefore = CurrentScreen() });
            return Task.FromResult(0);
        }

        private string CurrentScreen() => VtStripper.StripAnsi(_screen.ToString());

        /// <summary>Runs the full interactive login against this terminal.</summary>
        internal Task LoginAsync(string user = "admin", string password = "", bool useTerminalFlags = true)
            => RouterOsCliLogin.LoginAsync(user, password, useTerminalFlags,
                ReadUntilAsync, SendLineAsync, SendBytesAsync, CancellationToken.None);

        /// <summary>Runs only the settle-to-prompt phase (the SSH / mepty path).</summary>
        internal Task ResolveToPromptAsync(bool loginPromptMeansFailure = false)
            => RouterOsCliLogin.ResolveToPromptAsync(
                ReadUntilAsync, SendBytesAsync, CancellationToken.None, loginPromptMeansFailure);
    }
}
