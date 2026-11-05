using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Cli;
using tik4net.MacTelnet;
using tik4net.unittests.MacTelnet;
using tik4net.Winbox;
using tik4net.WinboxCli;

namespace tik4net.unittests.Cli
{
    /// <summary>The three terminal read engines: <c>CliPtySession</c> (Telnet, SSH), MAC-Telnet, the WinBox CLI (TCP, MAC).</summary>
    public enum TerminalCarrier
    {
        Pty,
        MacTelnet,
        WinboxCli,
    }

    /// <summary>
    /// What a scripted router writes, carrier-neutral: each line typed (a command, a raw key) starts the next
    /// <see cref="Exchange"/>, whose chunks are timed from that keystroke. A Ctrl-C and a VT100 reply start none.
    /// </summary>
    internal sealed class RouterScript
    {
        /// <summary>On screen before anything is typed, unread: a late repaint the previous read left behind.</summary>
        internal string Residue { get; set; }

        /// <summary>The router's answer to each line typed, in order.</summary>
        internal List<Exchange> Exchanges { get; } = new List<Exchange>();

        /// <summary>What the router writes when a Ctrl-C arrives; null for nothing.</summary>
        internal string OnCtrlC { get; set; }

        /// <summary>A script whose first line typed is answered with <paramref name="chunks"/> (ms after the keystroke, text).</summary>
        internal static RouterScript Answer(params (int AtMs, string Text)[] chunks)
        {
            var script = new RouterScript();
            script.Exchanges.Add(new Exchange(chunks));
            return script;
        }

        /// <summary>A script that never answers.</summary>
        internal static RouterScript Silent() => new RouterScript();

        /// <summary>The last exchange ends the session <paramref name="atMs"/> after its keystroke.</summary>
        internal RouterScript ThenEnd(int atMs)
        {
            Exchanges[Exchanges.Count - 1].EndAtMs = atMs;
            return this;
        }

        /// <summary>The last exchange ends the session together with its last chunk: one poll of the reader sees both.</summary>
        internal RouterScript EndingWithTheLastChunk()
        {
            Exchanges[Exchanges.Count - 1].EndWithLastChunk = true;
            return this;
        }

        internal RouterScript Then(params (int AtMs, string Text)[] chunks)
        {
            Exchanges.Add(new Exchange(chunks));
            return this;
        }
    }

    internal sealed class Exchange
    {
        internal Exchange((int AtMs, string Text)[] chunks) { Chunks = chunks; }
        internal (int AtMs, string Text)[] Chunks { get; }
        internal int? EndAtMs { get; set; }
        internal bool EndWithLastChunk { get; set; }
    }

    /// <summary>
    /// Plays a <see cref="RouterScript"/> on one clock: what the client types goes in through <see cref="OnInput"/>, and
    /// the carrier fake takes what is due with <see cref="TryTakeDue"/>. Thread-safe: the MAC-Telnet router thread and
    /// the client's pump use it at once.
    /// </summary>
    internal sealed class ScriptPlayer
    {
        /// <summary>One thing the router does: write <see cref="Text"/>, or end the session (<see cref="End"/>).</summary>
        internal sealed class Event
        {
            internal long DueMs;
            internal long Order;
            internal string Text;
            internal bool End;
        }

        private readonly object _gate = new object();
        private readonly RouterScript _script;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly List<Event> _pending = new List<Event>();
        private readonly List<string> _inputs = new List<string>();
        private long _order;
        private int _nextExchange;

        internal ScriptPlayer(RouterScript script, bool residueOnScreen)
        {
            _script = script;
            if (residueOnScreen && script.Residue != null)
                Schedule(0, script.Residue, end: false);
        }

        /// <summary>Everything the client typed, decoded, one entry per write.</summary>
        internal IReadOnlyList<string> Inputs { get { lock (_gate) return _inputs.ToList(); } }

        internal void OnInput(byte[] keys)
        {
            string typed = Encoding.UTF8.GetString(keys);
            lock (_gate)
            {
                _inputs.Add(typed);
                long now = _clock.ElapsedMilliseconds;
                if (typed == "\x03")
                {
                    if (_script.OnCtrlC != null)
                        Schedule(now, _script.OnCtrlC, end: false);
                    return;
                }
                if (typed.StartsWith("\x1b[", StringComparison.Ordinal))   // a VT100 reply, not a keystroke
                    return;
                if (_nextExchange >= _script.Exchanges.Count)
                    return;

                Exchange exchange = _script.Exchanges[_nextExchange++];
                long last = now;
                foreach (var (atMs, text) in exchange.Chunks)
                {
                    Schedule(now + atMs, text, end: false);
                    last = Math.Max(last, now + atMs);
                }
                if (exchange.EndWithLastChunk)
                    Schedule(last, null, end: true);
                else if (exchange.EndAtMs.HasValue)
                    Schedule(now + exchange.EndAtMs.Value, null, end: true);
            }
        }

        private void Schedule(long dueMs, string text, bool end)
        {
            _pending.Add(new Event { DueMs = dueMs, Order = _order++, Text = text, End = end });
            _pending.Sort((a, b) => a.DueMs != b.DueMs ? a.DueMs.CompareTo(b.DueMs) : a.Order.CompareTo(b.Order));
        }

        /// <summary>The next event that is due, without taking it.</summary>
        internal Event PeekDue()
        {
            lock (_gate)
                return _pending.Count > 0 && _pending[0].DueMs <= _clock.ElapsedMilliseconds ? _pending[0] : null;
        }

        internal bool TryTakeDue(out Event e)
        {
            lock (_gate)
            {
                e = _pending.Count > 0 && _pending[0].DueMs <= _clock.ElapsedMilliseconds ? _pending[0] : null;
                if (e != null) { _pending.RemoveAt(0); Taken++; }
                return e != null;
            }
        }

        /// <summary>How many events the carrier has taken: on the PTY and WinBox fakes, what the client has read.</summary>
        internal int Taken { get; private set; }

        /// <summary>Takes the next due event when it is an end scheduled for the same moment as <paramref name="data"/>.</summary>
        internal bool TryTakeEndWith(Event data)
        {
            lock (_gate)
            {
                if (_pending.Count == 0 || !_pending[0].End || _pending[0].DueMs != data.DueMs) return false;
                _pending.RemoveAt(0);
                return true;
            }
        }
    }

    /// <summary>
    /// One terminal client over a fake carrier that plays a <see cref="RouterScript"/>: the same calls on every
    /// <see cref="TerminalCarrier"/>, so one table of read cases runs against all three read engines.
    /// </summary>
    internal abstract class TerminalReadFixture : IDisposable
    {
        internal const string Prompt = "[admin@CHR2] > ";

        protected TerminalReadFixture(ScriptPlayer player) { Player = player; }

        protected ScriptPlayer Player { get; }

        /// <summary>Everything the client typed, one entry per write (VT100 replies and Ctrl-C included).</summary>
        internal IReadOnlyList<string> Inputs => Player.Inputs;

        /// <summary>What the client has read off the carrier so far (PTY and WinBox CLI; MAC-Telnet's router sends on its own).</summary>
        internal int CarrierReads => Player.Taken;

        internal abstract Task<string> CommandAsync(string command, CancellationToken ct = default);
        internal abstract Task<string> RawAsync(byte[] key, CancellationToken ct = default);
        internal abstract Task<string> QuietAsync(byte[] keys, int quietMs, CancellationToken ct = default);
        internal abstract void RequestInterrupt();

        /// <summary>The terminal's <see cref="ICliTerminal.End"/>: types <c>/quit</c> (and the <c>y</c>) and waits as its close does.</summary>
        internal abstract void End(bool answerSafeModeQuestion);

        public abstract void Dispose();

        internal static TerminalReadFixture Create(TerminalCarrier carrier, RouterScript script, int receiveTimeoutMs)
        {
            switch (carrier)
            {
                case TerminalCarrier.Pty: return new PtyFixture(script, receiveTimeoutMs);
                case TerminalCarrier.MacTelnet: return new MacTelnetFixture(script, receiveTimeoutMs);
                case TerminalCarrier.WinboxCli: return new WinboxCliFixture(script, receiveTimeoutMs);
                default: throw new ArgumentOutOfRangeException(nameof(carrier));
            }
        }

        // ── PTY: CliPtySession over a byte stream ────────────────────────────

        private sealed class PtyFixture : TerminalReadFixture
        {
            private readonly CliPtySession _session;

            internal PtyFixture(RouterScript script, int receiveTimeoutMs) : base(new ScriptPlayer(script, residueOnScreen: true))
            {
                // The close the Telnet and SSH clients give the session: end it while the carrier is up.
                _session = new CliPtySession(new PlayedByteStream(Player), Encoding.UTF8, () => receiveTimeoutMs, "SSH", "ssh.pty", "\r",
                    answer => _session.EndSession(answer));
            }

            internal override void End(bool answerSafeModeQuestion) => _session.End(answerSafeModeQuestion);

            internal override Task<string> CommandAsync(string command, CancellationToken ct = default) => _session.SendCommandAndReadAsync(command, null, ct);
            internal override Task<string> RawAsync(byte[] key, CancellationToken ct = default) => _session.SendRawAndReadAsync(key, ct);
            internal override Task<string> QuietAsync(byte[] keys, int quietMs, CancellationToken ct = default) => _session.SendRawAndReadUntilQuietAsync(keys, quietMs, ct);
            internal override void RequestInterrupt() => _session.RequestInterrupt();
            public override void Dispose() { }

            /// <summary>A chunk per <see cref="ReadAsync"/> once it is due; closed-and-drained once the end is.</summary>
            private sealed class PlayedByteStream : ICliByteStream
            {
                private readonly ScriptPlayer _player;
                private ScriptPlayer.Event _held;
                private volatile bool _ended;

                internal PlayedByteStream(ScriptPlayer player) { _player = player; }

                public bool HasData
                {
                    get
                    {
                        if (_held != null) return true;
                        while (!_ended && _player.TryTakeDue(out var e))
                        {
                            if (e.End) { _ended = true; break; }
                            _held = e;
                            return true;
                        }
                        return false;
                    }
                }

                public Task<int> ReadAsync(byte[] buffer, CancellationToken ct)
                {
                    if (!HasData) throw new InvalidOperationException("read with nothing buffered");
                    byte[] bytes = Encoding.UTF8.GetBytes(_held.Text);
                    _held = null;
                    Array.Copy(bytes, buffer, bytes.Length);
                    return Task.FromResult(bytes.Length);
                }

                public bool ClosedAndDrained => !HasData && _ended;

                public Task WriteAsync(byte[] bytes, CancellationToken ct)
                {
                    _player.OnInput(bytes);
                    return Task.CompletedTask;
                }
            }
        }

        // ── MAC-Telnet: MacTelnetUdpClient's pump over loopback UDP ───────────

        /// <summary>
        /// The router answers over <see cref="MacTelnetFakeRouter"/>, one datagram per chunk, and acknowledges each DATA
        /// packet it takes. <see cref="DropOnce"/> and <see cref="NeverAcknowledge"/> make it lose what the client typed.
        /// </summary>
        internal sealed class MacTelnetFixture : TerminalReadFixture
        {
            private readonly MacTelnetFakeRouter _router;
            private readonly Thread _thread;
            private volatile bool _stop;
            private uint _inCounter;
            private uint _highestAckFromClient;

            internal MacTelnetFixture(RouterScript script, int receiveTimeoutMs) : base(new ScriptPlayer(script, residueOnScreen: false))
            {
                _router = new MacTelnetFakeRouter(receiveTimeoutMs);
                _router.StartPump();
                _thread = new Thread(RouterLoop) { IsBackground = true, Name = "fake MAC-Telnet router" };
                _thread.Start();

                if (script.Residue != null)
                {
                    // On screen before the next keystroke. The pump acknowledges a datagram (TryAcceptData) before it
                    // appends the text to its buffer, so the acknowledgement alone does not say the text is there: a
                    // key sent in between has its buffer reset run first, and the residue then lands in its answer
                    // (seen once under a loaded test run, on X4's MAC-Telnet row). The pause covers the append.
                    _router.SendData(script.Residue);
                    var sw = Stopwatch.StartNew();
                    while (Volatile.Read(ref _highestAckFromClient) < _router.OutCounter && sw.ElapsedMilliseconds < 2000)
                        Thread.Sleep(5);
                    Thread.Sleep(50);
                }
            }

            /// <summary>The first DATA packet whose text is this is lost: neither taken nor acknowledged.</summary>
            internal string DropOnce { get; set; }

            /// <summary>Every DATA packet whose text is this is lost.</summary>
            internal string NeverAcknowledge { get; set; }

            /// <summary>How many DATA packets the router lost on purpose.</summary>
            internal int Dropped;

            internal MacTelnetUdpClient Client => _router.Client;

            internal MacTelnetFakeRouter Router => _router;

            internal override Task<string> CommandAsync(string command, CancellationToken ct = default) => Client.SendCommandAndReadAsync(command, null, ct);
            internal override Task<string> RawAsync(byte[] key, CancellationToken ct = default) => Client.SendRawAndReadAsync(key, ct);
            internal override Task<string> QuietAsync(byte[] keys, int quietMs, CancellationToken ct = default) => Client.SendRawAndReadUntilQuietAsync(keys, quietMs, ct);
            internal override void RequestInterrupt() => Client.RequestInterrupt();
            internal override void End(bool answerSafeModeQuestion) => Client.TryCloseSession(answerSafeModeQuestion);

            private void RouterLoop()
            {
                while (!_stop)
                {
                    if (_router.TryReceive(5, out byte type, out uint counter, out byte[] payload))
                        OnClientPacket(type, counter, payload);

                    while (Player.TryTakeDue(out var e))
                    {
                        if (e.End) { _router.SendEnd(); continue; }
                        // An end due with this chunk goes first, so every poll that sees the chunk also sees the end:
                        // the pump appends text after a PKT_END as it does before one.
                        if (Player.TryTakeEndWith(e)) _router.SendEnd();
                        _router.SendData(e.Text);
                    }
                }
            }

            private void OnClientPacket(byte type, uint counter, byte[] payload)
            {
                if (type == MacTelnetFakeRouter.PktAck)
                {
                    if (counter > _highestAckFromClient) Volatile.Write(ref _highestAckFromClient, counter);
                    return;
                }
                if (type != MacTelnetFakeRouter.PktData || payload.Length == 0)
                    return;

                if (counter < _inCounter) { _router.SendAck(_inCounter); return; }   // a retransmit of what was taken
                string text = Encoding.UTF8.GetString(payload);
                if (text == NeverAcknowledge) { Interlocked.Increment(ref Dropped); return; }
                if (text == DropOnce) { DropOnce = null; Interlocked.Increment(ref Dropped); return; }

                _inCounter = counter + (uint)payload.Length;
                _router.SendAck(_inCounter);
                Player.OnInput(payload);
            }

            public override void Dispose()
            {
                _stop = true;
                _thread.Join(2000);
                _router.Dispose();
            }
        }

        // ── WinBox CLI: WinboxCliClient over an M2 channel ───────────────────

        private sealed class WinboxCliFixture : TerminalReadFixture
        {
            private readonly WinboxCliClient _client;

            internal WinboxCliFixture(RouterScript script, int receiveTimeoutMs) : base(new ScriptPlayer(script, residueOnScreen: true))
            {
                _client = new WinboxCliClient(new PlayedChannel(Player), Encoding.UTF8, CliClientTimeouts.Fixed(receiveTimeoutMs, receiveTimeoutMs), "WinBox CLI", WinboxCliSession.TraceChannel);
                // The terminal the login would have opened (its frames carry no session id, so any is accepted); the close
                // types nothing into a terminal that was never opened.
                typeof(WinboxCliClient).GetField("_sessionId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .SetValue(_client, 1);
            }

            internal override Task<string> CommandAsync(string command, CancellationToken ct = default) => _client.SendCommandAndReadAsync(command, null, ct);
            internal override Task<string> RawAsync(byte[] key, CancellationToken ct = default) => _client.SendRawAndReadAsync(key, ct);
            internal override Task<string> QuietAsync(byte[] keys, int quietMs, CancellationToken ct = default) => _client.SendRawAndReadUntilQuietAsync(keys, quietMs, ct);
            internal override void RequestInterrupt() => _client.RequestInterrupt();
            internal override void End(bool answerSafeModeQuestion) => _client.TryCloseSession(answerSafeModeQuestion);
            public override void Dispose() => _client.Dispose();

            /// <summary>
            /// One mepty frame per due chunk, whether or not the client pulled (the pull rule has its own tests,
            /// <c>WinboxCliReadRuleTests</c>); the end is a <see cref="WinboxM2Protocol.Mepty.Close"/> frame, carrying
            /// the last chunk when the two are due together.
            /// </summary>
            private sealed class PlayedChannel : IWinboxM2Channel
            {
                private readonly ScriptPlayer _player;

                internal PlayedChannel(ScriptPlayer player) { _player = player; }

                public bool IsEncrypted => true;
                public bool SupportsStaleDrain => false;
                public bool SendAbandoned => false;
                public bool SendStalled => false;
                public bool DataAvailable => _player.PeekDue() != null;
                public long BytesReceived => 0;

                public void Open(string host, int port, string user, string password, int connectTimeoutMs, int ioTimeoutMs, int sendTimeoutMs = 0) { }
                public void StartIdleServicing() { }
                public byte[] NextReqIdField() => M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, 1);

                public void Send(byte[] m2)
                {
                    byte[] keys = M2Message.ParseUserBytes(m2, WinboxM2Protocol.Mepty.Key.Input);
                    if (keys != null) _player.OnInput(keys);   // a pull carries no input
                }

                public byte[] Receive(int timeoutMs)
                {
                    if (!_player.TryTakeDue(out var e)) return null;
                    if (e.End) return Frame(WinboxM2Protocol.Mepty.Close, null);
                    bool end = _player.TryTakeEndWith(e);
                    return Frame(end ? WinboxM2Protocol.Mepty.Close : WinboxM2Protocol.Mepty.Data, e.Text);
                }

                private static byte[] Frame(int command, string text)
                {
                    var fields = new List<byte[]>
                    {
                        M2Message.SysToArr(WinboxM2Protocol.Mepty.Handler), M2Message.SysFrom(),
                        M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, command),
                    };
                    if (text != null)
                        fields.Add(M2Message.RawUser(WinboxM2Protocol.Mepty.Key.Input, Encoding.UTF8.GetBytes(text)));
                    return M2Message.BuildM2(fields.ToArray());
                }

                public byte[] SendReceive(byte[] m2, int timeoutMs) { Send(m2); return Receive(timeoutMs); }
                public byte[] ReceiveNextFrame() => throw new NotSupportedException();
                public void Dispose() { }
            }
        }
    }
}
