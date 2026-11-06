using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.Cli
{
    /// <summary>
    /// What carries one RouterOS console for <see cref="CliConsole"/>: how its text arrives, how keys go out, and the
    /// carrier's own facts. Everything below the poll is the carrier's — the PTY's IAC filter, MAC-Telnet's pump and its
    /// datagram rules, the WinBox terminal's pull, byte acknowledgement and frame budget — and so are the VT100 replies,
    /// which the carrier sends as it brings the text in.
    /// </summary>
    internal interface ICliConsoleCarrier
    {
        /// <summary>"Telnet", "WinBox CLI MAC", …: named in exception texts.</summary>
        string TransportName { get; }

        /// <summary>The wire-trace channel the read's notes go to.</summary>
        string TraceChannel { get; }

        /// <summary>The console's text encoding.</summary>
        Encoding Encoding { get; }

        /// <summary>One Enter (<see cref="CliTerminalText.Enter"/>, or <see cref="CliTerminalText.TelnetEnter"/>).</summary>
        string Enter { get; }

        /// <summary>How long a prompt has to stay last before an answer is over (ms): a measured carrier fact
        /// (<c>Docs/findings-cli.md</c>, "Prompt redraw and settling").</summary>
        int SettleMs { get; }

        /// <summary>The receive deadline of one read (ms), read at each use: <see cref="ITikConnection.ReceiveTimeout"/> may
        /// change on an open connection.</summary>
        int ReceiveTimeoutMs { get; }

        /// <summary>The target's RoMON id once the console has been relayed through <c>/tool romon ssh</c>; null for a
        /// direct session and for the WinBox relay, which is M2 rather than a CLI relay.</summary>
        string? RomonTarget { get; }

        /// <summary>
        /// What happens to the residue of the previous read before a command or key: <c>false</c>, the console drains it
        /// (read on until quiet for <see cref="SettleMs"/>) when <see cref="HasData"/>; <c>true</c>, the carrier drops what
        /// it has collected (<see cref="DiscardResidue"/>), because a pump that owns the socket has already read it.
        /// </summary>
        bool DiscardsResidue { get; }

        /// <summary>Something is buffered that a poll would bring in at once: the drain's gate. Never blocks.</summary>
        bool HasData { get; }

        /// <summary>For a carrier that <see cref="DiscardsResidue"/>: what it has collected since the last read, raw.</summary>
        string PeekResidue();

        /// <summary>For a carrier that <see cref="DiscardsResidue"/>: drops it; <paramref name="beforeCommand"/> when a command
        /// line, not a key, is about to be typed.</summary>
        void DiscardResidue(bool beforeCommand);

        /// <summary>Refuses the write of <paramref name="what"/> when the router has already ended the session; does nothing
        /// on a carrier whose write finds that out itself (<see cref="WriteRefusedByClose"/>).</summary>
        void ThrowIfEnded(string what);

        /// <summary>A command line's write just failed because the carrier has closed: reported as a close found before the
        /// write rather than as the carrier's own exception.</summary>
        bool WriteRefusedByClose { get; }

        /// <summary>Writes keys as they are.</summary>
        Task SendAsync(byte[] keys, CancellationToken ct);

        /// <summary>The exception for a read that got nothing back because the router never took what was sent.</summary>
        TikConnectionSessionClosedException NotAcknowledged(string? sentCommand);

        /// <summary>Starts one read: the state a read keeps on this carrier (what it has accumulated, whether an end it sees
        /// arrived during it).</summary>
        ICliCarrierRead BeginRead(CliReadKind kind);

        /// <summary><see cref="ICliTerminal.End"/>: ends the session, answering the Safe Mode question when held, and
        /// releases the carrier. Never throws.</summary>
        void End(bool answerSafeModeQuestion);

        /// <summary><see cref="IDisposable.Dispose"/> of the terminal: releases the carrier without answering.</summary>
        void Release();
    }

    /// <summary>What a read is for, which some carriers act on: the WinBox terminal pulls only for a command.</summary>
    internal enum CliReadKind
    {
        /// <summary>A command's or a key's answer, read to its prompt.</summary>
        Command,

        /// <summary>A reaction read until it is quiet (Tab completion).</summary>
        Quiet,

        /// <summary>Residue read and dropped until it is quiet.</summary>
        Drain,
    }

    /// <summary>One read on a carrier (<see cref="ICliConsoleCarrier.BeginRead"/>).</summary>
    internal interface ICliCarrierRead
    {
        /// <summary>
        /// Brings in what has arrived since the last call and answers its VT100 probes; never waits longer than one frame
        /// already arriving. May do carrier work: the WinBox terminal pulls here while <paramref name="atPrompt"/> is false.
        /// </summary>
        /// <param name="atPrompt">The settle window is running (<see cref="CliPromptSettle.AtPrompt"/>).</param>
        /// <param name="elapsedMs">Time since the read started.</param>
        /// <param name="ct">Cancels a wait for data; never a write.</param>
        Task<CliCarrierPoll> PollAsync(bool atPrompt, long elapsedMs, CancellationToken ct);

        /// <summary>The raw text of this read so far, escapes included.</summary>
        string Text { get; }

        /// <summary>Waits out the carrier's poll interval, or less when something arrives.</summary>
        Task WaitAsync(CancellationToken ct);
    }

    /// <summary>What one <see cref="ICliCarrierRead.PollAsync"/> found.</summary>
    internal readonly struct CliCarrierPoll
    {
        internal CliCarrierPoll(bool received, bool ended, bool neverAcknowledged = false, Exception? midFrame = null)
        {
            Received = received;
            Ended = ended;
            NeverAcknowledged = neverAcknowledged;
            MidFrame = midFrame;
        }

        /// <summary>Something arrived: the settle window restarts, the drain's quiet window too.</summary>
        internal bool Received { get; }

        /// <summary>The router ended the session during this read (the carrier closed, <c>PKT_END</c>,
        /// <c>Mepty.Close</c>): no prompt will follow.</summary>
        internal bool Ended { get; }

        /// <summary>The router never took what was sent, after every retransmit: a datagram carrier only.</summary>
        internal bool NeverAcknowledged { get; }

        /// <summary>A frame that had started arriving did not finish: the stream cannot be read any further (WinBox).</summary>
        internal Exception? MidFrame { get; }
    }
}
