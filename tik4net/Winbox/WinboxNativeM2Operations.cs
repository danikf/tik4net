using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace tik4net.Winbox
{
    /// <summary>
    /// Native (structured, non-terminal) WinBox M2 CRUD operations on top of an
    /// <see cref="IWinboxM2Channel"/>. Implements the webfig /jsproxy protocol reverse-engineered in
    /// <c>master-d53cd8ec58cb.js</c>:
    /// <list type="bullet">
    ///   <item><c>getall</c> = <see cref="WinboxM2Protocol.Command.GetAll"/>, flag field
    ///         <see cref="WinboxM2Protocol.RecordKey.Flags"/> = <see cref="WinboxM2Protocol.GetAllFlags"/>,
    ///         records returned as a message-array under <see cref="WinboxM2Protocol.RecordKey.Records"/>
    ///         (webfig <c>Mfe0002</c>), paginated via <see cref="WinboxM2Protocol.RecordKey.Continuation"/>.</item>
    ///   <item><c>get-one</c> = <see cref="WinboxM2Protocol.Command.GetOne"/> +
    ///         <see cref="WinboxM2Protocol.RecordKey.Id"/>.</item>
    /// </list>
    /// All protocol numbers live in <see cref="WinboxM2Protocol"/> (single source of truth).
    /// </summary>
    /// <remarks>
    /// A decoded record is a <c>Dictionary&lt;fieldKey, (wireTypeName, value)&gt;</c>; the
    /// <c>WinboxNativeConnection</c> translates the numeric field keys back to API field names via
    /// a <c>.jg</c>-driven resolver.
    /// </remarks>
    internal sealed class WinboxNativeM2Operations
    {
        private readonly IWinboxM2Channel _channel;
        private readonly int _timeoutMs;
        private WinboxM2Multiplexer _mux;

        internal WinboxNativeM2Operations(IWinboxM2Channel channel, int timeoutMs = 5000)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _timeoutMs = timeoutMs;
        }

        /// <summary>
        /// Switches this instance from the lockstep channel path to id-multiplexed dispatch. Called once by
        /// the owning connection <b>after</b> authentication and one-time init have finished on the lockstep
        /// path, since the multiplexer's reader loop takes exclusive ownership of the channel's read side.
        /// </summary>
        /// <remarks>
        /// This also retires <see cref="DrainStaleFrames"/> and the request-id skip loop for multiplexed
        /// connections: both exist only because a lockstep reader takes "the next frame" rather than "its
        /// own frame", and a stray frame therefore shifts every subsequent reply by one. With id dispatch a
        /// stray frame is identified and dropped by the reader loop instead.
        /// </remarks>
        internal void UseMultiplexer(WinboxM2Multiplexer mux)
        {
            _mux = mux ?? throw new ArgumentNullException(nameof(mux));
            _mux.OnUnmatchedFrame = frame => OnResponse?.Invoke(frame);
        }

        // Request ids come from the multiplexer once it owns dispatch — it is the only component that can
        // refuse an id which is still awaiting a reply.
        private byte[] NextReqIdField()
            => _mux != null ? _mux.NextReqIdField() : _channel.NextReqIdField();

        /// <summary>
        /// Request-id field for a caller that builds its own M2 message and sends it through
        /// <see cref="SendReceiveCorrelated"/> — e.g. the <c>.jg</c> catalog's mproxy file transfer, which
        /// speaks a different command set but shares this channel.
        /// </summary>
        internal byte[] NextCorrelatedReqIdField() => NextReqIdField();

        /// <summary>
        /// Sends a caller-built M2 message through the same correlated seam every other operation uses
        /// (multiplexed dispatch, or the lockstep drain + request-id skip).
        /// </summary>
        /// <remarks>
        /// Exists so nothing has to reach past this class to <see cref="IWinboxM2Channel.SendReceive"/>
        /// directly. On the MAC transports that raw call is <c>Send(); Receive();</c> with no correlation at
        /// all, and <see cref="IWinboxM2Channel.SupportsStaleDrain"/> is <c>false</c> there — so one lost or
        /// late datagram leaves the channel a frame out of step and every later reply is read off by one,
        /// which surfaces as unrelated operations failing at random later in the connection.
        /// </remarks>
        internal byte[] SendReceiveCorrelated(byte[] request) => SendReceive(request);

        /// <summary>
        /// Optional row-level trace hooks, invoked with the raw M2 bytes of each request
        /// (<see cref="OnRequest"/>) and reply (<see cref="OnResponse"/>) around every
        /// <see cref="IWinboxM2Channel.SendReceive"/>. The owning connection wires these to its
        /// <c>OnWriteRow</c>/<c>OnReadRow</c> events so the native transport participates in the same
        /// diagnostics as the API/CLI transports.
        /// </summary>
        internal Action<byte[]> OnRequest { get; set; }

        /// <inheritdoc cref="OnRequest"/>
        internal Action<byte[]> OnResponse { get; set; }

        // Single send/receive seam — fires the trace hooks around the channel round-trip so every M2
        // operation (read and write) is observable without duplicating the hook at each call site.
        private byte[] SendReceive(byte[] request)
        {
            if (_mux != null)
            {
                OnRequest?.Invoke(request);
                byte[] multiplexed = _mux.SendReceive(request, _timeoutMs);
                OnResponse?.Invoke(multiplexed);
                return multiplexed;
            }

            return LockstepSendReceive(request);
        }

        // Pre-multiplexing path: used during connect/auth/init on every transport, and for the whole lifetime
        // of any channel that cannot run a reader loop (IWinboxM2Channel.SupportsReaderLoop). Both native
        // transports multiplex after init — the MAC one since P2.42.
        private byte[] LockstepSendReceive(byte[] request)
        {
            // Discard any frame already buffered on the shared channel before issuing a synchronous
            // request. The native M2 protocol is poll-based (no unsolicited server push), so anything
            // waiting here is a leftover — typically a delayed monitor/async reply from an earlier exchange
            // (observed: a tiny frame from a foreign handler with no echoed request-id). Reading it as THIS
            // request's reply would desync getall (no records → name resolution fails → "no such item", or
            // an add stores the unbound sentinel interface handle *FFFFFFFF). The channel is shared by every
            // operation — and, with connection reuse, across tests — so this can leak far from its origin.
            DrainStaleFrames();

            OnRequest?.Invoke(request);
            byte[] response = _channel.SendReceive(request, _timeoutMs);

            // Secondary guard: if a stale frame still slips in (raced in after the drain, or on a transport
            // where draining is disabled), skip it by the echoed request-id (sys key 0xFF0006). Synchronous
            // replies echo it; stray async frames do not. Use the short drain timeout for the re-read so a
            // transport that can desync without a recoverable next frame cannot stall for the full command
            // timeout on every operation.
            int expected = RequestIdOf(request);
            for (int skip = 0; expected >= 0 && skip < 16 && RequestIdOf(response) != expected; skip++)
            {
                try { response = _channel.Receive(DrainTimeoutMs); }
                catch { break; } // no further frame available — surface what we have
            }

            OnResponse?.Invoke(response);
            return response;
        }

        /// <summary>
        /// Drains whatever is already buffered on the channel. Called once by the owning connection just
        /// before it hands the read side to a <see cref="WinboxM2Multiplexer"/>, so no frame from the
        /// lockstep init phase can survive into id-dispatched territory and be matched to an unrelated
        /// request that later reuses its id.
        /// </summary>
        internal void DrainBufferedFrames() => DrainStaleFrames();

        // Drains frames already waiting on the channel (best-effort), used to clear leaked async/monitor
        // replies before a synchronous request so they cannot be mistaken for its reply.
        private void DrainStaleFrames()
        {
            if (!_channel.SupportsStaleDrain) return;
            try
            {
                // A leftover frame is already buffered, so it reads back immediately — use a short timeout so
                // a spurious DataAvailable (e.g. a partial chunk on the wire) cannot stall each operation for
                // the full command timeout.
                for (int i = 0; i < 64 && _channel.DataAvailable; i++)
                {
                    byte[] stale = _channel.Receive(DrainTimeoutMs);
                    OnResponse?.Invoke(stale); // keep the diagnostics trace honest about discarded frames
                    if (stale == null) break;
                }
            }
            catch { /* best-effort drain */ }
        }

        private const int DrainTimeoutMs = 250;

        // Extracts the M2 request-id (sys key 0xFF0006) echoed in a built request or a reply, or -1 when absent.
        private static int RequestIdOf(byte[] m2)
        {
            if (m2 == null) return -1;
            var fields = M2Message.ParseAllFields(m2);
            return fields.TryGetValue(WinboxM2Protocol.SysKey.RequestId, out var t) && t.Item2 != null
                ? Convert.ToInt32(t.Item2) : -1;
        }

        /// <summary>
        /// Sends <c>getall</c> to <paramref name="handler"/> and returns every record's decoded
        /// field dictionary, following <see cref="WinboxM2Protocol.RecordKey.Continuation"/> pagination
        /// until the handler signals "no more".
        /// </summary>
        internal List<Dictionary<int, Tuple<string, object>>> GetAll(
            int[] handler, int flags = WinboxM2Protocol.GetAllFlags, int maxObjs = 0, int maxMs = 8000)
        {
            var records = new List<Dictionary<int, Tuple<string, object>>>();
            object contToken = null; // continuation value carried back on the next request
            var sw = Stopwatch.StartNew();
            for (int round = 0; round < 256 && sw.ElapsedMilliseconds < maxMs; round++)
            {
                var head = new List<byte[]>
                {
                    M2Message.SysToArr(handler), M2Message.SysFrom(),
                    M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true),
                    NextReqIdField(),
                    M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Command.GetAll),
                    M2Message.U32Sys(WinboxM2Protocol.RecordKey.Flags, flags),
                };
                if (maxObjs > 0) head.Add(M2Message.U32Sys(WinboxM2Protocol.RecordKey.MaxObjs, maxObjs));
                if (contToken != null) head.Add(M2Message.U32Sys(WinboxM2Protocol.RecordKey.Continuation, Convert.ToInt32(contToken)));

                byte[] resp = SendReceive(M2Message.BuildM2(head.ToArray()));

                int status = M2Message.ParseSysStatus(resp);
                if (status != WinboxM2Protocol.Error.None && status != WinboxM2Protocol.Error.ObjectNonexistent)
                {
                    var ef = M2Message.ParseAllFields(resp);
                    string errStr = ef.TryGetValue(WinboxM2Protocol.SysKey.ErrorString, out var es) ? es.Item2?.ToString() : null;
                    throw new WinboxM2OperationException(status, errStr, "getall", handler);
                }

                records.AddRange(M2Message.ParseRecords(resp, WinboxM2Protocol.RecordKey.Records));

                var fields = M2Message.ParseAllFields(resp);
                // The handler signals "no more rows" with ObjectNonexistent in the error slot.
                if (status == WinboxM2Protocol.Error.ObjectNonexistent) break;
                if (!fields.TryGetValue(WinboxM2Protocol.RecordKey.Continuation, out var ct)) break;
                contToken = ct.Item2;
            }
            return records;
        }

        /// <summary>
        /// Reads the RouterOS version string from the system-info singleton
        /// (<see cref="WinboxM2Protocol.SysInfo.Handler"/> cmd=<see cref="WinboxM2Protocol.SysInfo.Command"/>),
        /// e.g. "7.21.4". Returns <c>null</c> when the field is absent.
        /// </summary>
        internal string GetRouterVersion()
        {
            byte[] msg = M2Message.BuildM2(
                M2Message.SysToArr(WinboxM2Protocol.SysInfo.Handler), M2Message.SysFrom(),
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), NextReqIdField(),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.SysInfo.Command));
            byte[] resp = SendReceive(msg);
            var fields = M2Message.ParseAllFields(resp);
            return fields.TryGetValue(WinboxM2Protocol.RecordKey.SysInfoVersion, out var t) ? t.Item2?.ToString() : null;
        }

        /// <summary>
        /// Sends <c>get-one</c> (<see cref="WinboxM2Protocol.Command.GetOne"/> +
        /// <see cref="WinboxM2Protocol.RecordKey.Id"/>=<paramref name="id"/>) and returns the record's
        /// decoded field dictionary. Records arrive under <see cref="WinboxM2Protocol.RecordKey.Records"/>;
        /// falls back to the top-level fields when the handler answers inline.
        /// </summary>
        internal Dictionary<int, Tuple<string, object>> GetOne(int[] handler, int id)
        {
            byte[] msg = M2Message.BuildM2(
                M2Message.SysToArr(handler), M2Message.SysFrom(),
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), NextReqIdField(),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Command.GetOne),
                M2Message.SessionIdField(id));
            byte[] resp = SendReceive(msg);
            var recs = M2Message.ParseRecords(resp, WinboxM2Protocol.RecordKey.Records);
            return recs.Count > 0 ? recs[0] : M2Message.ParseAllFields(resp);
        }

        /// <summary>
        /// Sends <c>get-singleton</c> (<see cref="WinboxM2Protocol.Command.GetSingleton"/> +
        /// <see cref="WinboxM2Protocol.RecordKey.Flags"/>) to a singleton (<c>type:'item'</c>) handler such
        /// as <c>/system/resource</c> or <c>/ip/dns</c>, and returns its single decoded record. Records may
        /// arrive under <see cref="WinboxM2Protocol.RecordKey.Records"/> or inline at the top level.
        /// </summary>
        internal Dictionary<int, Tuple<string, object>> GetSingleton(
            int[] handler, int flags = WinboxM2Protocol.GetAllFlags)
        {
            byte[] msg = M2Message.BuildM2(
                M2Message.SysToArr(handler), M2Message.SysFrom(),
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), NextReqIdField(),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Command.GetSingleton),
                M2Message.U32Sys(WinboxM2Protocol.RecordKey.Flags, flags));
            byte[] resp = SendReceive(msg);
            int status = M2Message.ParseSysStatus(resp);
            if (status != WinboxM2Protocol.Error.None && status != WinboxM2Protocol.Error.ObjectNonexistent)
            {
                var ef = M2Message.ParseAllFields(resp);
                string errStr = ef.TryGetValue(WinboxM2Protocol.SysKey.ErrorString, out var es) ? es.Item2?.ToString() : null;
                throw new WinboxM2OperationException(status, errStr, "get-singleton", handler);
            }
            var recs = M2Message.ParseRecords(resp, WinboxM2Protocol.RecordKey.Records);
            return recs.Count > 0 ? recs[0] : M2Message.ParseAllFields(resp);
        }

        // ── Writes ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Sends <c>set</c> (<see cref="WinboxM2Protocol.Command.Set"/> +
        /// <see cref="WinboxM2Protocol.RecordKey.Id"/>=<paramref name="id"/> + changed fields).
        /// Mirrors webfig <c>ObjectMap.setObject</c> ("edit = .id + changed fields"). Throws when the
        /// router reports a non-zero status.
        /// </summary>
        internal void Set(int[] handler, int id, IList<byte[]> fields)
        {
            var head = new List<byte[]>
            {
                M2Message.SysToArr(handler), M2Message.SysFrom(),
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), NextReqIdField(),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Command.Set),
                M2Message.SessionIdField(id),
            };
            if (fields != null) head.AddRange(fields);
            byte[] resp = SendReceive(M2Message.BuildM2(head.ToArray()));
            ThrowOnStatus(resp, "set", handler);
        }

        /// <summary>
        /// Sends <c>set-singleton</c> (<see cref="WinboxM2Protocol.Command.SetSingleton"/> + changed fields)
        /// to a singleton (<c>type:'item'</c>) handler such as <c>/system/identity</c> or <c>/ip/dns</c>.
        /// A singleton holds no record list, so there is normally no <c>.id</c> to address —
        /// <paramref name="id"/> is sent only when non-negative, matching webfig's
        /// <c>ObjectHolder.setObject</c> (<c>uff0007 = setcmd || 0xfe000e</c>, and <c>ufe0001</c> forwarded
        /// only <c>if ("ufe0001" in obj)</c>, which is how the hidden 'Change Password' holder targets a user).
        /// </summary>
        internal void SetSingleton(int[] handler, IList<byte[]> fields, int id = -1)
        {
            var head = new List<byte[]>
            {
                M2Message.SysToArr(handler), M2Message.SysFrom(),
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), NextReqIdField(),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Command.SetSingleton),
            };
            if (id >= 0) head.Add(M2Message.SessionIdField(id));
            if (fields != null) head.AddRange(fields);
            byte[] resp = SendReceive(M2Message.BuildM2(head.ToArray()));
            ThrowOnStatus(resp, "set-singleton", handler);
        }

        /// <summary>
        /// Sends <c>add</c> (<see cref="WinboxM2Protocol.Command.Add"/> + fields, no .id) and returns the
        /// new record's M2 id (reply field <see cref="WinboxM2Protocol.RecordKey.Id"/>), or <c>-1</c> if
        /// the reply carries no id.
        /// </summary>
        internal int Add(int[] handler, IList<byte[]> fields)
        {
            var head = new List<byte[]>
            {
                M2Message.SysToArr(handler), M2Message.SysFrom(),
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), NextReqIdField(),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Command.Add),
            };
            if (fields != null) head.AddRange(fields);
            byte[] resp = SendReceive(M2Message.BuildM2(head.ToArray()));
            ThrowOnStatus(resp, "add", handler);
            var f = M2Message.ParseAllFields(resp);
            return f.TryGetValue(WinboxM2Protocol.RecordKey.Id, out var t) && t.Item2 != null ? Convert.ToInt32(t.Item2) : -1;
        }

        /// <summary>
        /// Sends <c>remove</c> (<see cref="WinboxM2Protocol.Command.Remove"/> +
        /// <see cref="WinboxM2Protocol.RecordKey.Id"/>=<paramref name="id"/>).
        /// </summary>
        internal void Remove(int[] handler, int id)
        {
            byte[] msg = M2Message.BuildM2(
                M2Message.SysToArr(handler), M2Message.SysFrom(),
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), NextReqIdField(),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Command.Remove),
                M2Message.SessionIdField(id));
            byte[] resp = SendReceive(msg);
            ThrowOnStatus(resp, "remove", handler);
        }

        /// <summary>
        /// Sends <c>move</c> (<see cref="WinboxM2Protocol.Command.Move"/> +
        /// <see cref="WinboxM2Protocol.RecordKey.Id"/>=<paramref name="id"/> +
        /// <see cref="WinboxM2Protocol.RecordKey.NextId"/>=<paramref name="destNextId"/>). A negative
        /// <paramref name="destNextId"/> (move to end) omits the next-id field.
        /// </summary>
        internal void Move(int[] handler, int id, int destNextId)
        {
            var head = new List<byte[]>
            {
                M2Message.SysToArr(handler), M2Message.SysFrom(),
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), NextReqIdField(),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Command.Move),
                M2Message.SessionIdField(id),
            };
            if (destNextId >= 0) head.Add(M2Message.U32Sys(WinboxM2Protocol.RecordKey.NextId, destNextId));
            byte[] resp = SendReceive(M2Message.BuildM2(head.ToArray()));
            ThrowOnStatus(resp, "move", handler);
        }

        /// <summary>
        /// Invokes a per-record action verb (<paramref name="cmd"/>, a <c>.jg</c> doit/action SYS_CMD such as
        /// the scripts window's "Run Script" cmd:1) on <paramref name="handler"/>, optionally targeting record
        /// <paramref name="id"/> (omitted when negative). Returns the reply's top-level fields. Throws on a
        /// non-zero router status.
        /// </summary>
        internal Dictionary<int, Tuple<string, object>> InvokeAction(int[] handler, int cmd, int id,
            IList<byte[]> fields = null)
        {
            var head = new List<byte[]>
            {
                M2Message.SysToArr(handler), M2Message.SysFrom(),
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), NextReqIdField(),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, cmd),
            };
            if (id >= 0) head.Add(M2Message.SessionIdField(id));
            // A standalone action window (e.g. Wake on LAN) carries its inputs as ordinary record fields on
            // the action message itself — there is no record to attach them to.
            if (fields != null) head.AddRange(fields);
            byte[] resp = SendReceive(M2Message.BuildM2(head.ToArray()));
            ThrowOnStatus(resp, "action", handler);
            return M2Message.ParseAllFields(resp);
        }

        // ── Safe Mode (system handler [17]) ──────────────────────────────────────

        /// <summary>
        /// Takes Safe Mode (webfig <c>toggleSafeMode</c> enable branch: handler <see cref="WinboxM2Protocol.SafeMode.Handler"/>,
        /// SYS_CMD <see cref="WinboxM2Protocol.SafeMode.Take"/>). Returns the safe-mode id the router assigns
        /// (reply <see cref="WinboxM2Protocol.RecordKey.Id"/>), which must be sent back on
        /// <see cref="SafeModeRelease"/>. While held, dropping the M2 channel reverts the changes.
        /// </summary>
        internal uint SafeModeTake()
        {
            byte[] msg = M2Message.BuildM2(
                M2Message.SysToArr(WinboxM2Protocol.SafeMode.Handler), M2Message.SysFrom(),
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), NextReqIdField(),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.SafeMode.Take));
            byte[] resp = SendReceive(msg);
            ThrowOnStatus(resp, "safe-mode-take", WinboxM2Protocol.SafeMode.Handler);
            var f = M2Message.ParseAllFields(resp);
            return f.TryGetValue(WinboxM2Protocol.RecordKey.Id, out var t) && t.Item2 != null
                ? Convert.ToUInt32(t.Item2) : 0u;
        }

        /// <summary>
        /// Releases/commits Safe Mode (webfig <c>toggleSafeMode</c> disable branch: SYS_CMD
        /// <see cref="WinboxM2Protocol.SafeMode.Release"/> + the held <paramref name="safeModeId"/> in
        /// <see cref="WinboxM2Protocol.RecordKey.Id"/>). The changes become permanent.
        /// </summary>
        internal void SafeModeRelease(uint safeModeId)
        {
            byte[] msg = M2Message.BuildM2(
                M2Message.SysToArr(WinboxM2Protocol.SafeMode.Handler), M2Message.SysFrom(),
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), NextReqIdField(),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.SafeMode.Release),
                M2Message.SessionIdField(safeModeId));
            byte[] resp = SendReceive(msg);
            ThrowOnStatus(resp, "safe-mode-release", WinboxM2Protocol.SafeMode.Handler);
        }

        // ── Streaming monitor (start → poll → cancel) ────────────────────────────
        // A monitor is client-driven polling over this same request/reply channel (webfig
        // ObjectQuery/ObjectAction — NOT a server push). See Docs/winbox-native-m2-protocol.md §20.

        /// <summary>
        /// Starts a monitor: SYS_CMD <paramref name="startCmd"/> (+ optional <paramref name="requestFields"/>).
        /// Returns the monitor's <c>.id</c> handle (<see cref="WinboxM2Protocol.RecordKey.Id"/>) as a <c>uint</c>
        /// — it is a true u32 and can exceed <see cref="int.MaxValue"/> (the Profile monitor echoes 0xFFFFFFFD) —
        /// or <c>null</c> when the reply carries no id (a path-scoped monitor polled without an id).
        /// </summary>
        internal uint? StartMonitor(int[] handler, int startCmd, IList<byte[]> requestFields)
        {
            var head = new List<byte[]>
            {
                M2Message.SysToArr(handler), M2Message.SysFrom(),
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), NextReqIdField(),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, startCmd),
            };
            if (requestFields != null) head.AddRange(requestFields);
            byte[] resp = SendReceive(M2Message.BuildM2(head.ToArray()));
            ThrowOnStatus(resp, "monitor-start", handler);
            var f = M2Message.ParseAllFields(resp);
            return f.TryGetValue(WinboxM2Protocol.RecordKey.Id, out var t) && t.Item2 != null
                ? (uint?)Convert.ToUInt32(t.Item2) : null;
        }

        /// <summary>
        /// Issues ONE monitor round: SYS_CMD <paramref name="pollCmd"/> (+ the <paramref name="id"/> when ≥ 0,
        /// + <paramref name="contToken"/> when continuing a getall pass). A query window
        /// (<paramref name="isQuery"/>) returns its rows from <see cref="WinboxM2Protocol.RecordKey.Records"/>
        /// and includes the flag field; a poll-action returns a single status record (the reply's top-level
        /// fields).
        /// </summary>
        /// <returns>
        /// <c>done</c> — the router's <see cref="WinboxM2Protocol.RecordKey.Finished"/> flag: the monitored
        /// operation is over for good, stop. <c>continuation</c> — the token to pass back to continue the same
        /// pass, or <c>null</c> when this pass has ended (no token, or the
        /// <see cref="WinboxM2Protocol.Error.ObjectNonexistent"/> terminator).
        /// </returns>
        /// <remarks>
        /// <para>
        /// This deliberately runs ONE request/reply and lets the caller drive the pass — see
        /// <c>WinboxNativeConnection.MonitorLoop</c>. It used to loop internally under a 4-second /
        /// 256-round budget, which was wrong for the shape a <c>type:'query'</c> window actually has: the
        /// router answers a getall on such a window with ONE record plus a continuation token, and BLOCKS the
        /// next continuation until the next record exists. A 30-second ping is therefore a single pass of 30
        /// round trips a second apart, not a page-able snapshot. The budget cut that pass off after 5 records
        /// and threw the cursor away; the next poll started a fresh getall, which the router answered
        /// <c>ObjectNonexistent</c> ("no more rows") from then on — the monitor went silent with no error and
        /// no completion (P2.45). Measured on 7.23.2: with <c>count=3</c> the third reply carries
        /// <c>Finished</c> and the stream ends cleanly; with <c>count=30</c> it never got that far.
        /// </para>
        /// <para>
        /// Keeping the round here also keeps the connection's command gate per round rather than per pass,
        /// which is what makes a long monitor coexist with CRUD on other threads.
        /// </para>
        /// </remarks>
        internal (List<Dictionary<int, Tuple<string, object>>> records, bool done, object continuation) PollMonitorRound(
            int[] handler, int pollCmd, uint? id, bool isQuery, object contToken,
            int flags = WinboxM2Protocol.GetAllFlags)
        {
            var head = new List<byte[]>
            {
                M2Message.SysToArr(handler), M2Message.SysFrom(),
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), NextReqIdField(),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, pollCmd),
            };
            if (id.HasValue) head.Add(M2Message.SessionIdField(id.Value));
            if (isQuery) head.Add(M2Message.U32Sys(WinboxM2Protocol.RecordKey.Flags, flags));
            if (contToken != null) head.Add(M2Message.U32Sys(WinboxM2Protocol.RecordKey.Continuation, Convert.ToInt32(contToken)));

            byte[] resp = SendReceive(M2Message.BuildM2(head.ToArray()));
            int status = M2Message.ParseSysStatus(resp);
            if (status != WinboxM2Protocol.Error.None && status != WinboxM2Protocol.Error.ObjectNonexistent)
            {
                var ef = M2Message.ParseAllFields(resp);
                string errStr = ef.TryGetValue(WinboxM2Protocol.SysKey.ErrorString, out var es) ? es.Item2?.ToString() : null;
                throw new WinboxM2OperationException(status, errStr, "monitor-poll", handler);
            }

            var fields = M2Message.ParseAllFields(resp);
            var records = isQuery
                ? M2Message.ParseRecords(resp, WinboxM2Protocol.RecordKey.Records)
                : new List<Dictionary<int, Tuple<string, object>>> { fields }; // poll-action: the reply's top-level fields ARE the status record

            bool done = fields.TryGetValue(WinboxM2Protocol.RecordKey.Finished, out var dt)
                        && dt.Item2 is bool db && db;

            object continuation = null;
            // Pagination is a getall concept: only a query window continues a pass. A poll-action's reply is
            // one status record and webfig's ObjectAction never continues it — following a token there would
            // spin the round trip instead of waiting out the autorefresh interval.
            if (isQuery
                && status != WinboxM2Protocol.Error.ObjectNonexistent
                && fields.TryGetValue(WinboxM2Protocol.RecordKey.Continuation, out var ct))
                continuation = ct.Item2;

            return (records, done, continuation);
        }

        /// <summary>
        /// Cancels a monitor: SYS_CMD <paramref name="cancelCmd"/> (+ the <paramref name="id"/> when ≥ 0).
        /// Best-effort — a failed cancel (the monitor already ended) is swallowed.
        /// </summary>
        internal void CancelMonitor(int[] handler, int cancelCmd, uint? id)
        {
            var head = new List<byte[]>
            {
                M2Message.SysToArr(handler), M2Message.SysFrom(),
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), NextReqIdField(),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, cancelCmd),
            };
            if (id.HasValue) head.Add(M2Message.SessionIdField(id.Value));
            try { SendReceive(M2Message.BuildM2(head.ToArray())); }
            catch { /* cancel is best-effort */ }
        }

        private static void ThrowOnStatus(byte[] resp, string op, int[] handler)
        {
            int status = M2Message.ParseSysStatus(resp);
            if (status == WinboxM2Protocol.Error.None) return;
            var fields = M2Message.ParseAllFields(resp);
            string errStr = fields.TryGetValue(WinboxM2Protocol.SysKey.ErrorString, out var es)
                ? es.Item2?.ToString() : null;
            throw new WinboxM2OperationException(status, errStr, op, handler);
        }
    }
}
