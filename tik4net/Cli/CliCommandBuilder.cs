using System;
using System.Collections.Generic;
using System.Text;

namespace tik4net.Cli
{
    /// <summary>
    /// Translates <see cref="ITikCommand"/> metadata into RouterOS CLI command strings.
    /// All methods are transport-agnostic; the caller (transport layer) is responsible for
    /// appending <c>without-paging</c> on PTY transports before sending to the device.
    /// </summary>
    internal static class CliCommandBuilder
    {
        // ── Path translation ───────────────────────────────────────────────────

        /// <summary>
        /// Converts an API-style path (/ip/address/print) to CLI form (/ip address print).
        /// The leading slash is preserved; segments are joined with spaces.
        /// </summary>
        internal static string ApiPathToCli(string apiPath)
        {
            if (string.IsNullOrWhiteSpace(apiPath))
                return apiPath;

            string trimmed = apiPath.TrimStart('/');
            var parts = trimmed.Split('/');
            return "/" + string.Join(" ", parts);
        }

        // ── Print ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a <c>:put [/path print as-value [where …]]</c> command.
        /// <para>
        /// IMPORTANT: bare <c>print as-value</c> emits NOTHING to an interactive terminal — the
        /// as-value format is only materialised in script context. Wrapping it in <c>:put [ … ]</c>
        /// forces RouterOS to print the value. The result is a single line where records are
        /// concatenated with <c>;</c> and each record starts at <c>.id=</c> (see <see cref="CliOutputParser"/>).
        /// No <c>without-paging</c> is needed inside <c>:put</c> (script context does not page).
        /// </para>
        /// </summary>
        internal static string BuildPrint(string apiPath, IList<ITikCommandParameter> parameters)
        {
            string cliBase = ApiPathToCli(apiPath);
            var sb = new StringBuilder(":put [");
            sb.Append(cliBase);

            // The O/R mapper requests the full field set via a 'detail' NameValue parameter
            // (metadata.IncludeDetails). Bare 'print as-value' returns only the summary columns
            // (e.g. /interface omits default-name, mtu, rx-byte…); 'print detail as-value' returns all.
            if (HasNameValueFlag(parameters, "detail"))
                sb.Append(" detail");
            // NOTE: '.cli-stats' is a CLI-layer signal — it is not a print modifier and must be
            // ignored here (it never becomes a CLI word or a where-clause predicate).

            // Some commands (e.g. /interface/ethernet/monitor) require a 'numbers=<name>' NameValue
            // parameter to identify the target interface, and a flag-style 'once' parameter to take a
            // single snapshot instead of running continuously.  These must be passed before 'as-value'.
            string numbersValue = FindNameValueParam(parameters, "numbers");
            if (numbersValue != null)
            {
                sb.Append(" numbers=");
                sb.Append(QuoteIfNeeded(numbersValue));
            }
            if (HasNameValueFlag(parameters, "once"))
                sb.Append(" once");

            sb.Append(" as-value");

            string whereClause = BuildWhereClause(parameters);
            if (!string.IsNullOrEmpty(whereClause))
            {
                sb.Append(" where ");
                sb.Append(whereClause);
            }

            sb.Append(']');
            return sb.ToString();
        }

        /// <summary>
        /// Builds a <c>:put [/path print stats as-value [where …]]</c> command.
        /// Used as the second query in the two-query path for entities with <c>IncludeCliStats</c>.
        /// Does NOT include <c>detail</c> — <c>stats</c> and <c>detail</c> are mutually exclusive
        /// in RouterOS CLI (adding both yields only the stats columns).
        /// </summary>
        internal static string BuildPrintStats(string apiPath, IList<ITikCommandParameter> parameters)
        {
            string cliBase = ApiPathToCli(apiPath);
            var sb = new StringBuilder(":put [");
            sb.Append(cliBase);
            sb.Append(" stats as-value");

            string whereClause = BuildWhereClause(parameters);
            if (!string.IsNullOrEmpty(whereClause))
            {
                sb.Append(" where ");
                sb.Append(whereClause);
            }

            sb.Append(']');
            return sb.ToString();
        }

        // ── Monitor snapshot ───────────────────────────────────────────────────

        /// <summary>
        /// Builds a pollable monitor snapshot: <c>:put [/path &lt;inputs&gt; &lt;modifier&gt; as-value]</c>.
        /// The command's NameValue parameters are emitted as the monitor's inputs (e.g.
        /// <c>interface=ether1</c>, <c>address=8.8.8.8</c>); an empty-valued parameter is emitted as a bare
        /// flag. <paramref name="snapshotModifier"/> (e.g. <c>once</c>, <c>count=1</c>, <c>duration=1</c> —
        /// see <see cref="CliMonitorVerbs"/>) is appended unless the caller already supplied that token.
        /// Wrapping in <c>:put [ … ]</c> forces RouterOS to materialise the as-value line (bare
        /// <c>as-value</c> prints nothing to a terminal — see <see cref="BuildPrint"/>).
        /// </summary>
        internal static string BuildMonitorSnapshot(string apiPath, IList<ITikCommandParameter> parameters, string snapshotModifier)
        {
            string cliBase = ApiPathToCli(apiPath);
            var sb = new StringBuilder(":put [");
            sb.Append(cliBase);

            string modName = string.IsNullOrEmpty(snapshotModifier) ? null : snapshotModifier.Split('=')[0];
            bool modifierAlreadyPresent = false;

            foreach (var p in parameters)
            {
                if (p.ParameterFormat == TikCommandParameterFormat.Filter)
                    continue;
                if (p.Name == TikSpecialProperties.Id || IsSpecialParam(p.Name))
                    continue;

                if (modName != null && string.Equals(p.Name, modName, System.StringComparison.OrdinalIgnoreCase))
                    modifierAlreadyPresent = true;

                sb.Append(' ');
                sb.Append(p.Name);
                if (!string.IsNullOrEmpty(p.Value))
                {
                    sb.Append('=');
                    sb.Append(QuoteIfNeeded(p.Value));
                }
            }

            if (!string.IsNullOrEmpty(snapshotModifier) && !modifierAlreadyPresent)
            {
                sb.Append(' ');
                sb.Append(snapshotModifier);
            }

            sb.Append(" as-value]");
            return sb.ToString();
        }

        // ── Torch (freeze-frame) ──────────────────────────────────────────────

        /// <summary>
        /// The torch fields tik4net requests via <c>proplist</c>. Matches every property on
        /// <c>tik4net.Objects.Tool.ToolTorch</c> except <c>.section</c> (a CLI-only limitation — RouterOS's
        /// per-row section/time-slice index is not exposed as a torch proplist field). The order listed here
        /// is NOT preserved in the response — confirmed live that RouterOS reorders columns to its own
        /// canonical order (<c>ip-protocol</c> first) regardless of the requested <c>proplist</c> order, so
        /// <see cref="CliOutputParser.ParseTorchFrame"/> reads the actual order back from each frame's own
        /// <c>Columns:</c> declaration rather than assuming it matches this list.
        /// </summary>
        internal static readonly string[] TorchFields =
            { "src-address", "src-port", "dst-address", "dst-port", "ip-protocol", "tx", "rx", "tx-packets", "rx-packets" };

        /// <summary>
        /// Builds a torch snapshot: <c>:put [/tool torch &lt;inputs&gt; duration=D freeze-frame-interval=F
        /// proplist=…]</c>. Unlike other monitors, torch's <c>as-value</c> form emits nothing, and its default
        /// plain-text columns omit <c>tx-packets</c>/<c>rx-packets</c> and self-adjust width per VT100 redraw.
        /// <c>freeze-frame-interval</c> makes it append a discrete, terminated frame instead of redrawing in
        /// place; an explicit <c>proplist</c> (see <see cref="TorchFields"/>) fixes the field set (its order in
        /// the response is decided by RouterOS, not by the order requested here — see <see cref="TorchFields"/>).
        /// <paramref name="freezeFrameSeconds"/> is duplicated into <c>duration</c> as <c>2×</c> itself —
        /// confirmed live (ROS 7.21.4) as the minimum that reliably flushes one complete frame; a
        /// <c>duration</c> equal to a single interval can complete with zero frames flushed.
        /// </summary>
        internal static string BuildTorchSnapshot(string apiPath, IList<ITikCommandParameter> parameters, int freezeFrameSeconds)
        {
            string cliBase = ApiPathToCli(apiPath);
            var sb = new StringBuilder(":put [");
            sb.Append(cliBase);

            foreach (var p in parameters)
            {
                if (p.ParameterFormat == TikCommandParameterFormat.Filter)
                    continue;
                if (p.Name == TikSpecialProperties.Id || IsSpecialParam(p.Name))
                    continue;
                if (string.Equals(p.Name, "duration", System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.Name, "freeze-frame-interval", System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.Name, "proplist", System.StringComparison.OrdinalIgnoreCase))
                    continue; // these are owned by this builder, not the caller

                sb.Append(' ');
                sb.Append(p.Name);
                if (!string.IsNullOrEmpty(p.Value))
                {
                    sb.Append('=');
                    sb.Append(QuoteIfNeeded(p.Value));
                }
            }

            sb.Append(" duration=").Append(freezeFrameSeconds * 2);
            sb.Append(" freeze-frame-interval=").Append(freezeFrameSeconds);
            sb.Append(" proplist=").Append(string.Join(",", TorchFields));
            sb.Append(']');
            return sb.ToString();
        }

        // ── Add ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a <c>:put [/path add k=v …]</c> command that returns the new record's .id.
        /// </summary>
        internal static string BuildAdd(string apiPath, IList<ITikCommandParameter> parameters)
        {
            // Path includes "add" as the last segment already (e.g. /ip/address/add).
            string cliBase = ApiPathToCli(apiPath);
            var sb = new StringBuilder(":put [");
            sb.Append(cliBase);
            AppendNameValueParams(sb, parameters);
            sb.Append(']');
            return sb.ToString();
        }

        // ── Set ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a <c>/path set [find .id=*N] k=v …</c> command.
        /// </summary>
        internal static string BuildSet(string apiPath, IList<ITikCommandParameter> parameters)
        {
            string cliBase = ApiPathToCli(apiPath);
            var sb = new StringBuilder(cliBase);

            string idValue = FindIdParam(parameters);
            AppendFindIdentifier(sb, idValue);

            AppendNameValueParams(sb, parameters, skipId: true);
            return sb.ToString();
        }

        // ── Remove ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a <c>/path remove [find .id=*N]</c> command.
        /// </summary>
        internal static string BuildRemove(string apiPath, IList<ITikCommandParameter> parameters)
        {
            return BuildFindVerb(apiPath, parameters);
        }

        // ── Simple verbs (enable / disable / move / unset) ────────────────────

        /// <summary>
        /// Builds <c>/path verb [find .id=*N] [k=v …]</c> for simple verbs.
        /// </summary>
        internal static string BuildSimpleVerb(string apiPath, string verb, IList<ITikCommandParameter> parameters)
        {
            // apiPath already contains the verb as last segment; verb param is informational only
            return BuildFindVerb(apiPath, parameters);
        }

        // ── GetScalar ─────────────────────────────────────────────────────────

        /// <summary>
        /// Builds <c>:put [/path get .id=*N value-name=propertyName]</c>.
        /// Used for ExecuteScalar on non-singleton paths where a specific field is requested.
        /// </summary>
        internal static string BuildGetScalar(string apiPath, string id, string valueName)
        {
            string cliBase = ApiPathToCli(apiPath);
            // Strip last segment (print/get) and replace with "get"
            int lastSpace = cliBase.LastIndexOf(' ');
            string basePath = lastSpace >= 0 ? cliBase.Substring(0, lastSpace) : cliBase;

            var sb = new StringBuilder(":put [");
            sb.Append(basePath);
            sb.Append(" get");
            if (!string.IsNullOrEmpty(id))
            {
                sb.Append(" .id=");
                sb.Append(id);
            }
            if (!string.IsNullOrEmpty(valueName))
            {
                sb.Append(" value-name=");
                sb.Append(valueName);
            }
            sb.Append(']');
            return sb.ToString();
        }

        // ── NonQuery ──────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a non-query command (system reboot, etc.).  No where-clause, no id find.
        /// Parameters are appended as name=value pairs.
        /// </summary>
        internal static string BuildNonQuery(string apiPath, IList<ITikCommandParameter> parameters)
        {
            string cliBase = ApiPathToCli(apiPath);
            var sb = new StringBuilder(cliBase);
            AppendNameValueParams(sb, parameters);
            return sb.ToString();
        }

        // ── Where clause ──────────────────────────────────────────────────────

        /// <summary>
        /// Builds a <c>where name=value &amp;&amp; …</c> clause from Filter-format parameters.
        /// Supports negation (<c>!value</c>), comparison (<c>&gt;value</c>, <c>&lt;value</c>),
        /// and regex (<c>~pattern</c>) prefixes.
        /// </summary>
        internal static string BuildWhereClause(IList<ITikCommandParameter> parameters)
        {
            var conditions = new List<string>();
            foreach (var p in parameters)
            {
                if (p.ParameterFormat != TikCommandParameterFormat.Filter)
                    continue;

                string name = p.Name;
                string val = p.Value ?? string.Empty;

                if (IsSpecialParam(name))
                    continue;

                string condition = BuildCondition(name, val);
                if (!string.IsNullOrEmpty(condition))
                    conditions.Add(condition);
            }

            return string.Join(" && ", conditions);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string BuildCondition(string name, string val)
        {
            if (string.IsNullOrEmpty(val))
                return name;

            // Negation: ?name=!value → name!=value
            if (val.StartsWith("!"))
                return name + "!=" + QuoteForWhere(val.Substring(1));

            // Greater-than: ?>count=5 encoded as value starting with ">"
            if (val.StartsWith(">"))
                return name + ">" + QuoteForWhere(val.Substring(1));

            // Less-than: ?<count=5 encoded as value starting with "<"
            if (val.StartsWith("<"))
                return name + "<" + QuoteForWhere(val.Substring(1));

            // Regex: ?~comment=eth encoded as value starting with "~"
            if (val.StartsWith("~"))
                return name + "~" + QuoteForWhere(val.Substring(1));

            // Plain equality
            return name + "=" + QuoteForWhere(val);
        }

        /// <summary>
        /// Quotes a value used on the right side of a <c>where</c> condition. The where-clause is an
        /// expression context where characters like <c>/</c> (e.g. in <c>192.168.1.1/24</c>) and
        /// <c>:</c> (e.g. MAC/IPv6) are interpreted as operators, so <c>where address=192.168.1.1/24</c>
        /// matches NOTHING. Anything outside a conservative safe set is wrapped in double-quotes.
        /// (Name=value parameters for add/set do NOT need this — they are not expression context.)
        /// </summary>
        internal static string QuoteForWhere(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            bool safe = true;
            foreach (char c in value)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                          || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-';
                if (!ok) { safe = false; break; }
            }

            if (safe)
                return value;

            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string BuildFindVerb(string apiPath, IList<ITikCommandParameter> parameters)
        {
            string cliBase = ApiPathToCli(apiPath);
            var sb = new StringBuilder(cliBase);

            string idValue = FindIdParam(parameters);
            AppendFindIdentifier(sb, idValue);

            // Append any remaining NameValue params (e.g. destination for move)
            AppendNameValueParams(sb, parameters, skipId: true);
            return sb.ToString();
        }

        /// <summary>
        /// Client-side marker parameters that must NEVER be emitted as a CLI word — they are stripped
        /// from where-clauses and from name=value lists. (Mirror of <c>RestRequestBuilder.IsSpecialParam</c>
        /// / <c>ApiCommand.IsSpecialParam</c>; the membership differs per transport on purpose.)
        ///   <c>.proplist</c> — as-value always returns every field; proplist trimming is not expressible in CLI.
        ///   <c>.tag</c>      — no tag protocol over a terminal.
        ///   <c>.cli-stats</c> — CLI-layer signal that triggers the two-query stats merge (<see cref="CliConnectionBase"/>).
        /// NOTE: this is the "dropped" set. <c>detail</c> / <c>once</c> / <c>numbers</c> are a DIFFERENT
        /// category — "consumed flags" that <see cref="BuildPrint"/> translates into print modifiers
        /// (via <see cref="HasNameValueFlag"/> / <see cref="FindNameValueParam"/>), not dropped.
        /// </summary>
        private static bool IsSpecialParam(string name)
            => name == TikSpecialProperties.Proplist
            || name == TikSpecialProperties.Tag
            || name == TikSpecialProperties.CliStats;

        /// <summary>
        /// Returns true when a non-Filter "consumed flag" parameter with the given name is present
        /// (e.g. the mapper's empty-valued <c>detail</c> flag). Used to translate flag parameters into
        /// print modifiers (see <see cref="BuildPrint"/>).
        /// </summary>
        private static bool HasNameValueFlag(IList<ITikCommandParameter> parameters, string name)
        {
            foreach (var p in parameters)
            {
                if (p.ParameterFormat != TikCommandParameterFormat.Filter
                    && string.Equals(p.Name, name, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Returns the value of a non-Filter (NameValue) parameter with the given name, or <c>null</c>
        /// if no such parameter exists.
        /// </summary>
        private static string FindNameValueParam(IList<ITikCommandParameter> parameters, string name)
        {
            foreach (var p in parameters)
            {
                if (p.ParameterFormat != TikCommandParameterFormat.Filter
                    && string.Equals(p.Name, name, System.StringComparison.OrdinalIgnoreCase))
                    return p.Value;
            }
            return null;
        }

        /// <summary>
        /// Appends the record identifier for set/remove/enable/disable/move. A real <c>.id</c> (<c>*N</c>) uses
        /// <c>[find .id=*N]</c>. Any other value uses <c>[find .id=X or name=X]</c> so a NAME works too
        /// (<c>.id=ether1</c> alone matches nothing — the literal <c>.id</c> is <c>*N</c> — but
        /// <c>name=ether1</c> resolves it). Names-as-id are accepted directly by the binary API/native
        /// transports; this bridges the CLI gap. The <c>or name=…</c> clause is harmless on tables without a
        /// <c>name</c> field (it simply never matches), and a bogus id (e.g. <c>-NoID-</c>) still yields
        /// "expected item id" → <see cref="TikNoSuchItemException"/>, preserving error fidelity. (Confirmed
        /// live against RouterOS 7.21.4.)
        /// </summary>
        private static void AppendFindIdentifier(StringBuilder sb, string idValue)
        {
            if (string.IsNullOrEmpty(idValue))
                return;
            if (idValue.StartsWith("*"))
            {
                sb.Append(" [find .id=");
                sb.Append(idValue);
                sb.Append(']');
            }
            else
            {
                string q = QuoteIfNeeded(idValue);
                sb.Append(" [find .id=");
                sb.Append(q);
                sb.Append(" or name=");
                sb.Append(q);
                sb.Append(']');
            }
        }

        private static string FindIdParam(IList<ITikCommandParameter> parameters)
        {
            foreach (var p in parameters)
            {
                if (p.Name == TikSpecialProperties.Id)
                    return p.Value;
            }
            return null;
        }

        private static void AppendNameValueParams(StringBuilder sb, IList<ITikCommandParameter> parameters, bool skipId = false)
        {
            foreach (var p in parameters)
            {
                if (p.ParameterFormat == TikCommandParameterFormat.Filter)
                    continue;
                if (skipId && p.Name == TikSpecialProperties.Id)
                    continue;
                if (IsSpecialParam(p.Name))
                    continue;

                // RouterOS CLI presence-flags (e.g. '/routing table … fib') are set by the bare field
                // NAME and REJECT a '=value' form ("expected end of command"). This is a CLI-only wire
                // quirk — the binary API/REST accept the usual 'fib=yes' — so it is handled here: a
                // truthy value emits the bare name, a falsy value is omitted (absence = false).
                if (IsCliPresenceFlag(p.Name))
                {
                    if (IsTruthy(p.Value))
                    {
                        sb.Append(' ');
                        sb.Append(p.Name);
                    }
                    continue;
                }

                sb.Append(' ');
                sb.Append(p.Name);
                sb.Append('=');
                sb.Append(QuoteIfNeeded(p.Value ?? string.Empty));
            }
        }

        /// <summary>
        /// RouterOS CLI fields that are set by the bare presence of the field name and reject a
        /// <c>=value</c> form. Kept minimal and explicit; extend as further presence-flags surface.
        /// </summary>
        private static readonly HashSet<string> CliPresenceFlagFields =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fib" };

        private static bool IsCliPresenceFlag(string name) => CliPresenceFlagFields.Contains(name);

        private static bool IsTruthy(string value)
            => string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Wraps a value in double-quotes if it contains whitespace, semicolons, or hash characters
        /// that would be misinterpreted by the RouterOS CLI parser.
        /// </summary>
        internal static string QuoteIfNeeded(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            bool needsQuote = false;
            foreach (char c in value)
            {
                if (c == ' ' || c == '\t' || c == ';' || c == '#' || c == '"')
                {
                    needsQuote = true;
                    break;
                }
            }

            if (!needsQuote)
                return value;

            // Escape any embedded double-quotes with backslash
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
