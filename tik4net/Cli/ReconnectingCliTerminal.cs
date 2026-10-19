using System;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Connection;
using tik4net.Diagnostics;

namespace tik4net.Cli
{
    /// <summary>
    /// A console the router may end underneath us (the MAC layer logs an idle session out after ~30 s). A command is
    /// re-run on a fresh session when that is safe: never while Safe Mode is held (the drop IS the rollback), never after
    /// a streamed line reached the caller (it would be delivered twice), never for a keystroke (it acts on a console state
    /// the new session never saw).
    /// </summary>
    internal sealed class ReconnectingCliTerminal : ICliTerminal
    {
        private readonly Func<CancellationToken, Task> _firstLogin;
        private readonly Func<CliConnectionBase.CliTerminalOpen> _attempt;
        private readonly Func<bool> _mayRebuild;
        private readonly string _traceChannel;
        private readonly Func<ICliTerminal, string, string, string>? _vet;

        // The session in use: the first one, then each rebuilt one. Replaced only by a command, under the connection's
        // command lock; End and Dispose read it from Close.
        private ICliTerminal _current;

        /// <param name="first">The first session and its login (<see cref="Login"/> runs it).</param>
        /// <param name="attempt">Builds a new session and its login, for a rebuild.</param>
        /// <param name="mayRebuild">Whether a dropped session may be rebuilt now (<c>SessionRebuildAllowed</c>).</param>
        /// <param name="traceChannel">The wire-trace channel the rebuild is marked on.</param>
        /// <param name="vet">Checks a non-streamed answer (session, command, answer) and returns it, or throws.</param>
        internal ReconnectingCliTerminal(CliConnectionBase.CliTerminalOpen first, Func<CliConnectionBase.CliTerminalOpen> attempt,
            Func<bool> mayRebuild, string traceChannel, Func<ICliTerminal, string, string, string>? vet = null)
        {
            _current = first.Terminal;
            _firstLogin = first.Login;
            _attempt = attempt;
            _mayRebuild = mayRebuild;
            _traceChannel = traceChannel;
            _vet = vet;
        }

        /// <summary>The first session's login: what the open awaits.</summary>
        internal Task Login(CancellationToken ct) => _firstLogin(ct);

        public async Task<string> SendCommandAndReadAsync(string command, Action<string>? onLine, CancellationToken ct)
        {
            // A rebuild re-runs the command from the start, so it is safe only while nothing has reached the caller:
            // once a streamed line has, re-running would deliver the same lines twice — a silently wrong stream.
            bool delivered = false;
            Action<string>? track = onLine == null ? null : new Action<string>(line => { delivered = true; onLine(line); });
            try
            {
                return Vetted(command, onLine, await _current.SendCommandAndReadAsync(command, track, ct).ConfigureAwait(false));
            }
            catch (TikConnectionSessionClosedException) when (_mayRebuild() && !delivered)
            {
                await RebuildAsync(ct).ConfigureAwait(false);
                return Vetted(command, onLine, await _current.SendCommandAndReadAsync(command, track, ct).ConfigureAwait(false));
            }
        }

        private string Vetted(string command, Action<string>? onLine, string answer)
            => onLine == null && _vet != null ? _vet(_current, command, answer) : answer;

        // The re-login goes through TikOpenRetry for the same reason the open does: RouterOS refuses about 1 % of
        // logins with correct credentials, and a recovery path that inherits that failure rate is worse than none. A
        // refused login leaves its carrier unusable, so each attempt builds a new session, and disposing the current one
        // at the top of an attempt closes the dropped session on the first and a refused one on the next.
        private async Task RebuildAsync(CancellationToken ct)
        {
            // Marked in the trace because the rebuild is otherwise invisible: it turns a dropped session into a slow
            // success.
            if (TikWireTrace.Enabled)
                TikWireTrace.Emit(_traceChannel, TikWireDir.Note, "SESSION DROPPED BY ROUTER — reopening");

            await TikOpenRetry.RunAsync(async () =>
            {
                try { _current.Dispose(); } catch { /* the old session is gone anyway */ }
                var next = _attempt();
                _current = next.Terminal;
                await next.Login(ct).ConfigureAwait(false);
            }, TikOpenRetry.IsTransientRefusal).ConfigureAwait(false);
        }

        // Keystrokes (Safe Mode, Tab completion) are never re-sent: a key acts on a console state, and a fresh session
        // never saw what came before it.
        public Task<string> SendRawAndReadAsync(byte[] raw, CancellationToken ct) => _current.SendRawAndReadAsync(raw, ct);

        public Task<string> SendRawAndReadUntilQuietAsync(byte[] raw, int quietMs, CancellationToken ct)
            => _current.SendRawAndReadUntilQuietAsync(raw, quietMs, ct);

        public bool SupportsQuietRead => _current.SupportsQuietRead;

        public bool SupportsLineStreaming => _current.SupportsLineStreaming;

        public void End(bool answerSafeModeQuestion) => _current.End(answerSafeModeQuestion);

        public void Dispose() => _current.Dispose();
    }
}
