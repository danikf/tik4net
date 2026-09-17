using System;
using System.Collections.Generic;
using System.Globalization;
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
        /// <param name="apiPath">API-style path of the printed menu.</param>
        /// <param name="parameters">Command parameters (filters, flags, markers).</param>
        /// <param name="asJson">
        /// When set, the print is wrapped in <c>:serialize to=json [ … ]</c> so the result comes back as
        /// escaped JSON instead of the separator-ambiguous <c>as-value</c> form — see
        /// <see cref="CliJsonParser"/> for why that matters and <see cref="TikSpecialProperties.CliJson"/>
        /// for how a command asks for it. Requires RouterOS 7.13+; <see cref="CliConnectionBase"/> owns
        /// the fallback for older routers.
        /// </param>
        /// <param name="fromIndices">
        /// The <c>from=</c> row selector of a paged read — <see cref="WindowVariable"/> inside a window —
        /// or <c>null</c> for a whole-table read. See <see cref="AppendFrom"/> for where it has to go.
        /// </param>
        internal static string BuildPrint(string apiPath, IList<ITikCommandParameter> parameters, bool asJson = false,
                                          string? fromIndices = null)
            => WrapPut(BuildPrintExpression(apiPath, parameters, fromIndices), asJson);

        /// <summary>
        /// The bare print expression <see cref="BuildPrint"/> wraps — <c>/path print … as-value …</c>, with no
        /// <c>:put</c> around it — so a caller can bind it to a variable instead (<see cref="BuildCountedRead"/>).
        /// </summary>
        internal static string BuildPrintExpression(string apiPath, IList<ITikCommandParameter> parameters,
                                                    string? fromIndices = null)
        {
            string cliBase = ApiPathToCli(apiPath);
            var sb = new StringBuilder();
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
            string? numbersValue = FindNameValueParam(parameters, "numbers");
            if (numbersValue != null)
            {
                sb.Append(" numbers=");
                sb.Append(QuoteIfNeeded(numbersValue));
            }
            if (HasNameValueFlag(parameters, "once"))
                sb.Append(" once");

            sb.Append(" as-value");
            AppendFrom(sb, fromIndices);

            string whereClause = BuildWhereClause(parameters);
            if (!string.IsNullOrEmpty(whereClause))
            {
                sb.Append(" where ");
                sb.Append(whereClause);
            }

            return sb.ToString();
        }

        /// <summary>
        /// <c>:put [expression]</c>, or <c>:put [:serialize to=json [expression]]</c> — how a print is made
        /// to write its value to the terminal (see <see cref="BuildPrint"/>).
        /// </summary>
        internal static string WrapPut(string expression, bool asJson)
            => asJson ? ":put [:serialize to=json [" + expression + "]]" : ":put [" + expression + "]";

        /// <summary>
        /// A whole-table read that also states how many records its answer carries: binds the print to a
        /// variable, writes it exactly as <see cref="WrapPut"/> would, then writes one <see cref="CountMarker"/>
        /// line.
        /// </summary>
        /// <remarks>
        /// <para><c>:put $d</c> is byte-identical to <c>:put [expression]</c>, and
        /// <c>:serialize to=json $d</c> to the direct form — both measured on 7.24, on a list menu and a
        /// singleton — so binding the answer changes nothing about how it is parsed.</para>
        /// <para>The marker is <c>#n=&lt;len&gt;/&lt;kind&gt;</c>, and the kind is not decoration. A
        /// singleton's (or a monitor snapshot's) <c>print as-value</c> is ONE record held as a keyed array,
        /// and <c>[:len]</c> of that counts its <b>fields</b> — <c>/system identity</c> gives 1, <c>/ip dns</c>
        /// 19. <c>[:find $d [:pick $d 0]]</c> tells the two apart: a list answers the index <c>0</c>
        /// (<c>num</c>), a keyed array <c>nil</c>. Its first element's own type cannot, since a singleton's
        /// first field can itself be an array. See Docs/findings-cli.md §1 and
        /// <see cref="ExpectedRecordCount"/>.</para>
        /// </remarks>
        internal static string BuildCountedRead(string expression, bool asJson)
            => ":local d [" + expression + "]; "
             + (asJson ? ":put [:serialize to=json $d]; " : ":put $d; ")
             + ":put (\"" + CountMarker + "\" . [:len $d] . \"/\" . [:typeof [:find $d [:pick $d 0]]])";

        /// <summary>Prefix of the trailing line a counted read emits — <c>#n=&lt;len&gt;/&lt;kind&gt;</c>.</summary>
        internal const string CountMarker = "#n=";

        /// <summary>
        /// The number of records a counted read's answer must carry, from its marker's tail
        /// (<c>1672/num</c>), or <c>-1</c> when the tail is not one this builder produces.
        /// </summary>
        /// <remarks>
        /// <c>num</c> is a list, so the length is the row count. Anything else is one keyed record — or none
        /// at all when the length is 0, which is how an empty table answers (<c>0/nil</c>).
        /// </remarks>
        internal static int ExpectedRecordCount(string markerTail)
        {
            string tail = (markerTail ?? string.Empty).Trim();
            int slash = tail.IndexOf('/');
            if (slash <= 0)
                return -1;
            if (!int.TryParse(tail.Substring(0, slash), NumberStyles.None, CultureInfo.InvariantCulture, out int len))
                return -1;
            string kind = tail.Substring(slash + 1);
            if (kind == "num")
                return len;
            if (kind == "nil")
                return len == 0 ? 0 : 1;
            return -1;
        }

        /// <summary>
        /// Appends the <c>from=</c> row selector of a paged read.
        /// </summary>
        /// <remarks>
        /// <para><b>Position matters and the failure is silent.</b> <c>where</c> consumes everything that
        /// follows it, so <c>… as-value where chain=x from=0,1</c> swallows the <c>from=</c> into the
        /// expression and RouterOS answers with the WHOLE table — measured at 1672 rows where 2 were asked
        /// for. Placed before <c>where</c>, both apply: the slice is taken first and the filter is applied
        /// inside it.</para>
        /// <para><c>from=</c> takes a comma-separated list of positional indices or <c>.id</c> values, or an
        /// array of them; a range (<c>from=0-4</c>) is a syntax error, and an index past the last row answers
        /// <c>no such item</c> rather than returning fewer rows. That is why a paged read selects by an array
        /// of ids taken with <c>:pick</c>, which clamps, rather than by positions
        /// (<see cref="BuildPagedWindow"/>). See Docs/findings-cli.md §1.</para>
        /// </remarks>
        private static void AppendFrom(StringBuilder sb, string? fromIndices)
        {
            if (string.IsNullOrEmpty(fromIndices))
                return;
            sb.Append(" from=");
            sb.Append(fromIndices);
        }

        /// <summary>
        /// Wraps one slice of a paged read: takes a window of ids with <c>:pick</c>, prints the rows it
        /// names, and states how many ids the window held.
        /// </summary>
        /// <remarks>
        /// <para><c>:pick</c> is what makes the common case free. It <b>clamps</b> — a window past the end
        /// yields an empty array rather than an error — so a table smaller than one page is answered by the
        /// first request, with no row-count round trip to pay for first. Measured over MAC-Telnet, a two-row
        /// read costs 177 ms this way against 400 ms when a count query precedes it.</para>
        /// <para>The <c>:if</c> is not decoration: <c>from=</c> rejects an <b>empty</b> array outright
        /// (<c>invalid value for argument from</c>), which a table whose size is an exact multiple of the
        /// page would otherwise hit on its last window. Guarding it is better than reading that error as
        /// end-of-data, because a wording that ever covered something else would turn a real failure into a
        /// silently short table.</para>
        /// <para>A filtered read passes its clause in <paramref name="findClause"/>, so the window covers
        /// MATCHING rows only and the print inside it carries no <c>where</c> of its own. Applying the filter
        /// inside an unfiltered window is correct too, but it multiplies the work instead of shrinking it —
        /// 49 of 1672 mangle rows cost 1190 ms that way against 72 ms with the filter in the <c>find</c>
        /// (7.24, router-side, page size 20), and it would break what <see cref="WindowMarker"/> means.</para>
        /// <para><b>The parentheses around the clause are load-bearing, and silently so.</b> A bare
        /// <c>find name=value</c> is parsed as the verb's ARGUMENTS: a leading <c>!</c> is a syntax error
        /// (<c>find !(x)</c>) and an unknown field is refused by name (<c>bad parameter</c>) where
        /// <c>where</c> simply matches nothing. Wrapped in one pair of parentheses the clause is an
        /// EXPRESSION, parsed by the same grammar <c>where</c> uses, and every form
        /// <see cref="BuildWhereClause"/> emits then selects the same rows in the same order as <c>where</c>
        /// — verified on 7.24 by id sequence for equality, <c>!=</c>, <c>&gt;</c>, <c>&lt;</c>, <c>~</c>, a
        /// bare name, <c>=""</c>, <c>(a || b)</c>, <c>(a &amp;&amp; b)</c>, <c>!(a)</c>, a top-level
        /// <c>&amp;&amp;</c> join, a quoted value, an unknown field and no match. See
        /// Docs/findings-cli.md §1.</para>
        /// </remarks>
        internal static string BuildPagedWindow(string apiPath, string printCommand, int offset, int pageSize,
                                                string? findClause = null)
        {
            string menu = MenuPathToCli(apiPath);
            string find = string.IsNullOrEmpty(findClause) ? " find" : " find (" + findClause + ")";
            return ":local w [:pick [" + menu + find + "] "
                 + offset.ToString(CultureInfo.InvariantCulture) + " "
                 + (offset + pageSize).ToString(CultureInfo.InvariantCulture) + "]; "
                 + ":if ([:len $w] > 0) do={ " + printCommand + " }; "
                 + ":put (\"" + WindowMarker + "\" . [:len $w])";
        }

        /// <summary>The selector a paged window's print uses — the window array the wrapper bound.</summary>
        internal const string WindowVariable = "$w";

        /// <summary>
        /// Prefix of the trailing line a paged window emits, carrying how many <b>ids the window held</b>.
        /// </summary>
        /// <remarks>
        /// It is the router's own statement of how many rows the answer must carry — the window's print
        /// never carries a filter of its own, so every id in it names one row — and it ends the loop. The
        /// records parsed from the answer are checked against it rather than trusted in its place: a count
        /// that disagrees means rows were lost on the way, or split by the parser, and the read is refused.
        /// </remarks>
        internal const string WindowMarker = "#w=";

        /// <summary>The menu without its verb — <c>/ip/firewall/mangle/print</c> gives <c>/ip firewall mangle</c>.</summary>
        private static string MenuPathToCli(string apiPath)
        {
            string trimmed = (apiPath ?? string.Empty).TrimEnd('/');
            int lastSlash = trimmed.LastIndexOf('/');
            return ApiPathToCli(lastSlash > 0 ? trimmed.Substring(0, lastSlash) : trimmed);
        }

        /// <summary>
        /// Builds a <c>:put [/path print stats as-value [where …]]</c> command.
        /// Used as the second query in the two-query path for entities with <c>IncludeCliStats</c>.
        /// Does NOT include <c>detail</c> — <c>stats</c> and <c>detail</c> are mutually exclusive
        /// in RouterOS CLI (adding both yields only the stats columns).
        /// </summary>
        /// <param name="apiPath">API-style path of the printed menu.</param>
        /// <param name="parameters">Command parameters (filters, flags, markers).</param>
        /// <param name="asJson">
        /// Applies the same <c>:serialize to=json</c> wrapping as <see cref="BuildPrint"/>, so an entity
        /// asking for both the stats merge and JSON gets JSON for both halves of it.
        /// </param>
        /// <param name="fromIndices">The same row selector as <see cref="BuildPrint"/> takes.</param>
        internal static string BuildPrintStats(string apiPath, IList<ITikCommandParameter> parameters, bool asJson = false,
                                               string? fromIndices = null)
            => WrapPut(BuildPrintStatsExpression(apiPath, parameters, fromIndices), asJson);

        /// <summary>The bare expression <see cref="BuildPrintStats"/> wraps, as <see cref="BuildPrintExpression"/>.</summary>
        internal static string BuildPrintStatsExpression(string apiPath, IList<ITikCommandParameter> parameters,
                                                         string? fromIndices = null)
        {
            string cliBase = ApiPathToCli(apiPath);
            var sb = new StringBuilder();
            sb.Append(cliBase);
            sb.Append(" stats as-value");
            AppendFrom(sb, fromIndices);

            string whereClause = BuildWhereClause(parameters);
            if (!string.IsNullOrEmpty(whereClause))
            {
                sb.Append(" where ");
                sb.Append(whereClause);
            }

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
        /// <param name="apiPath">API-style path of the monitor command.</param>
        /// <param name="parameters">Command parameters — the monitor's inputs.</param>
        /// <param name="snapshotModifier">Per-verb snapshot token; see <see cref="CliMonitorVerbs"/>.</param>
        /// <param name="includeFilters">
        /// Set on the READ path (<c>ExecuteList</c>/<c>LoadList</c> of <c>/ping</c>, <c>/tool/traceroute</c>, …),
        /// where <c>TikGenericCommand.ResolveParamsForRead</c> has already rewritten the caller's Default-format
        /// parameters to Filter format. A monitor has no query semantics, so a Filter parameter here can only be
        /// that rewrite — dropping it is how a ping went out as <c>:put [/ping as-value]</c>. Same
        /// reasoning, and the same flag name, as <see cref="BuildNonQuery"/> uses for <c>/tool/wol</c>.
        /// </param>
        internal static string BuildMonitorSnapshot(string apiPath, IList<ITikCommandParameter> parameters,
            string snapshotModifier, bool includeFilters = false)
        {
            var sb = new StringBuilder(":put [");
            sb.Append(ApiPathToCli(apiPath));
            AppendMonitorInputs(sb, parameters, snapshotModifier, includeFilters);
            sb.Append(" as-value]");
            return sb.ToString();
        }

        /// <summary>
        /// Builds the same monitor command in its <b>bare interactive</b> form — <c>/path &lt;inputs&gt;
        /// &lt;modifier&gt;</c>, with no <c>:put [ … ]</c> wrapper and no <c>as-value</c> — for a caller that
        /// wants the rows AS THEY HAPPEN rather than in one lump at the end.
        /// </summary>
        /// <remarks>
        /// The wrapper is what costs the streaming: <c>:put</c> is handed a completed array, so the router
        /// prints nothing until the command has finished. Measured on 7.23.2, a <c>count=5</c> ping
        /// wrapped in <c>:put [… as-value]</c> emitted its first byte of data at +4019 ms and all five rows
        /// at once; the same ping unwrapped emitted the header at +58 ms and then a row every ~1000 ms.
        /// <para>
        /// The output is therefore RouterOS's fixed-width table rather than an as-value line, and is read by
        /// <see cref="CliTableParser"/>. Callers that want one complete result set and do not care when it
        /// arrives should keep using <see cref="BuildMonitorSnapshot"/> — the as-value form needs no column
        /// arithmetic and is the safer parse.
        /// </para>
        /// </remarks>
        internal static string BuildInteractiveMonitor(string apiPath, IList<ITikCommandParameter> parameters, string snapshotModifier)
        {
            var sb = new StringBuilder(ApiPathToCli(apiPath));
            AppendMonitorInputs(sb, parameters, snapshotModifier);
            return sb.ToString();
        }

        // Appends the monitor's inputs (NameValue parameters; an empty value becomes a bare flag) followed
        // by the snapshot modifier, unless the caller already supplied a parameter of that name.
        // includeFilters: see BuildMonitorSnapshot — on the read path the inputs arrive in Filter format.
        private static void AppendMonitorInputs(StringBuilder sb, IList<ITikCommandParameter> parameters,
            string snapshotModifier, bool includeFilters = false)
        {
            string? modName = string.IsNullOrEmpty(snapshotModifier) ? null : snapshotModifier.Split('=')[0];
            bool modifierAlreadyPresent = false;

            foreach (var p in parameters)
            {
                if (p.ParameterFormat == TikCommandParameterFormat.Filter && !includeFilters)
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
        /// Builds a <c>/path set [find where .id=*N] k=v …</c> command.
        /// </summary>
        internal static string BuildSet(string apiPath, IList<ITikCommandParameter> parameters)
        {
            string cliBase = ApiPathToCli(apiPath);
            var sb = new StringBuilder(cliBase);

            string? idValue = FindIdParam(parameters);
            AppendFindIdentifier(sb, idValue, useNumbers: true);

            AppendNameValueParams(sb, parameters, skipId: true);
            return sb.ToString();
        }

        // ── Remove ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a <c>/path remove numbers=*N</c> command.
        /// </summary>
        internal static string BuildRemove(string apiPath, IList<ITikCommandParameter> parameters)
        {
            return BuildFindVerb(apiPath, parameters, "remove");
        }

        // ── Simple verbs (enable / disable / move / unset / comment / run) ─────

        /// <summary>
        /// Builds <c>/path verb &lt;record selector&gt; [k=v …]</c> for the verbs that address one row.
        /// </summary>
        internal static string BuildSimpleVerb(string apiPath, string verb, IList<ITikCommandParameter> parameters)
        {
            // apiPath already contains the verb as its last segment; `verb` chooses the record selector.
            return BuildFindVerb(apiPath, parameters, verb);
        }

        /// <summary>
        /// Whether <paramref name="verb"/> selects its row with <c>numbers=</c>. The row-addressing verbs
        /// do; an <b>action</b> verb does not, and saying so is not cosmetic — <c>/system/script/run
        /// numbers=*B</c> answers <i>bad parameter numbers (line 1 column 30)</i> while
        /// <c>run [find where .id=*B]</c> runs the script (measured on 7.24, run-count 0 → 1).
        /// </summary>
        /// <remarks>
        /// An allow-list rather than a deny-list, so a verb nobody has measured keeps the older
        /// <c>[find]</c> form — which works for every verb, and merely cannot report a row that is not
        /// there. Wrong in the direction that stays functional.
        /// </remarks>
        private static bool TakesNumbers(string? verb)
        {
            switch (verb)
            {
                case "set":
                case "remove":
                case "enable":
                case "disable":
                case "move":
                case "unset":
                case "comment":
                    return true;
                default:
                    return false;   // action verbs (run, …) and anything unmeasured
            }
        }

        // ── Get ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds <c>:put [/path get [number=*N] [value-name=field]]</c> — the <c>get</c> verb, which the
        /// print builder cannot express.
        /// </summary>
        /// <remarks>
        /// <b>The row is selected with <c>number=</c>, not <c>.id=</c>.</b> Measured on RouterOS 7.24:
        /// <c>get .id=*2 value-name=name</c> answers "bad parameter .id" and so does
        /// <c>get [find .id=*2] name</c>, while <c>get number=*2 value-name=name</c> and the positional
        /// <c>get *2 name</c> both return the value. An <c>.id</c> is accepted as the <c>number=</c>
        /// argument even though the parameter is named for the ordinal — that is what makes this
        /// translation possible at all.
        /// <para>
        /// With no <c>value-name</c> the router returns the whole row as one <c>as-value</c> string, which
        /// is exactly what the binary API puts in <c>=ret=</c> for the same command — so the two transports
        /// answer a whole-row get with the same single value rather than with different shapes.
        /// </para>
        /// <para>
        /// Note that <c>as-value</c> must NOT be appended here: <c>get</c> takes the value name as a
        /// positional argument, so <c>:put [/system identity get as-value]</c> is read as a request for a
        /// field called "as-value" and answered "input does not match any value of value-name".
        /// </para>
        /// </remarks>
        /// <param name="apiPath">API-style path whose last segment is the <c>get</c> verb.</param>
        /// <param name="id">The row's <c>.id</c>, or null/empty for a singleton menu.</param>
        /// <param name="valueName">The field to read, or null/empty for the whole row.</param>
        internal static string BuildGet(string apiPath, string? id, string? valueName)
        {
            string cliBase = ApiPathToCli(apiPath);

            var sb = new StringBuilder(":put [");
            sb.Append(cliBase);
            if (!string.IsNullOrEmpty(id))
            {
                // Not quoted: an id is '*' plus hex and never needs it, and the rest of this builder spells
                // ids bare too ([find where .id=*1]). Both forms are accepted here — measured — but one
                // spelling across the file is worth more than the redundancy.
                sb.Append(" number=");
                sb.Append(id);
            }
            if (!string.IsNullOrEmpty(valueName))
            {
                sb.Append(" value-name=");
                sb.Append(QuoteIfNeeded(valueName!));
            }
            sb.Append(']');
            return sb.ToString();
        }

        // ── NonQuery ──────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a non-query command (system reboot, etc.).  No where-clause, no id find.
        /// Parameters are appended as name=value pairs.
        /// <para>
        /// <paramref name="includeFilters"/> also emits Filter-format parameters as name=value. Needed
        /// when an ACTION is reached through a read method (<c>/tool/wol</c> via
        /// <c>ExecuteSingleRowOrDefault</c>): <c>TikGenericCommand.ResolveParamsForRead</c> rewrites
        /// Default-format parameters to Filter, so the action's own inputs arrive here as filters.
        /// A where-clause is meaningless for an action, so there is nothing to confuse them with.
        /// Without this the inputs are silently dropped and RouterOS PROMPTS for the missing value
        /// (<c>mac: </c>) — over a terminal that is an interactive hang, not an error.
        /// </para>
        /// </summary>
        internal static string BuildNonQuery(string apiPath, IList<ITikCommandParameter> parameters, bool includeFilters = false)
        {
            string cliBase = ApiPathToCli(apiPath);
            var sb = new StringBuilder(cliBase);
            AppendNameValueParams(sb, parameters, includeFilters: includeFilters);
            return sb.ToString();
        }

        // ── Where clause ──────────────────────────────────────────────────────

        /// <summary>
        /// Builds a <c>where name=value &amp;&amp; …</c> clause from Filter-format parameters.
        /// Supports negation (<c>!value</c>), comparison (<c>&gt;value</c>, <c>&lt;value</c>),
        /// and regex (<c>~pattern</c>) prefixes.
        /// <para>The same text serves as a window's <c>find</c> clause, where it is parenthesised rather
        /// than introduced by <c>where</c> — see <see cref="BuildPagedWindow"/>.</para>
        /// </summary>
        internal static string BuildWhereClause(IList<ITikCommandParameter> parameters)
        {
            // The filters are a postfix STACK, not a list of things to AND: the API's query words
            // '?#|', '?#&' and '?#!' combine the two (or one) predicates before them. Joining everything
            // with '&&' answered a different question — '?type=ether ?type=loopback ?#|' asks for either
            // and was sent as 'where type=ether && type=loopback', which no row can satisfy. RouterOS
            // spells the combinations '(a || b)', '(a && b)' and '!(a)'; all three verified on 7.24.
            //
            // The same evaluation the WinBox-native transport does in memory (TikQueryStack), rendered as
            // text instead of applied to a row, so a query means the same thing on both.
            var stack = new List<string>();
            foreach (var p in parameters)
            {
                if (p.ParameterFormat != TikCommandParameterFormat.Filter)
                    continue;

                string name = p.Name;
                string? val = p.Value;   // null and "" mean different things here — see BuildCondition

                if (IsSpecialParam(name))
                    continue;

                if (name == "#|" || name == "#&")
                {
                    string b = PopCondition(stack, name), a = PopCondition(stack, name);
                    stack.Add("(" + a + (name == "#|" ? " || " : " && ") + b + ")");
                    continue;
                }
                if (name == "#!")
                {
                    stack.Add("!(" + PopCondition(stack, name) + ")");
                    continue;
                }
                // Any other '#…' is a stack word this does not implement. Left alone rather than treated
                // as a field name, exactly as TikQueryStack leaves it — inventing 'where #x=…' would send
                // the router a predicate on a property that does not exist.
                if (name.StartsWith("#"))
                    continue;

                string condition = BuildCondition(name, val);
                if (!string.IsNullOrEmpty(condition))
                    stack.Add(condition);
            }

            // Whatever is left unconsumed is ANDed, which is what a query with no operators at all means.
            return string.Join(" && ", stack);
        }

        /// <summary>
        /// Pops the operand an operator needs, or refuses: an operator with nothing under it cannot be
        /// rendered, and guessing would send the router a clause the caller did not write.
        /// </summary>
        private static string PopCondition(List<string> stack, string op)
        {
            if (stack.Count == 0)
                throw new ArgumentException(
                    $"query operator '?{op}' has no predicate to apply it to. The filters form a postfix "
                    + "stack: the operands come first, then the operator.", nameof(op));
            string top = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            return top;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string BuildCondition(string name, string? val)
        {
            // A NULL value is the binary API's '?name' — "the property is set" — which the CLI spells as the
            // bare field name. An EMPTY value is '?name=' — "the property equals the empty string" — and is
            // spelled 'name=""'. Emitting the bare name for both INVERTED the empty case: measured on
            // 7.23.2 against two /system/script rows (one with a comment, one without), 'where comment'
            // returns the row that HAS a comment, while '?comment=' over the binary API and
            // 'where comment=""' over the CLI both return none.
            if (val == null)
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
        /// <para>The safe set is <see cref="IsSafeUnquoted"/>, shared with <see cref="QuoteIfNeeded"/>:
        /// a name=value argument turned out to need exactly the same treatment, for a different reason —
        /// there the router parses the value by the PARAMETER'S type, and a script-typed one reads
        /// punctuation as code. Two contexts, one rule, so they cannot drift apart.</para>
        /// <para>
        /// The safe set excludes <c>$</c>, so a value containing one is always quoted — and inside
        /// those quotes it would be substituted away, silently matching the wrong rows. Escaping is
        /// therefore shared with <see cref="QuoteIfNeeded"/>.
        /// </para>
        /// </summary>
        internal static string QuoteForWhere(string value)
        {
            // Empty → the explicit empty literal, for the same reason as in QuoteIfNeeded: a bare
            // 'where comment=' does not parse. (A null never reaches here — BuildCondition emits the bare
            // field name for it, which is the CLI's "property is set" test.)
            if (string.IsNullOrEmpty(value))
                return value == null ? null! : "\"\""; // value is never actually null here (see comment above)

            bool safe = true;
            foreach (char c in value)
            {
                if (!IsSafeUnquoted(c)) { safe = false; break; }
            }

            if (safe)
                return value;

            return "\"" + EscapeInsideQuotes(value) + "\"";
        }

        private static string BuildFindVerb(string apiPath, IList<ITikCommandParameter> parameters, string? verb = null)
        {
            string cliBase = ApiPathToCli(apiPath);
            var sb = new StringBuilder(cliBase);

            string? idValue = FindIdParam(parameters);
            AppendFindIdentifier(sb, idValue, TakesNumbers(verb));

            // Append any remaining NameValue params (e.g. destination for move)
            AppendNameValueParams(sb, parameters, skipId: true);
            return sb.ToString();
        }

        /// <summary>
        /// Client-side marker parameters that must NEVER be emitted as a CLI word — they are stripped
        /// from where-clauses and from name=value lists. (Mirror of <c>RestRequestBuilder.IsSpecialParam</c>
        /// / <c>ApiCommand.IsSpecialParam</c>; the membership differs per transport on purpose.)
        ///   <c>.proplist</c> — honoured by trimming the rows client-side (<see cref="CliConnectionBase"/>), never as
        ///                    the CLI's own <c>proplist=</c>: that refuses the whole read when one name is
        ///                    unknown ("input does not match any value of value-name", 7.24), where the API
        ///                    ignores the name.
        ///   <c>.tag</c>      — no tag protocol over a terminal.
        ///   <c>.cli-stats</c> — CLI-layer signal that triggers the two-query stats merge (<see cref="CliConnectionBase"/>).
        ///   <c>.cli-json</c>  — CLI-layer signal that switches the read to <c>:serialize to=json</c> (same class).
        /// NOTE: this is the "dropped" set. <c>detail</c> / <c>once</c> / <c>numbers</c> are a DIFFERENT
        /// category — "consumed flags" that <see cref="BuildPrint"/> translates into print modifiers
        /// (via <see cref="HasNameValueFlag"/> / <see cref="FindNameValueParam"/>), not dropped.
        /// </summary>
        private static bool IsSpecialParam(string name)
            => name == TikSpecialProperties.Proplist
            || name == TikSpecialProperties.Tag
            || name == TikSpecialProperties.CliStats
            || name == TikSpecialProperties.CliJson;

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
        private static string? FindNameValueParam(IList<ITikCommandParameter> parameters, string name)
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
        /// Appends the record identifier for set/remove/enable/disable/move/unset. A real <c>.id</c>
        /// (<c>*N</c>) uses <c>numbers=*N</c>; any other value uses <c>[find where .id=X or name=X]</c> so a
        /// NAME works too (<c>.id=ether1</c> alone matches nothing — the literal <c>.id</c> is <c>*N</c> —
        /// but <c>name=ether1</c> resolves it). Names-as-id are accepted directly by the binary
        /// API/native transports; this bridges the CLI gap. The <c>or name=…</c> clause is harmless on
        /// tables without a <c>name</c> field (it simply never matches).
        /// <para>
        /// <b><c>numbers=</c> rather than <c>[find where .id=…]</c>, because a <c>[find]</c> that matches
        /// nothing is not an error.</b> Measured on RouterOS 7.24: <c>remove [find where .id=*7FFFFFFF]</c>
        /// prints nothing and the CLI reports success, while <c>remove numbers=*7FFFFFFF</c> answers
        /// <c>no such item (4)</c> — the same thing the binary API and REST say. A terminal has no error
        /// channel other than the text it prints, so under <c>[find]</c> there is nothing for
        /// <c>CliErrorParser</c> to raise on and a <c>Save</c> or <c>Delete</c> against a row that has
        /// gone away <b>would report success</b> on all five CLI transports — worst on the menus whose
        /// rows come and go on their own, where that is the expected case. <c>numbers=</c> takes an
        /// <c>.id</c> despite being named for the ordinal — the same thing that makes the <c>get</c>
        /// translation above possible.
        /// </para>
        /// <para>
        /// The name branch keeps <c>[find]</c>: <c>numbers=</c> only accepts a name on a menu that HAS a
        /// <c>name</c> field (<c>/interface set numbers=ether1</c> works, and an unknown name answers
        /// <i>no such item</i>), but on a nameless menu it is a syntax error
        /// (<c>/ip firewall filter remove numbers=nosuchname</c> → <i>syntax error (line 1 column 36)</i>),
        /// and the builder cannot know which kind of menu it is addressing. A name that resolves to
        /// nothing is consequently still silent — the O/R mapper never takes this branch, since it always
        /// addresses a row by <c>.id</c>.
        /// </para>
        /// <para>
        /// Where <c>[find]</c> is still emitted, the explicit <c>where</c> keyword is required as of
        /// RouterOS 7.24 — a bare <c>[find .id=X]</c> is rejected with "bad parameter .id"/"bad parameter
        /// name" even for a well-formed <c>*N</c> id. Earlier versions (confirmed on 7.21.4) accepted both
        /// forms, so always emitting <c>where</c> keeps compatibility across the range.
        /// </para>
        /// </summary>
        private static void AppendFindIdentifier(StringBuilder sb, string? idValue, bool useNumbers)
        {
            if (string.IsNullOrEmpty(idValue))
                return;
            // netstandard2.0's string.IsNullOrEmpty isn't annotated NotNullWhen, so the compiler can't narrow.
            if (useNumbers && idValue!.StartsWith("*"))
            {
                sb.Append(" numbers=");
                sb.Append(idValue);
            }
            else if (idValue!.StartsWith("*"))
            {
                sb.Append(" [find where .id=");
                sb.Append(idValue);
                sb.Append(']');
            }
            else
            {
                string q = QuoteIfNeeded(idValue)!; // idValue is non-null/non-empty here (guarded above), so QuoteIfNeeded's null-input branch cannot apply
                sb.Append(" [find where .id=");
                sb.Append(q);
                sb.Append(" or name=");
                sb.Append(q);
                sb.Append(']');
            }
        }

        private static string? FindIdParam(IList<ITikCommandParameter> parameters)
        {
            foreach (var p in parameters)
            {
                if (p.Name == TikSpecialProperties.Id)
                    return p.Value;
            }
            return null;
        }

        private static void AppendNameValueParams(StringBuilder sb, IList<ITikCommandParameter> parameters,
            bool skipId = false, bool includeFilters = false)
        {
            foreach (var p in parameters)
            {
                if (p.ParameterFormat == TikCommandParameterFormat.Filter && !includeFilters)
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

        private static bool IsTruthy(string? value)
            => string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Wraps a value in double-quotes unless every character of it is plainly safe bare
        /// (<see cref="IsSafeUnquoted"/>), and escapes the characters that are special *inside* those
        /// quotes.
        /// <para>
        /// Measured on RouterOS 7.23.2 (see <c>Docs/findings-cli.md</c>): inside a double-quoted
        /// value <c>$</c> starts variable substitution and <c>\</c> starts an escape sequence, so an
        /// unescaped value is silently rewritten by the router before it is ever stored —
        /// <c>source="x$y z"</c> lands as <c>x z</c>, <c>"C:\temp\new"</c> lands as
        /// <c>C:&lt;TAB&gt;emp&lt;LF&gt;ew</c>. An escape the router does not know (<c>\y</c>) is a
        /// syntax error instead. Outside quotes both characters are always a syntax error, so a value
        /// carrying either MUST be quoted as well — hence they are in the trigger set below.
        /// </para>
        /// <para>Escapes, in this order (backslash first, or it would double the ones we add):
        /// <c>\</c> → <c>\\</c>, <c>"</c> → <c>\"</c>, <c>$</c> → <c>\$</c>. A literal newline/tab is
        /// left as a real character: RouterOS accepts a line break inside an open quoted value (that
        /// is how a multi-line script source round-trips today), and rewriting it to <c>\n</c> would
        /// be indistinguishable from a value that really carries a backslash and an <c>n</c>. CR/LF
        /// are in the trigger set for the opposite reason — *unquoted* they would end the command
        /// line and the tail would be executed as one.</para>
        /// </summary>
        internal static string? QuoteIfNeeded(string? value)
        {
            // An EMPTY value must be written as the two-character empty literal — a bare 'name=' is a
            // RouterOS syntax error the moment anything follows it on the line ("/system note set note=
            // show-at-login=yes" → "expected end of command (line 1 column 37)"), and even where it is
            // accepted (last argument on the line) the intent is clearer spelled out. This is how clearing a
            // field over a CLI transport failed: nothing in the suite ever saved an empty string, so the
            // trap only surfaced when a round-trip test restored a field that started out empty.
            if (string.IsNullOrEmpty(value))
                return value == null ? null : "\"\"";

            bool needsQuote = false;
            // netstandard2.0's string.IsNullOrEmpty isn't annotated NotNullWhen, so the compiler can't narrow.
            foreach (char c in value!)
            {
                if (!IsSafeUnquoted(c))
                {
                    needsQuote = true;
                    break;
                }
            }

            if (!needsQuote)
                return value;

            return "\"" + EscapeInsideQuotes(value) + "\"";
        }

        /// <summary>
        /// Whether <paramref name="c"/> can stand in an unquoted CLI value.
        /// </summary>
        /// <remarks>
        /// An ALLOW-list, not a deny-list, and that is the point. RouterOS parses an unquoted value
        /// according to the PARAMETER'S TYPE, so which characters are legal is not a property of the CLI
        /// grammar that could be enumerated once - <c>address=10.0.0.0/24</c> is fine because that
        /// parameter is an IP prefix, while <c>source=a/bc</c> is a syntax error because
        /// <c>/system/script</c>'s <c>source</c> is script-typed and the router parses the value as CODE.
        /// Measured on RouterOS 7.24 against a script-typed parameter, every one of
        /// <c>: [ ] ( ) { } ' ? ! ~ &lt; &gt; | &amp; , * / + =</c> breaks it, leading or mid-value. The
        /// deny-list this replaced listed nine characters and caught none of them.
        /// <para>Quoting costs nothing: the same values quoted round-trip unchanged through an IP prefix,
        /// an interface reference, a bool, an enum and <c>numbers=</c> with a <c>*</c>-id, each verified on
        /// the router. So anything that is not plainly a bare word gets quotes, and a parameter type never
        /// seen before cannot produce a value that is silently mis-sent.</para>
        /// <para><c>-</c>, <c>_</c> and <c>.</c> stay unquoted because nearly every RouterOS value is built
        /// from them (<c>wpa2-psk</c>, <c>ether2</c>, <c>1.5</c>, <c>10.99.0.10-10.99.0.20</c>), and
        /// quoting all of those would make every wire trace unreadable for no gain.</para>
        /// </remarks>
        private static bool IsSafeUnquoted(char c)
            => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
            || c == '-' || c == '_' || c == '.';

        /// <summary>
        /// Escapes the three characters that are special inside a RouterOS double-quoted string.
        /// Shared by <see cref="QuoteIfNeeded"/> (name=value arguments) and
        /// <see cref="QuoteForWhere"/> (where-clause operands) so the two cannot drift apart.
        /// </summary>
        private static string EscapeInsideQuotes(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("$", "\\$");
        }
    }
}
