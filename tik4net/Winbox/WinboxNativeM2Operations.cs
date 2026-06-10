using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace tik4net.Winbox
{
    /// <summary>
    /// Native (structured, non-terminal) WinBox M2 CRUD operations on top of an
    /// <see cref="IWinboxM2Channel"/>. Implements the read side of the webfig /jsproxy protocol
    /// reverse-engineered in <c>master-d53cd8ec58cb.js</c>:
    /// <list type="bullet">
    ///   <item><c>getall</c> = command <c>0xFE0004</c>, flag field <c>0xFE000C</c> = <c>0x10000005</c>,
    ///         records returned as a message-array under key <c>0xFE0002</c> (webfig <c>Mfe0002</c>),
    ///         paginated via continuation token <c>0xFE0003</c>.</item>
    ///   <item><c>get-one</c> = command <c>0xFE0002</c> + record id field <c>0xFE0001</c>.</item>
    /// </list>
    /// The write commands (set/add/remove/move) are declared here as constants but are Phase F2 — not
    /// implemented in F1.
    /// </summary>
    /// <remarks>
    /// A decoded record is a <c>Dictionary&lt;fieldKey, (wireTypeName, value)&gt;</c>; the
    /// <c>WinboxNativeConnection</c> translates the numeric field keys back to API field names via
    /// a <c>.jg</c>-driven resolver.
    /// </remarks>
    internal sealed class WinboxNativeM2Operations
    {
        // ── Command catalog (uff0007 / 0xFF0007), from webfig ─────────────────
        internal const int CMD_GETALL = 0xFE0004; // list all
        internal const int CMD_GETONE = 0xFE0002; // get one by .id
        internal const int CMD_SET    = 0xFE0003; // set/change (F2)
        internal const int CMD_ADD    = 0xFE0005; // add (F2)
        internal const int CMD_REMOVE = 0xFE0006; // remove (F2)
        internal const int CMD_MOVE   = 0xFE0007; // move (ordered) (F2)

        // ── Well-known system field keys ──────────────────────────────────────
        internal const int KEY_RECORDS = 0xFE0002; // Mfe0002 — record message-array
        internal const int KEY_ID      = 0xFE0001; // ufe0001 — record .id
        internal const int KEY_FLAGS   = 0xFE000C; // ufe000c — getall/get flags
        internal const int KEY_MAXOBJS = 0xFE0018; // ufe0018 — getall maxobjs
        internal const int KEY_CONT    = 0xFE0003; // ufe0003 — getall continuation token
        internal const int KEY_NEXTID  = 0xFE0005; // ufe0005 — move destination next-id
        internal const int KEY_COMMENT = 0xFE0009; // sfe0009 — comment (types.comment)
        internal const int KEY_NAME    = 0x10006;  // s10006  — Name

        // getall base flags (webfig: refetchonopen | refreshfilter). Without this the handler
        // returns no rows.
        internal const int GETALL_FLAGS = 0x10000005;

        // error code returned as a terminator at the end of getall pagination
        private const int ERR_OBJ_NONEXISTANT = 0xFE0004;

        private readonly IWinboxM2Channel _channel;
        private readonly int _timeoutMs;

        internal WinboxNativeM2Operations(IWinboxM2Channel channel, int timeoutMs = 5000)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _timeoutMs = timeoutMs;
        }

        /// <summary>
        /// Sends <c>getall</c> to <paramref name="handler"/> and returns every record's decoded
        /// field dictionary, following <c>0xFE0003</c> pagination until the handler signals "no more".
        /// </summary>
        internal List<Dictionary<int, Tuple<string, object>>> GetAll(
            int[] handler, int flags = GETALL_FLAGS, int maxObjs = 0, int maxMs = 8000)
        {
            var records = new List<Dictionary<int, Tuple<string, object>>>();
            object contToken = null; // 0xFE0003 value carried back on the next request
            var sw = Stopwatch.StartNew();
            for (int round = 0; round < 256 && sw.ElapsedMilliseconds < maxMs; round++)
            {
                var head = new List<byte[]>
                {
                    M2Message.SysToArr(handler), M2Message.SysFrom(),
                    M2Message.BoolSys(0xFF0005, true),    // reply expected
                    _channel.NextReqIdField(),
                    M2Message.U32Sys(0xFF0007, CMD_GETALL),
                    M2Message.U32Sys(KEY_FLAGS, flags),
                };
                if (maxObjs > 0) head.Add(M2Message.U32Sys(KEY_MAXOBJS, maxObjs));
                if (contToken != null) head.Add(M2Message.U32Sys(KEY_CONT, Convert.ToInt32(contToken)));

                byte[] resp = _channel.SendReceive(M2Message.BuildM2(head.ToArray()), _timeoutMs);

                int status = M2Message.ParseSysStatus(resp);
                if (status != 0 && status != ERR_OBJ_NONEXISTANT)
                    throw new InvalidOperationException(
                        $"WinBox native getall returned error 0x{status:X} on handler [{string.Join(",", handler)}].");

                records.AddRange(M2Message.ParseRecords(resp, KEY_RECORDS));

                var fields = M2Message.ParseAllFields(resp);
                if (status == ERR_OBJ_NONEXISTANT) break;          // explicit terminator
                if (!fields.TryGetValue(KEY_CONT, out var ct)) break; // no continuation → done
                contToken = ct.Item2;
            }
            return records;
        }

        // Field keys of the system-info singleton handler [13,4] cmd=7 (from the PoC GetSystemInfo).
        private static readonly int[] SYSINFO_HANDLER = { 13, 4 };
        private const int SYSINFO_CMD = 7;
        private const int KEY_SYS_VERSION = 0x000016; // s16 — RouterOS version string

        /// <summary>
        /// Reads the RouterOS version string from the system-info singleton (<c>[13,4]</c> cmd=7),
        /// e.g. "7.21.4". Returns <c>null</c> when the field is absent.
        /// </summary>
        internal string GetRouterVersion()
        {
            byte[] msg = M2Message.BuildM2(
                M2Message.SysToArr(SYSINFO_HANDLER), M2Message.SysFrom(),
                M2Message.BoolSys(0xFF0005, true), _channel.NextReqIdField(),
                M2Message.U32Sys(0xFF0007, SYSINFO_CMD));
            byte[] resp = _channel.SendReceive(msg, _timeoutMs);
            var fields = M2Message.ParseAllFields(resp);
            return fields.TryGetValue(KEY_SYS_VERSION, out var t) ? t.Item2?.ToString() : null;
        }

        /// <summary>
        /// Sends <c>get-one</c> (<c>0xFE0002</c> + <c>0xFE0001</c>=<paramref name="id"/>) and returns the
        /// record's decoded field dictionary. Records arrive under <c>0xFE0002</c>; falls back to the
        /// top-level fields when the handler answers inline.
        /// </summary>
        internal Dictionary<int, Tuple<string, object>> GetOne(int[] handler, int id)
        {
            byte[] msg = M2Message.BuildM2(
                M2Message.SysToArr(handler), M2Message.SysFrom(),
                M2Message.BoolSys(0xFF0005, true), _channel.NextReqIdField(),
                M2Message.U32Sys(0xFF0007, CMD_GETONE),
                M2Message.SessionIdField(id));
            byte[] resp = _channel.SendReceive(msg, _timeoutMs);
            var recs = M2Message.ParseRecords(resp, KEY_RECORDS);
            return recs.Count > 0 ? recs[0] : M2Message.ParseAllFields(resp);
        }

        // ── Writes (F2) ────────────────────────────────────────────────────────

        /// <summary>
        /// Sends <c>set</c> (<c>0xFE0003</c> + <c>0xFE0001</c>=<paramref name="id"/> + changed fields) and
        /// returns the reply's SYS status (0 = ok). Mirrors webfig <c>ObjectMap.setObject</c>
        /// ("edit = .id + changed fields"). Throws when the router reports a non-zero status.
        /// </summary>
        internal void Set(int[] handler, int id, IList<byte[]> fields)
        {
            var head = new List<byte[]>
            {
                M2Message.SysToArr(handler), M2Message.SysFrom(),
                M2Message.BoolSys(0xFF0005, true), _channel.NextReqIdField(),
                M2Message.U32Sys(0xFF0007, CMD_SET),
                M2Message.SessionIdField(id),
            };
            if (fields != null) head.AddRange(fields);
            byte[] resp = _channel.SendReceive(M2Message.BuildM2(head.ToArray()), _timeoutMs);
            ThrowOnStatus(resp, "set", handler);
        }

        /// <summary>
        /// Sends <c>add</c> (<c>0xFE0005</c> + fields, no .id) and returns the new record's M2 id
        /// (reply field <c>0xFE0001</c>), or <c>-1</c> if the reply carries no id.
        /// </summary>
        internal int Add(int[] handler, IList<byte[]> fields)
        {
            var head = new List<byte[]>
            {
                M2Message.SysToArr(handler), M2Message.SysFrom(),
                M2Message.BoolSys(0xFF0005, true), _channel.NextReqIdField(),
                M2Message.U32Sys(0xFF0007, CMD_ADD),
            };
            if (fields != null) head.AddRange(fields);
            byte[] resp = _channel.SendReceive(M2Message.BuildM2(head.ToArray()), _timeoutMs);
            ThrowOnStatus(resp, "add", handler);
            var f = M2Message.ParseAllFields(resp);
            return f.TryGetValue(KEY_ID, out var t) && t.Item2 != null ? Convert.ToInt32(t.Item2) : -1;
        }

        /// <summary>Sends <c>remove</c> (<c>0xFE0006</c> + <c>0xFE0001</c>=<paramref name="id"/>).</summary>
        internal void Remove(int[] handler, int id)
        {
            byte[] msg = M2Message.BuildM2(
                M2Message.SysToArr(handler), M2Message.SysFrom(),
                M2Message.BoolSys(0xFF0005, true), _channel.NextReqIdField(),
                M2Message.U32Sys(0xFF0007, CMD_REMOVE),
                M2Message.SessionIdField(id));
            byte[] resp = _channel.SendReceive(msg, _timeoutMs);
            ThrowOnStatus(resp, "remove", handler);
        }

        /// <summary>
        /// Sends <c>move</c> (<c>0xFE0007</c> + <c>0xFE0001</c>=<paramref name="id"/> +
        /// <c>0xFE0005</c>=<paramref name="destNextId"/>). A negative <paramref name="destNextId"/>
        /// (move to end) omits the next-id field.
        /// </summary>
        internal void Move(int[] handler, int id, int destNextId)
        {
            var head = new List<byte[]>
            {
                M2Message.SysToArr(handler), M2Message.SysFrom(),
                M2Message.BoolSys(0xFF0005, true), _channel.NextReqIdField(),
                M2Message.U32Sys(0xFF0007, CMD_MOVE),
                M2Message.SessionIdField(id),
            };
            if (destNextId >= 0) head.Add(M2Message.U32Sys(KEY_NEXTID, destNextId));
            byte[] resp = _channel.SendReceive(M2Message.BuildM2(head.ToArray()), _timeoutMs);
            ThrowOnStatus(resp, "move", handler);
        }

        private static void ThrowOnStatus(byte[] resp, string op, int[] handler)
        {
            int status = M2Message.ParseSysStatus(resp);
            if (status != 0)
                throw new InvalidOperationException(
                    $"WinBox native {op} returned error 0x{status:X} on handler [{string.Join(",", handler)}].");
        }
    }
}
