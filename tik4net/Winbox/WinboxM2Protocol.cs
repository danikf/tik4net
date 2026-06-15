namespace tik4net.Winbox
{
    /// <summary>
    /// Single source of truth for the WinBox M2 (nv/jsproxy) protocol constants used by tik4net's
    /// native WinBox transport. This class is documentation as much as code: every constant carries
    /// its meaning and, where relevant, its wire encoding. It is the human-readable reference list of
    /// the protocol numbers the library speaks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sources of truth (all under <c>_notes/</c>):
    /// <list type="bullet">
    ///   <item>System field keys + error codes: tenable/routeros <c>common/winbox_message.cpp</c>,
    ///         transcribed in <c>_notes/winbox-native-m2-plan.md</c> §6.</item>
    ///   <item>Command catalog + error descriptions: webfig <c>master-d53cd8ec58cb.js</c>
    ///         (<c>ObjectMap.getall/fetch/setObject/...</c>, <c>getErrorDescription</c>), summarised in
    ///         <c>_notes/winbox-native-m2-plan.md</c> §10.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Field-key encoding.</b> A field on the wire begins with a 3-byte little-endian key
    /// (<c>[key_lo][key_mid][key_hi]</c>) where the high byte is the namespace: <c>0x00</c> = user
    /// fields (1-based key), <c>0xFF</c> = system fields, <c>0xFE</c> = session/record fields. The 4th
    /// header byte is the type byte (see <see cref="Tlv"/>). So e.g. key <c>0xFE0009</c> = namespace
    /// <c>0xFE</c>, key id <c>0x0009</c> (the well-known <c>comment</c> field).
    /// </para>
    /// <para>
    /// <b>Value collisions — same number, different concept.</b> The command numbers (<see cref="Command"/>)
    /// and the error codes (<see cref="Error"/>) live in the same <c>0xFE00xx</c> range and several
    /// numbers coincide while meaning entirely different things depending on the field they appear in
    /// (a command sits in <c>uff0007</c> = <see cref="SysKey.Command"/>; an error sits in
    /// <c>uff0008</c> = <see cref="SysKey.ErrorCode"/>). These are intentionally kept as separate,
    /// distinctly-named members and cross-referenced in comments:
    /// <list type="bullet">
    ///   <item><c>0xFE0004</c> = <see cref="Command.GetAll"/> AND <see cref="Error.ObjectNonexistent"/>
    ///         (the getall terminator).</item>
    ///   <item><c>0xFE0002</c> = <see cref="Command.GetOne"/> AND <see cref="Error.NotImplemented"/>.</item>
    ///   <item><c>0xFE000D</c> = <see cref="Command.GetSingleton"/> AND <see cref="Error.Timeout"/>.</item>
    ///   <item><c>0xFE0012</c> = <see cref="Command.Subscribe"/> AND <see cref="Error.Busy"/>.</item>
    /// </list>
    /// </para>
    /// </remarks>
    internal static class WinboxM2Protocol
    {
        // ──────────────────────────────────────────────────────────────────────
        // SysKey — system field keys, namespace 0xFF (0xFF00xx).
        // These frame every M2 message: routing (to/from), the command, reply control,
        // request correlation, and the error channel. Source: winbox_message.cpp (plan §6).
        // ──────────────────────────────────────────────────────────────────────
        internal static class SysKey
        {
            /// <summary><c>k_sys_to</c> — destination handler path (u32[]). webfig <c>Uff0001</c>.</summary>
            internal const int To = 0xFF0001;
            /// <summary><c>k_from</c> — source id (u32[]); in a push notification, which subscription. webfig <c>Uff0002</c>.</summary>
            internal const int From = 0xFF0002;
            /// <summary><c>k_reply_expected</c> — bool; request a reply frame for this message.</summary>
            internal const int ReplyExpected = 0xFF0005;
            /// <summary><c>k_request_id</c> — request correlation id (u8/u32).</summary>
            internal const int RequestId = 0xFF0006;
            /// <summary><c>k_command</c> — the command number (<see cref="Command"/>) carried as <c>uff0007</c>.</summary>
            internal const int Command = 0xFF0007;
            /// <summary><c>k_error_code</c> — error code (<see cref="Error"/>) on a failed reply; 0 = ok. webfig <c>uff0008</c>.</summary>
            internal const int ErrorCode = 0xFF0008;
            /// <summary><c>k_error_string</c> — optional human error string. webfig <c>sff0009</c>.</summary>
            internal const int ErrorString = 0xFF0009;
        }

        // ──────────────────────────────────────────────────────────────────────
        // RecordKey — well-known record/field keys, namespace 0xFE (0xFE00xx).
        // Generic across all config tables (the per-table fields come from the .jg catalog).
        // ──────────────────────────────────────────────────────────────────────
        internal static class RecordKey
        {
            /// <summary><c>.id</c> — record handle. webfig <c>ufe0001</c> (also the mproxy/mepty SESSION_ID slot).</summary>
            internal const int Id = 0xFE0001;
            /// <summary>Records — the message-array of returned records in a getall/get-one reply. webfig <c>Mfe0002</c>.</summary>
            internal const int Records = 0xFE0002;
            /// <summary>Continuation token — present on a getall reply while more pages remain. webfig <c>ufe0003</c>.</summary>
            internal const int Continuation = 0xFE0003;
            /// <summary>Next-id — destination for an ordered <see cref="Command.Move"/> (insert before this id). webfig <c>ufe0005</c>.</summary>
            internal const int NextId = 0xFE0005;
            /// <summary>Comment — well-known string field (<c>types.comment</c>). webfig <c>sfe0009</c>; verified live.</summary>
            internal const int Comment = 0xFE0009;
            /// <summary>Disabled — well-known bool flag (<c>1</c>=disabled) for the default enable/disable
            /// toggle. webfig <c>types.enable</c> with no explicit id writes <c>obj.bfe000a</c>.</summary>
            internal const int Disabled = 0xFE000A;
            /// <summary>Finished — monitor "done" flag: a streaming-monitor poll reply sets <c>bfe000b</c>
            /// when the operation has completed (e.g. a traceroute reached its target), telling the client to
            /// stop polling. webfig <c>ObjectQuery</c>/<c>ObjectAction</c>: <c>if(rep.bfe000b) this.stop()</c>.</summary>
            internal const int Finished = 0xFE000B;
            /// <summary>Flags — getall/get flag word (see <see cref="GetAllFlags"/>). webfig <c>ufe000c</c>.</summary>
            internal const int Flags = 0xFE000C;
            /// <summary>Removed flag — set on a record that has been deleted (push model). webfig <c>ufe0013</c>.</summary>
            internal const int Removed = 0xFE0013;
            /// <summary>MaxObjs — getall page-size hint. webfig <c>ufe0018</c>.</summary>
            internal const int MaxObjs = 0xFE0018;
            /// <summary>Count — total object count reported alongside getall records. webfig <c>ufe0019</c>.</summary>
            internal const int Count = 0xFE0019;

            /// <summary>
            /// Name — well-known interface/object name field, USER namespace key <c>0x10006</c>
            /// (webfig <c>s10006</c>). Not a 0xFE key, but a protocol-stable field key shared by config
            /// tables, so it is seeded here.
            /// </summary>
            internal const int Name = 0x10006;

            // System-info singleton ([13,4] cmd=7) string fields, USER namespace (0x0000xx).
            /// <summary>System-info: RouterOS version string, e.g. "7.21.4". webfig <c>s16</c>.</summary>
            internal const int SysInfoVersion = 0x000016;
        }

        /// <summary>
        /// Base getall flags <c>refetchonopen | refreshfilter</c> carried in <see cref="RecordKey.Flags"/>
        /// (<c>ufe000c</c>). Without this the handler returns no rows. webfig: <c>req.ufe000c = 0x10000005</c>.
        /// </summary>
        internal const int GetAllFlags = 0x10000005;

        /// <summary>
        /// Stats bit OR'd into <see cref="GetAllFlags"/> for handlers whose <c>.jg</c> window is
        /// <c>autorefresh</c> (live/dynamic data): it makes getall include runtime counter fields such as
        /// the firewall rule <c>bytes</c>/<c>packets</c> (keys 0x2711/0x2710), which the base flag omits.
        /// </summary>
        internal const int GetAllStatsFlag = 0x2;

        // ──────────────────────────────────────────────────────────────────────
        // Command — command numbers carried in SysKey.Command (uff0007). From webfig master.js.
        // Grouped by role. NOTE: several of these numbers collide with Error codes — see each remark
        // and the class-level <remarks>. A command is only a command in the uff0007 slot.
        // ──────────────────────────────────────────────────────────────────────
        internal static class Command
        {
            // ── READ ──────────────────────────────────────────────────────────
            /// <summary>
            /// getall — list all records of a (paginated) config table. webfig default <c>getallcmd</c>.
            /// COLLISION: same value as <see cref="Error.ObjectNonexistent"/> (which the handler returns
            /// in <see cref="SysKey.ErrorCode"/> as the getall "no more rows" terminator).
            /// </summary>
            internal const int GetAll = 0xFE0004;
            /// <summary>
            /// get-one — fetch a single record by <see cref="RecordKey.Id"/>.
            /// COLLISION: same value as <see cref="Error.NotImplemented"/>.
            /// </summary>
            internal const int GetOne = 0xFE0002;

            // ── WRITE ─────────────────────────────────────────────────────────
            /// <summary>set/change — edit a record (<see cref="RecordKey.Id"/> + changed fields). webfig map <c>setcmd</c>.</summary>
            internal const int Set = 0xFE0003;
            /// <summary>add — insert a new record (fields, no .id); reply carries the new <see cref="RecordKey.Id"/>.</summary>
            internal const int Add = 0xFE0005;
            /// <summary>remove — delete a record by <see cref="RecordKey.Id"/>.</summary>
            internal const int Remove = 0xFE0006;
            /// <summary>
            /// move — reorder an ordered table: <see cref="RecordKey.Id"/> + <see cref="RecordKey.NextId"/>
            /// (move before next-id; 0xFFFFFFFF = move to end).
            /// </summary>
            internal const int Move = 0xFE0007;

            // ── SINGLETON (holder objects, e.g. /system/identity) ─────────────
            /// <summary>
            /// get singleton — read a holder/singleton object. webfig <c>getcmd</c>.
            /// COLLISION: same value as <see cref="Error.Timeout"/>.
            /// </summary>
            internal const int GetSingleton = 0xFE000D;
            /// <summary>set singleton — write a holder/singleton object. webfig holder <c>setcmd</c>.</summary>
            internal const int SetSingleton = 0xFE000E;

            // ── SUBSCRIBE (push model) ────────────────────────────────────────
            /// <summary>
            /// subscribe — start an async push subscription on a path (<see cref="SysKey.To"/>).
            /// COLLISION: same value as <see cref="Error.Busy"/>.
            /// </summary>
            internal const int Subscribe = 0xFE0012;
            /// <summary>unsubscribe — stop a push subscription.</summary>
            internal const int Unsubscribe = 0xFE0013;

            // ── SETUP / WIZARD ────────────────────────────────────────────────
            /// <summary>setup/wizard step — webfig <c>mfe000f</c>=obj, <c>ufe000e</c>=page.</summary>
            internal const int SetupStep = 0xFE0008;
        }

        // ──────────────────────────────────────────────────────────────────────
        // SafeMode — the system safe-mode handler ([17]). Take/release are exposed by WebFig's
        // toggleSafeMode(): take = SYS_CMD 0x80003 (reply carries the safe-mode id in RecordKey.Id,
        // 0xFE0001), release/commit = SYS_CMD 0x80005 with that id sent back. Source: webfig
        // master-d53cd8ec58cb.js toggleSafeMode(). WebFig has no unroll/get command (it remembers the id
        // locally), so the native transport supports take/release only.
        // ──────────────────────────────────────────────────────────────────────
        internal static class SafeMode
        {
            /// <summary>Safe-mode system handler path: <c>[17]</c> (webfig <c>Uff0001:[17]</c>).</summary>
            internal static readonly int[] Handler = { 17 };
            /// <summary>take/enable safe mode. webfig <c>uff0007:0x80003</c>; reply returns the id in <see cref="RecordKey.Id"/>.</summary>
            internal const int Take = 0x80003;
            /// <summary>release/commit safe mode. webfig <c>uff0007:0x80005</c> + the held id in <see cref="RecordKey.Id"/>.</summary>
            internal const int Release = 0x80005;
        }

        // ──────────────────────────────────────────────────────────────────────
        // Mproxy — the file-proxy handler ([2,2]) and the system-info handler ([13,4]).
        // These use small per-handler command numbers (NOT the 0xFE00xx generic CRUD set).
        // ──────────────────────────────────────────────────────────────────────
        internal static class Mproxy
        {
            /// <summary>mproxy file handler path: <c>[2,2]</c>.</summary>
            internal static readonly int[] Handler = { 2, 2 };

            /// <summary>open a static file under <c>/home/web/webfig/</c> (e.g. <c>list</c>, <c>&lt;name&gt;.jg.gz</c>). cmd=7.</summary>
            internal const int OpenStatic = 7;
            /// <summary>open a package file under <c>/var/pckg/</c> (authenticated; CHR denies this). cmd=3.</summary>
            internal const int OpenVarPkg = 3;
            /// <summary>read the next chunk of an open file handle. cmd=4.</summary>
            internal const int Read = 4;
            /// <summary>setup/handshake step used by the legacy MD5 login flow. cmd=5.</summary>
            internal const int Setup = 5;

            /// <summary>Per-read chunk size (bytes); reads continue until a short/empty chunk = EOF.</summary>
            internal const int ChunkSize = 32768;

            /// <summary>
            /// mproxy user-namespace (0x00) field keys. Small per-handler key ids — the same numeric
            /// value means different things under a different handler (see <see cref="Mepty.Key"/> /
            /// <see cref="LegacyAuth"/>), so they are scoped here, not global.
            /// </summary>
            internal static class Key
            {
                /// <summary>File name to open (string), user key 1.</summary>
                internal const int FileName = 1;
                /// <summary>Max bytes to read this chunk (u32), user key 2.</summary>
                internal const int MaxChunk = 2;
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // Mepty — the terminal-PTY handler ([76]). Drives an interactive RouterOS CLI
        // over the encrypted M2 channel (used by the WinBox-CLI transport).
        // ──────────────────────────────────────────────────────────────────────
        internal static class Mepty
        {
            /// <summary>mepty (terminal PTY) handler path: <c>[76]</c>.</summary>
            internal static readonly int[] Handler = { 76 };

            /// <summary>open a terminal session and authenticate. cmd=0x0A0065 (meptyLogin).</summary>
            internal const int Login = 0x0A0065;
            /// <summary>send keystrokes / pull terminal output. cmd=0x0A0067 (meptyData).</summary>
            internal const int Data = 0x0A0067;

            /// <summary>
            /// mepty user-namespace (0x00) field keys. NOTE the <see cref="Login"/>/<see cref="Data"/>
            /// command context changes a key's meaning — see <see cref="Cols"/> vs <see cref="Counter"/>.
            /// </summary>
            internal static class Key
            {
                /// <summary>Login password (string), user key 1.</summary>
                internal const int Password = 1;
                /// <summary>Terminal type on <see cref="Login"/> / keystroke bytes on <see cref="Data"/> (string/raw), user key 2.</summary>
                internal const int Input = 2;
                /// <summary>
                /// Terminal column count on <see cref="Login"/> (u32), user key 3.
                /// COLLISION: on <see cref="Data"/> the same key is the <see cref="Counter"/>.
                /// </summary>
                internal const int Cols = 3;
                /// <summary>
                /// Monotonic data counter on <see cref="Data"/> (u32), user key 3.
                /// COLLISION: on <see cref="Login"/> the same key is <see cref="Cols"/>.
                /// </summary>
                internal const int Counter = 3;
                /// <summary>Terminal row count on <see cref="Login"/> (u32), user key 4.</summary>
                internal const int Rows = 4;
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // LegacyAuth — pre-6.43 MD5 login fallback (when EC-SRP5 is unavailable).
        // Flow: mproxy open "list" (Mproxy.OpenStatic) → Mproxy.Setup → [13,4] GetSalt → [13,4] Login.
        // Reuses the system-info handler path ([13,4] = SysInfo.Handler).
        // ──────────────────────────────────────────────────────────────────────
        internal static class LegacyAuth
        {
            /// <summary>request the password salt from the system handler. [13,4] cmd=4.</summary>
            internal const int GetSalt = 4;
            /// <summary>submit user + salted MD5 hash. [13,4] cmd=1.</summary>
            internal const int Login = 1;

            /// <summary>legacy-auth user-namespace (0x00) field keys.</summary>
            internal static class Key
            {
                /// <summary>User name (string), user key 1.</summary>
                internal const int User = 1;
                /// <summary>Password salt (raw), user key 9.</summary>
                internal const int Salt = 9;
                /// <summary>Salted MD5 hash (raw), user key 10.</summary>
                internal const int Hash = 10;
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // SysInfo — the system-info singleton handler ([13,4]).
        // ──────────────────────────────────────────────────────────────────────
        internal static class SysInfo
        {
            /// <summary>System-info singleton handler path: <c>[13,4]</c>.</summary>
            internal static readonly int[] Handler = { 13, 4 };

            /// <summary>read the system-info singleton (version/board/arch/identity). cmd=7.</summary>
            internal const int Command = 7;
        }

        // ──────────────────────────────────────────────────────────────────────
        // Error — error codes carried in SysKey.ErrorCode (uff0008); 0 = success.
        // Authoritative names from winbox_message.cpp (plan §6); descriptions from webfig
        // getErrorDescription(). NOTE: numbers collide with Command numbers — an error is only an
        // error in the uff0008 slot. See each remark and the class-level <remarks>.
        // ──────────────────────────────────────────────────────────────────────
        internal static class Error
        {
            /// <summary>Success / no error (uff0008 absent or 0).</summary>
            internal const int None = 0;

            /// <summary>
            /// <c>k_not_implemented</c> — "feature is not implemented".
            /// COLLISION: same value as <see cref="Command.GetOne"/>.
            /// </summary>
            internal const int NotImplemented = 0xFE0002;
            /// <summary>Also "feature is not implemented" per webfig <c>getErrorDescription</c>.</summary>
            internal const int NotImplemented2 = 0xFE0003;
            /// <summary>
            /// <c>k_obj_nonexistant</c> — "object doesn't exist". Also used as the getall pagination
            /// terminator. COLLISION: same value as <see cref="Command.GetAll"/>.
            /// </summary>
            internal const int ObjectNonexistent = 0xFE0004;
            /// <summary>Also "object doesn't exist" per webfig <c>getErrorDescription</c>.</summary>
            internal const int ObjectNonexistent2 = 0xFE0011;
            /// <summary>"object already exists" (webfig). COLLISION: same value as <see cref="Command.Move"/>.</summary>
            internal const int AlreadyExists = 0xFE0007;
            /// <summary><c>k_not_permitted</c> — "not permitted". COLLISION: same value as <see cref="RecordKey.Comment"/>.</summary>
            internal const int NotPermitted = 0xFE0009;
            /// <summary>
            /// <c>k_timeout</c> — "timeout".
            /// COLLISION: same value as <see cref="Command.GetSingleton"/>.
            /// </summary>
            internal const int Timeout = 0xFE000D;
            /// <summary>
            /// <c>k_busy</c> — "busy".
            /// COLLISION: same value as <see cref="Command.Subscribe"/>.
            /// </summary>
            internal const int Busy = 0xFE0012;
        }

        // ──────────────────────────────────────────────────────────────────────
        // Tlv — TLV type bytes for the M2 wire format (4th field-header byte).
        // The byte = (ftype<<3) | sizeFlags, where sizeFlags = FS_SHORT(0x01) | FS_LONG(0x02).
        // ftype: 0=bool 1=u32 2=u64 3=addr6 4=string 5=message 6=raw  (+16 for the array variant).
        // Length/count: short=1B, normal=2B, long=4B (webfig readLen). These are documented here for
        // reference; M2Message's builders/parser keep the literals inline (switch labels) for clarity.
        // ──────────────────────────────────────────────────────────────────────
        internal static class Tlv
        {
            /// <summary>bool false (ftype 0).</summary>
            internal const int BoolFalse = 0x00;
            /// <summary>bool true (ftype 0).</summary>
            internal const int BoolTrue = 0x01;
            /// <summary>u8 — single byte (ftype 1, short flag). Used for small u32 values (&lt;= 255).</summary>
            internal const int U8 = 0x09;
            /// <summary>u32 — 4-byte little-endian (ftype 1).</summary>
            internal const int U32 = 0x08;
            /// <summary>u64 — 8-byte little-endian (ftype 2).</summary>
            internal const int U64 = 0x10;
            /// <summary>u32[] — 2B count + count×4B (ftype 17).</summary>
            internal const int U32Array = 0x88;
            /// <summary>string, short — 1B length + UTF-8 bytes (ftype 4, short).</summary>
            internal const int StringShort = 0x21;
            /// <summary>string, normal — 2B length + UTF-8 bytes (ftype 4).</summary>
            internal const int StringNormal = 0x20;
            /// <summary>raw, short — 1B length + bytes (ftype 6, short).</summary>
            internal const int RawShort = 0x31;
            /// <summary>raw, normal — 2B length + bytes (ftype 6).</summary>
            internal const int RawNormal = 0x30;
            /// <summary>string[] — 2B count + (2B len + bytes) per entry (ftype 20). The 0xA0 "str_array" trap.</summary>
            internal const int StringArray = 0xA0;
            /// <summary>message, normal — 2B length + 'M2' submessage (ftype 5).</summary>
            internal const int MessageNormal = 0x28;
            /// <summary>message, short — 1B length + 'M2' submessage (ftype 5, short).</summary>
            internal const int MessageShort = 0x29;
            /// <summary>message, long — 4B length + 'M2' submessage (ftype 5, long).</summary>
            internal const int MessageLong = 0x2A;
            /// <summary>message[], normal — 2B count + (2B len + 'M2' submessage) per element (ftype 21).</summary>
            internal const int MessageArrayNormal = 0xA8;
            /// <summary>message[], short — 1B count + (1B len + submessage) per element (ftype 21, short).</summary>
            internal const int MessageArrayShort = 0xA9;
            /// <summary>message[], long — 4B count + (4B len + submessage) per element (ftype 21, long).</summary>
            internal const int MessageArrayLong = 0xAA;
        }
    }
}
