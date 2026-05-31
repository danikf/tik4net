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
        /// Builds a <c>/path print as-value [where …]</c> command.
        /// </summary>
        internal static string BuildPrint(string apiPath, IList<ITikCommandParameter> parameters)
        {
            string cliBase = ApiPathToCli(apiPath);
            var sb = new StringBuilder(cliBase);
            sb.Append(" as-value");

            string whereClause = BuildWhereClause(parameters);
            if (!string.IsNullOrEmpty(whereClause))
            {
                sb.Append(" where ");
                sb.Append(whereClause);
            }

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
            if (idValue != null)
            {
                sb.Append(" [find .id=");
                sb.Append(idValue);
                sb.Append(']');
            }

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

                // Skip special properties
                if (name == TikSpecialProperties.Proplist || name == TikSpecialProperties.Tag)
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
                return name + "!=" + QuoteIfNeeded(val.Substring(1));

            // Greater-than: ?>count=5 encoded as value starting with ">"
            if (val.StartsWith(">"))
                return name + ">" + QuoteIfNeeded(val.Substring(1));

            // Less-than: ?<count=5 encoded as value starting with "<"
            if (val.StartsWith("<"))
                return name + "<" + QuoteIfNeeded(val.Substring(1));

            // Regex: ?~comment=eth encoded as value starting with "~"
            if (val.StartsWith("~"))
                return name + "~" + QuoteIfNeeded(val.Substring(1));

            // Plain equality
            return name + "=" + QuoteIfNeeded(val);
        }

        private static string BuildFindVerb(string apiPath, IList<ITikCommandParameter> parameters)
        {
            string cliBase = ApiPathToCli(apiPath);
            var sb = new StringBuilder(cliBase);

            string idValue = FindIdParam(parameters);
            if (idValue != null)
            {
                sb.Append(" [find .id=");
                sb.Append(idValue);
                sb.Append(']');
            }

            // Append any remaining NameValue params (e.g. destination for move)
            AppendNameValueParams(sb, parameters, skipId: true);
            return sb.ToString();
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
                if (p.Name == TikSpecialProperties.Proplist || p.Name == TikSpecialProperties.Tag)
                    continue;

                sb.Append(' ');
                sb.Append(p.Name);
                sb.Append('=');
                sb.Append(QuoteIfNeeded(p.Value ?? string.Empty));
            }
        }

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
