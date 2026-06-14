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

        internal WinboxNativeM2Operations(IWinboxM2Channel channel, int timeoutMs = 5000)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _timeoutMs = timeoutMs;
        }

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
            OnRequest?.Invoke(request);
            byte[] response = _channel.SendReceive(request, _timeoutMs);
            OnResponse?.Invoke(response);
            return response;
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
                    _channel.NextReqIdField(),
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
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), _channel.NextReqIdField(),
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
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), _channel.NextReqIdField(),
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
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), _channel.NextReqIdField(),
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
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), _channel.NextReqIdField(),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Command.Set),
                M2Message.SessionIdField(id),
            };
            if (fields != null) head.AddRange(fields);
            byte[] resp = SendReceive(M2Message.BuildM2(head.ToArray()));
            ThrowOnStatus(resp, "set", handler);
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
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), _channel.NextReqIdField(),
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
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), _channel.NextReqIdField(),
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
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), _channel.NextReqIdField(),
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
        internal Dictionary<int, Tuple<string, object>> InvokeAction(int[] handler, int cmd, int id)
        {
            var head = new List<byte[]>
            {
                M2Message.SysToArr(handler), M2Message.SysFrom(),
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), _channel.NextReqIdField(),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, cmd),
            };
            if (id >= 0) head.Add(M2Message.SessionIdField(id));
            byte[] resp = SendReceive(M2Message.BuildM2(head.ToArray()));
            ThrowOnStatus(resp, "action", handler);
            return M2Message.ParseAllFields(resp);
        }

        // ── Streaming monitor (start → poll → cancel) ────────────────────────────
        // A monitor is client-driven polling over this same request/reply channel (webfig
        // ObjectQuery/ObjectAction — NOT a server push). See _notes/winbox-native-m2-plan.md §20.

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
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), _channel.NextReqIdField(),
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
        /// Polls a monitor once: SYS_CMD <paramref name="pollCmd"/> (+ the <paramref name="id"/> when ≥ 0),
        /// following getall continuation pagination within the pass. A query window
        /// (<paramref name="isQuery"/>) returns its rows from <see cref="WinboxM2Protocol.RecordKey.Records"/>
        /// and includes the flag field; a poll-action returns a single status record (the reply's top-level
        /// fields). The <c>done</c> result reflects the router's <see cref="WinboxM2Protocol.RecordKey.Finished"/>
        /// flag (stop polling when set).
        /// </summary>
        internal (List<Dictionary<int, Tuple<string, object>>> records, bool done) PollMonitor(
            int[] handler, int pollCmd, uint? id, bool isQuery,
            int flags = WinboxM2Protocol.GetAllFlags, int maxMs = 4000)
        {
            var records = new List<Dictionary<int, Tuple<string, object>>>();
            bool done = false;
            object contToken = null;
            var sw = Stopwatch.StartNew();
            for (int round = 0; round < 256 && sw.ElapsedMilliseconds < maxMs; round++)
            {
                var head = new List<byte[]>
                {
                    M2Message.SysToArr(handler), M2Message.SysFrom(),
                    M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), _channel.NextReqIdField(),
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
                if (isQuery)
                    records.AddRange(M2Message.ParseRecords(resp, WinboxM2Protocol.RecordKey.Records));
                else
                    records.Add(fields); // poll-action: the status record is the reply's top-level fields

                if (fields.TryGetValue(WinboxM2Protocol.RecordKey.Finished, out var dt) && dt.Item2 is bool db && db)
                    done = true;
                if (status == WinboxM2Protocol.Error.ObjectNonexistent) break; // end of this getall pass
                if (!fields.TryGetValue(WinboxM2Protocol.RecordKey.Continuation, out var ct)) break;
                contToken = ct.Item2;
            }
            return (records, done);
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
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), _channel.NextReqIdField(),
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
