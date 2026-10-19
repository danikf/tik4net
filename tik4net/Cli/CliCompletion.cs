using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace tik4net.Cli
{
    /// <summary>
    /// Tab completion and the help key over the connection's terminal: the probes behind
    /// <see cref="ITikCliCompletion"/>, <c>ICliCompletionReaction</c> and <c>ICliHelpKey</c>, which
    /// <see cref="CliConnectionBase"/> implements by forwarding here. One per open: the settle window it learns is
    /// the link's.
    /// </summary>
    internal sealed class CliCompletion
    {
        private readonly ICliCommandHost _host;

        internal CliCompletion(ICliCommandHost host)
        {
            _host = host;
        }


        /// <summary>Tab — triggers the RouterOS terminal completion listing. Byte 0x09.</summary>
        private const byte Tab = 0x09;
        /// <summary>Ctrl-C — aborts the current input line (leaving a fresh prompt). Byte 0x03.</summary>
        private const byte CtrlC = 0x03;
        /// <summary>The least silence (no new bytes) that marks a completion listing as fully arrived (ms).</summary>
        private const int CompletionSettleQuietMs = 300;

        /// <summary>The most silence a completion waits for, however slow the link (ms).</summary>
        private const int CompletionSettleQuietMaxMs = 5000;

        // How long an answered Tab takes on this connection (ms, a running average; 0 until the first): the settle
        // window is twice it, never under CompletionSettleQuietMs, so a slow link (MAC-Telnet, RoMON) is not cut short
        // and a fast one does not wait longer than it must. Reset on every open.
        private double _completionResponseMs;

        private int CompletionQuietMs
            => (int)Math.Min(CompletionSettleQuietMaxMs, Math.Max(CompletionSettleQuietMs, 2 * _completionResponseMs));

        // One settle read, timed: a reaction that came is the link's response time, the settle window after its last byte
        // taken off. A Tab left unanswered teaches nothing.
        private string SettleTab(ICliTerminal terminal, byte[] bytes)
        {
            int quietMs = CompletionQuietMs;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string reaction = terminal.SendRawAndReadUntilQuietAsync(bytes, quietMs, CancellationToken.None).GetAwaiter().GetResult();
            if (reaction.Length > 0)
            {
                double response = Math.Max(0, sw.ElapsedMilliseconds - quietMs);
                _completionResponseMs = _completionResponseMs == 0 ? response : 0.7 * _completionResponseMs + 0.3 * response;
            }
            return reaction;
        }

        // The optional settle read for the completion probe (ICliTerminal.SendRawAndReadUntilQuietAsync): "send raw
        // bytes, then read until the output goes quiet for N ms", returning the reaction with its escape sequences — an
        // inline completion is written in cursor moves, which CliCompletionParser replays. Unlike the command and
        // control-key reads (which read up to the next shell prompt), the Tab listing does NOT end in a bare prompt —
        // RouterOS redraws the prompt with the echoed stem — so it must be read on a settle window, not a prompt match.
        // All four terminal clients have it; a terminal without it (SupportsQuietRead) makes CompleteCli report that
        // the transport does not support completion (fail-closed).

        /// <summary>What <see cref="ITikCliCompletion.CompleteCli"/> answers.</summary>
        internal IReadOnlyList<string> CompleteCli(string partialInput)
            => CompleteCliBoth(partialInput).Items.Select(i => i.Name).ToList();

        /// <summary>What <see cref="ITikCliCompletion.CompleteCliRaw"/> answers.</summary>
        internal string CompleteCliRaw(string partialInput)
            => CompleteCliBoth(partialInput).Raw.TrimEnd();

        /// <summary>
        /// Types <paramref name="partialInput"/> and the help key, F1 (<c>ESC O P</c>), reads the answer on the settle
        /// window, and clears the line with Ctrl-C. Nothing is executed. Docs/findings-cli.md §14, <i>F1</i>.
        /// </summary>
        internal string HelpCli(string partialInput)
        {
            _host.EnsureOpened();
            var terminal = _host.Terminal is { SupportsQuietRead: true } t ? t
                : throw new NotSupportedException($"The {_host.TransportName} transport cannot press the help key.");
            byte[] stem = _host.Encoding.GetBytes(partialInput);
            byte[] keys = new byte[stem.Length + F1.Length];
            Array.Copy(stem, keys, stem.Length);
            Array.Copy(F1, 0, keys, stem.Length, F1.Length);

            _host.CommandLock.Wait();
            try
            {
                _host.FireWriteRow("<help-key> " + partialInput);
                string answer = VtStripper.StripAnsi(SettleTab(terminal, keys));
                _host.FireReadRow(answer);
                try { _host.SendRawAndReadAsync(new[] { CtrlC }, CancellationToken.None).GetAwaiter().GetResult(); }
                catch { /* best-effort cleanup — the answer is already captured */ }
                return answer;
            }
            finally { _host.CommandLock.Release(); }
        }

        private static readonly byte[] F1 = { 0x1B, (byte)'O', (byte)'P' };

        internal (IReadOnlyList<CliCompletionItem> Items, string Raw) CompleteCliBoth(string partialInput)
        {
            var (first, more) = CompleteCliReaction(partialInput);
            var items = CliCompletionParser.Items(first, partialInput).ToList();
            string raw = CliCompletionParser.Clean(first, partialInput, keepWordEnd: true);
            if (more != null)
            {
                var seen = new HashSet<string>(items.Select(i => i.Name), StringComparer.Ordinal);
                items.AddRange(CliCompletionParser.Items(more, partialInput).Where(i => seen.Add(i.Name)));
                raw += "\n" + CliCompletionParser.Clean(more, partialInput);
            }
            return (items, raw);
        }

        /// <summary>
        /// Drives one Tab-completion probe and returns the terminal reaction, escape sequences included.
        /// Sequence (verified live): send <c>&lt;partialInput&gt;&lt;Tab&gt;</c> and read until the listing
        /// settles (RouterOS prints the completions then redraws <c>] &gt; &lt;stem&gt;</c> — never a bare
        /// prompt, so a prompt-based read would hang); then send <c>Ctrl-C</c> to abort the half-typed line
        /// so the session is left at a clean prompt for the next call. <c>?</c> is deliberately not used — it
        /// emits no listing over a RouterOS PTY. A listing is followed by one more Tab: RouterOS lists on it what
        /// the first left out (a menu's <c>..</c> and <c>get</c>, a command's less common arguments), and a third
        /// would repeat the first (findings-cli §14). That second listing is <c>More</c>, null when there is none.
        /// </summary>
        private (string First, string? More) CompleteCliReaction(string partialInput)
        {
            _host.EnsureOpened();
            if (partialInput == null)
                throw new ArgumentNullException(nameof(partialInput));
            var terminal = _host.Terminal is { SupportsQuietRead: true } t ? t
                : throw new NotSupportedException(
                    $"The {_host.TransportName} transport does not support interactive Tab-completion. "
                    + "Use the Telnet transport for CompleteCli / the mikrotik_cli_complete MCP tool.");

            byte[] stem = _host.Encoding.GetBytes(partialInput);
            byte[] tab = new byte[stem.Length + 1];
            Array.Copy(stem, tab, stem.Length);
            tab[stem.Length] = Tab;

            _host.CommandLock.Wait();
            try
            {
                _host.FireWriteRow("<tab-complete> " + partialInput);
                string reaction = SettleTab(terminal, tab);
                _host.FireReadRow(VtStripper.StripAnsi(reaction));

                // RouterOS 6 lists only on a SECOND Tab when the typed stem already is the candidates' common
                // prefix (`frame-types=admit-`): the first one echoes the line and nothing else. RouterOS 7 lists
                // on the first. So a first Tab that changed nothing is followed by one more, and its listing wins.
                if (CliCompletionParser.Clean(reaction, partialInput).Length == 0)
                {
                    _host.FireWriteRow("<tab-complete> <second tab>");
                    string again = SettleTab(terminal, new[] { Tab });
                    _host.FireReadRow(VtStripper.StripAnsi(again));
                    if (CliCompletionParser.Clean(again, partialInput).Length > 0)
                        reaction = again;
                }

                string? more = null;
                var listed = CliCompletionParser.Tokens(reaction, partialInput);
                if (listed.Count > 0)
                {
                    _host.FireWriteRow("<tab-complete> <more>");
                    string next = SettleTab(terminal, new[] { Tab });
                    _host.FireReadRow(VtStripper.StripAnsi(next));
                    var extra = CliCompletionParser.Tokens(next, partialInput);
                    if (extra.Count > 0 && !extra.All(listed.Contains))
                        more = next;
                }

                // Abort the half-typed line (Ctrl-C → fresh prompt). Prompt-based read returns promptly here.
                try { _host.SendRawAndReadAsync(new[] { CtrlC }, CancellationToken.None).GetAwaiter().GetResult(); }
                catch { /* best-effort cleanup — the listing is already captured */ }

                return (reaction, more);
            }
            finally { _host.CommandLock.Release(); }
        }
    }
}
