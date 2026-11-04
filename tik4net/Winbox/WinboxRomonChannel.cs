using System;
using System.Linq;
using tik4net.Cli;

namespace tik4net.Winbox
{
    /// <summary>
    /// A WinBox channel to a RoMON target, carried by a session to the agent: it logs in to the agent, asks it to
    /// open a link to the target, and from then on routes every message over that link. The WinBox transports run
    /// on it unchanged — native M2 and the mepty terminal alike, over TCP or the MAC layer, whichever carrier the
    /// inner channel is.
    /// </summary>
    /// <remarks>
    /// The protocol, as measured from WinBox (<c>Docs/findings-romon.md</c> §5): one request to the agent's binary 2
    /// with command 2001, the target's RoMON id, user and password; the agent logs in to the target itself (an
    /// ordinary WinBox session over RoMON port 4) and answers with a link id. Every message for the target then
    /// carries <c>SYS_TO = [2, link, &lt;handler path&gt;]</c>, and the replies come back from
    /// <c>SYS_FROM = [2, link, …]</c>. This channel adds the prefix on the way out and removes it on the way in,
    /// so the layers above address the target's handlers exactly as on a direct session.
    /// <para>The agent decrypts the target's stream and re-encrypts it for us: it sees the session in plain form,
    /// the target's password included. The leg to the agent is as private as the inner channel.</para>
    /// </remarks>
    internal sealed class WinboxRomonChannel : IWinboxM2Channel
    {
        // The agent's message router (msg-proxy), and the command that opens a link through it.
        private const int ProxyHandler = 2;
        private const int ConnectCommand = 2001;
        private const int KeyTargetId = 4;
        private const int KeyConnectFlag = 6;
        private const int KeyTargetUser = 7;
        private const int KeyTargetPassword = 8;

        // [127,2] is the agent's RoMON settings singleton: 0x1 enabled, 0x65 its current-id (absent when disabled).
        private static readonly WinboxHandler RomonSettingsHandler = new WinboxHandler(127, 2);
        private const int KeySettingsEnabled = 0x1;
        private const int KeySettingsCurrentId = 0x65;

        // What the agent answers a connect with when it fails. 0xFE0006 carries "timeout" for an id it cannot reach
        // (RoMON switched off on the agent looks the same, hence the settings check before the connect); 0xFE0009
        // (not permitted) is the target refusing the login.
        private const int StatusTimeout = 0xFE0006;
        private const int StatusNotPermitted = 0xFE0009;

        private readonly IWinboxM2Channel _agent;
        private readonly RomonRelayTarget _target;
        private readonly string? _agentMac;
        private int _link;

        /// <param name="agent">The carrier to the agent.</param>
        /// <param name="target">The router to relay to.</param>
        /// <param name="agentMac">
        /// The agent's MAC on a MAC-layer carrier, which names an agent opened by MAC alone in the relay's messages
        /// (<see cref="CliConnectionBase.AgentName"/>); <c>null</c> on TCP.
        /// </param>
        internal WinboxRomonChannel(IWinboxM2Channel agent, RomonRelayTarget target, string? agentMac = null)
        {
            _agent = agent ?? throw new ArgumentNullException(nameof(agent));
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _agentMac = agentMac;
        }

        /// <summary>The agent's own RoMON id, read when the channel opened; <c>null</c> before.</summary>
        internal string? AgentRomonId { get; private set; }

        /// <summary>The link id the agent gave the connection; 0 before it opened.</summary>
        internal int Link => _link;

        /// <summary>
        /// Logs in to the agent with <paramref name="user"/> and <paramref name="password"/> (the agent's), then opens
        /// the link to the target with the target's credentials from the <see cref="RomonRelayTarget"/>.
        /// </summary>
        public void Open(string host, int port, string user, string password, int connectTimeoutMs, int ioTimeoutMs,
            int sendTimeoutMs = 0)
        {
            // The agent as the messages below name it: a MAC-layer agent opened by MAC alone has no host.
            string agentName = CliConnectionBase.AgentName(host, _agentMac);
            try { _agent.Open(host, port, user, password, connectTimeoutMs, ioTimeoutMs, sendTimeoutMs); }
            catch (TikConnectionLoginException ex) { throw CliConnectionBase.AgentLoginFailed(agentName, ex); }
            _agentHost = agentName;

            AgentRomonId = ReadAgentRomonId(agentName, ioTimeoutMs);
            _link = Connect(agentName, Math.Max(ioTimeoutMs, ConnectWaitMs));
            CheckLinkAnswers(agentName, Math.Min(ioTimeoutMs > 0 ? ioTimeoutMs : LinkAnswerWaitMs, LinkAnswerWaitMs));
        }

        // A link the agent has just opened is now and then silent for good — it happens while /tool romon ssh relays
        // start and end on the agent — and the first command through it would wait out its whole receive timeout. One
        // system-info read through the link tells it apart; any answer will do (WinboxRomonLinkSilentException).
        private const int LinkAnswerWaitMs = 5000;

        private void CheckLinkAnswers(string host, int timeoutMs)
        {
            try
            {
                AgentRequest(M2Message.Request(
                    new WinboxHandler(new[] { ProxyHandler, _link }.Concat(WinboxM2Protocol.SysInfo.Handler.ToArray()).ToArray()),
                    WinboxM2Protocol.SysInfo.Command, _agent.NextReqIdField()),
                    timeoutMs, host, "first request through the link to " + _target.RomonId);
            }
            catch (TikConnectionReceiveTimeoutException ex)
            {
                throw new WinboxRomonLinkSilentException("The RoMON agent " + host + " opened a link to "
                    + _target.RomonId + ", but nothing answered through it within " + timeoutMs + " ms.", ex);
            }
        }

        // The agent answers an unreachable id after 5 s; a shorter read timeout must not cut that answer off.
        private const int ConnectWaitMs = 10000;

        private string ReadAgentRomonId(string host, int timeoutMs)
        {
            byte[] reply = AgentRequest(M2Message.Request(
                RomonSettingsHandler, WinboxM2Protocol.Command.GetSingleton, _agent.NextReqIdField(),
                M2Message.U32Sys(WinboxM2Protocol.RecordKey.Flags, WinboxM2Protocol.GetAllFlags)), timeoutMs, host, "read of its RoMON settings");
            var fields = M2Message.ParseAllFields(reply);
            bool enabled = fields.TryGetValue(KeySettingsEnabled, out var e) && e.Value is bool b && b;
            byte[]? id = M2Message.ParseUserBytes(reply, KeySettingsCurrentId);
            if (!enabled || id == null || id.Length != 6)
                throw new TikRomonRelayException(TikRomonRelayFailure.RomonNotEnabledOnAgent,
                    "RoMON is not enabled on the agent " + host + " (/tool romon set enabled=yes), so it cannot reach "
                    + _target.RomonId + ".");
            return TikMacText.Format(id);
        }

        private int Connect(string host, int timeoutMs)
        {
            byte[] reply = AgentRequest(M2Message.Request(
                new WinboxHandler(ProxyHandler), ConnectCommand, _agent.NextReqIdField(),
                M2Message.BoolSys(KeyConnectFlag, true),
                M2Message.StringUser(KeyTargetPassword, _target.Password),
                M2Message.StringUser(KeyTargetUser, _target.User),
                M2Message.RawUser(KeyTargetId, ParseRomonId(_target.RomonId))), timeoutMs, host, "request to open a link to " + _target.RomonId);

            int status = M2Message.ParseSysStatus(reply);
            if (status == StatusTimeout)
                throw new TikRomonRelayException(TikRomonRelayFailure.TargetUnreachable,
                    "The RoMON agent " + host + " could not reach " + _target.RomonId + " (the agent answered '"
                    + M2Message.DescribeSysError(reply) + "'). /tool romon discover on the agent lists what it can reach.");
            if (status == StatusNotPermitted)
                throw new TikRomonRelayException(TikRomonRelayFailure.TargetRefusedLogin,
                    "The RoMON target " + _target.RomonId + " refused the login of user '" + _target.User
                    + "' (wrong user or password).");
            if (status != 0 || !M2Message.TryParseSessionId(reply, out int link))
                throw new TikRomonRelayException(TikRomonRelayFailure.TargetDidNotRespond,
                    "The RoMON agent " + host + " did not open a link to " + _target.RomonId + ": "
                    + M2Message.DescribeSysError(reply));
            return link;
        }

        // While the link opens, the agent may push frames of its own between a request and its answer: when it reaps
        // links that other sessions left behind it pushes their logouts (SYS_CMD 0xFE0014, SYS_FROM [0xFF0003, <their
        // link>], no request id) to every WinBox session of the user. The answer is the frame carrying the request's
        // own id; anything else is read past. A receive that timed out comes back null on every carrier, as does a frame
        // with no M2 data over the MAC layer. Neither is the answer, so the wait goes on to the
        // deadline, and a deadline with no answer is a timeout — not a reply lacking every field, which would read as
        // "RoMON is not enabled" or "no link", nor a socket error, which would read as a failed login.
        private byte[] AgentRequest(byte[] m2, int timeoutMs, string host, string step)
        {
            int? sent = M2Message.ParseSysReqId(m2);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            byte[]? reply = _agent.SendReceive(m2, timeoutMs);
            int readPast = 0;
            while (reply == null || (sent != null && M2Message.ParseSysReqId(reply) != sent))
            {
                if (reply != null)
                {
                    readPast++;
                    if (Diagnostics.TikWireTrace.Enabled)
                        Diagnostics.TikWireTrace.Emit("wbx.romon", Diagnostics.TikWireDir.Recv,
                            "read past during the " + step + ": " + M2Message.Describe(reply));
                }
                int left = timeoutMs - (int)watch.ElapsedMilliseconds;
                if (left <= 0)
                    throw new TikConnectionReceiveTimeoutException(TimeSpan.FromMilliseconds(timeoutMs),
                        "The RoMON agent " + host + " did not answer the " + step + " within " + timeoutMs + " ms"
                        + (readPast > 0 ? " (" + readPast + " frame(s) of its own were read past)." : "."));
                reply = _agent.Receive(left);
            }
            return reply;
        }

        private static byte[] ParseRomonId(string id)
            => id.Split(':').Select(p => Convert.ToByte(p, 16)).ToArray();

        // ── routing ──

        private byte[] Route(byte[] m2)
            => M2Message.RewriteU32ArrayField(m2, WinboxM2Protocol.SysKey.To,
                path => new[] { ProxyHandler, _link }.Concat(path).ToArray());

        private byte[]? Unroute(byte[]? m2)
            => m2 == null ? null : M2Message.RewriteU32ArrayField(m2, WinboxM2Protocol.SysKey.From,
                path => path.Length >= 2 && path[0] == ProxyHandler && path[1] == _link ? path.Skip(2).ToArray() : path);

        public void Send(byte[] m2)
        {
            if (_ended) throw RelayEnded(commandMayHaveRun: false);
            _agent.Send(Outbound(m2));
        }

        public byte[]? Receive(int timeoutMs) => Inbound(_agent.Receive(timeoutMs));

        public byte[]? SendReceive(byte[] m2, int timeoutMs)
        {
            if (_ended) throw RelayEnded(commandMayHaveRun: false);
            return Inbound(_agent.SendReceive(Outbound(m2), timeoutMs));
        }

        public byte[]? ReceiveNextFrame() => Inbound(_agent.ReceiveNextFrame());

        // ── the link ending ──

        // When the link ends — RoMON switched off on the target, the target rebooting or logging the session out —
        // the agent pushes logouts (SYS_CMD 0xFE0014, no request id): one from its msg-proxy, one naming the link
        // (SYS_FROM [0xFF0003, link]), about 5 s after the target went quiet. Nothing answers on the link after that,
        // not even once the target is back, so every waiter would otherwise sit out its whole receive timeout.
        private volatile bool _ended;
        private string? _agentHost;

        // Traced before routing, as the operation wrote it: with the replies traced in Inbound, a reply that looks
        // wrong can be held against the request it answers.
        private byte[] Outbound(byte[] m2)
        {
            if (Diagnostics.TikWireTrace.Enabled)
                Diagnostics.TikWireTrace.Emit("wbx.romon", Diagnostics.TikWireDir.Send,
                    "link " + _link + ": " + M2Message.Describe(m2));
            return Route(m2);
        }

        private byte[]? Inbound(byte[]? m2)
        {
            if (m2 != null && Diagnostics.TikWireTrace.Enabled)
                Diagnostics.TikWireTrace.Emit("wbx.romon", Diagnostics.TikWireDir.Recv,
                    "link " + _link + ": " + M2Message.Describe(m2));
            if (m2 != null && IsLinkLogout(m2))
            {
                _ended = true;
                throw RelayEnded(commandMayHaveRun: true);
            }
            return Unroute(m2);
        }

        // Only the logout naming this link ends it. The agent pushes a logout for every session of the user that ends
        // on it (SYS_FROM [<that session>], 0xFF000B = 0xFFFFFFFF) — a Telnet login closing elsewhere is one — and the
        // link answers normally after those.
        private bool IsLinkLogout(byte[] m2)
        {
            if (M2Message.ParseSysReqId(m2) != null
                || M2Message.ParseSysU32(m2, WinboxM2Protocol.SysKey.Command) != WinboxM2Protocol.Command.Logout)
                return false;
            int[]? from = M2Message.ParseU32ArrayField(m2, WinboxM2Protocol.SysKey.From);
            return from != null && from.Length == 2 && from[0] == LinkLogoutOrigin && from[1] == _link;
        }

        // What the agent puts in front of the link id in the SYS_FROM of a link's logout.
        private const int LinkLogoutOrigin = 0xFF0003;

        private TikRomonRelayEndedException RelayEnded(bool commandMayHaveRun)
            => new TikRomonRelayEndedException("The RoMON relay to " + _target.RomonId + " through " + _agentHost
                + " ended: the agent logged the link out (the target left the RoMON overlay, rebooted or ended the session)."
                + " Open a new connection to continue.", commandMayHaveRun, null);

        // ── the rest is the agent session's ──

        public bool IsEncrypted => _agent.IsEncrypted;
        public bool DataAvailable => _agent.DataAvailable;
        public bool SupportsStaleDrain => _agent.SupportsStaleDrain;
        public bool SendAbandoned => _agent.SendAbandoned;
        public bool SendStalled => _agent.SendStalled;
        public long BytesReceived => _agent.BytesReceived;
        public byte[] NextReqIdField() => _agent.NextReqIdField();
        public void StartIdleServicing() => _agent.StartIdleServicing();
        public void Dispose() => _agent.Dispose();
    }
}
