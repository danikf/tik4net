using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Connection;
using tik4net.Diagnostics;

namespace tik4net.Cli
{
    /// <summary>
    /// What a read method runs over a terminal: it picks the shape of the read from its verb, and sends and parses it.
    /// The windowed and counted print is <see cref="CliPagedReader"/>; what is learnt about the router lives in
    /// <see cref="CliRouterKnowledge"/>. One per connection.
    /// </summary>
    internal sealed class CliPrintDriver
    {
        private readonly ICliCommandHost _host;
        private readonly CliFlagSupplier _flags;
        private readonly CliPagedReader _pagedReader;

        internal CliPrintDriver(ICliCommandHost host, CliFlagSupplier flags, CliPagedReader pagedReader)
        {
            _host = host;
            _flags = flags;
            _pagedReader = pagedReader;
        }

        /// <summary>
        /// Executes a <c>print as-value</c> command and returns parsed sentences.
        /// When the <c>.cli-stats</c> marker is present in the descriptor's parameters,
        /// performs two queries (detail + stats) and merges the results by <c>.id</c>
        /// so that config fields and live counter fields are combined in each record.
        /// </summary>
        internal async Task<IList<TikRecordSentence>> RunPrintAsync(
            TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            IList<TikRecordSentence> rows = await RunPrintCoreAsync(descriptor, cancellationToken).ConfigureAwait(false);
            // The spellings that depend on the menu as well as the field (bridge priorities): the parse that
            // built the rows knows only field names. A raw command's text is a CLI line, not a menu path.
            if (!descriptor.IsRaw)
                CliValueNormalizer.NormalizeForMenu(descriptor.CommandText, rows);
            return rows;
        }

        private async Task<IList<TikRecordSentence>> RunPrintCoreAsync(
            TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            _host.EnsureOpened();

            // Raw pass-through (CreateRawCommand): send the command line verbatim — no CliCommandBuilder,
            // no path→CLI rewrite, no where-clause. The caller is responsible for as-value materialisation;
            // wrapAsValue:true wraps it in ':put [ … as-value]' as a convenience so the output parses.
            if (descriptor.IsRaw)
            {
                string rawCli = descriptor.WrapAsValue
                    ? CliConnectionBase.WrapRawAsValue(descriptor.CommandText)
                    : descriptor.CommandText;
                string rawOutput = await _host.ExecuteAsync(rawCli, cancellationToken).ConfigureAwait(false);
                // A command that did not run must not come back as an empty table: 0 rows is
                // indistinguishable from a table with nothing in it. Same check the low-level
                // CallCommandSync applies, so the two halves of the raw promise agree about failure.
                CliErrorParser.ThrowIfError(rawOutput, _host.CommandFor(descriptor));
                return CliOutputParser.ParseAsValue(rawOutput);
            }

            // Action verbs (e.g. /system/script/run) perform an action and produce no result set over a
            // terminal (no per-record !re output, unlike the binary API) — they belong on the non-query
            // path. Reject them on the read path so the misuse is explicit instead of silently returning an
            // empty list; invoke them via ExecuteNonQuery (RunNonQuery handles the 'run' verb).
            string printVerb = TikPath.Verb(descriptor.CommandText);
            if (IsActionVerb(printVerb))
                throw ActionVerbOnReadPath(descriptor.CommandText);

            // Actions that the binary API answers with an empty '!re' row (verified live for /tool/wol:
            // "!re" with no words, then "!done"). Consumers therefore legitimately reach them through a
            // read method — ToolWol.ExecuteWol uses ExecuteSingleRowOrDefault — so throwing here would
            // punish correct code. Route them to the non-query builder instead and return no rows;
            // 'at most one row' is what the callers are written against. This is the opposite treatment
            // from IsActionVerb ('run'), where a read call really is consumer error.
            // Without this, BuildPrint silently DROPS the name=value inputs (mac= is not a print
            // modifier) and appends 'as-value', yielding ':put [/tool wol as-value]' — the router then
            // rejects it for a missing mac.
            if (IsEmptyRowAction(printVerb))
            {
                // includeFilters: the read path rewrote the action's own inputs (mac=, interface=) to
                // Filter format — see CliCommandBuilder.BuildNonQuery.
                string actionText = CliCommandBuilder.BuildNonQuery(
                    descriptor.CommandText, descriptor.Parameters, includeFilters: true);
                string actionOutput = await _host.ExecuteAsync(actionText, cancellationToken).ConfigureAwait(false);
                // An empty-row action (wol) is silent when it succeeds and prints its complaint when it does
                // not — e.g. "input does not match any value of interface", which no phrase list caught, so
                // WolWithInvalidInterfaceWillFail saw a failed send reported as success.
                CliErrorParser.ThrowIfError(actionOutput, _host.CommandFor(descriptor), silentOnSuccess: true);
                return new List<TikRecordSentence>();
            }

            // Monitor commands (/ping, /tool/traceroute, /interface/monitor-traffic, …) reached through a READ
            // method. Their parameters are the command's own INPUTS, but BuildPrint only understands print
            // modifiers and a 'where' clause, so it dropped every one of them: '/ping =address=127.0.0.1
            // =count=2' went out as ':put [/ping as-value]' → "failure: resolve failed". The async path
            // never had the bug — it has always used BuildMonitorSnapshot, which is what is used here too.
            if (CliMonitorVerbs.IsSyncMonitorVerb(printVerb))
                return await RunMonitorSnapshotAsync(descriptor, printVerb, cancellationToken).ConfigureAwait(false);

            // The `get` verb. BuildPrint cannot express it: it drops the .id and value-name inputs (neither
            // is a print modifier) and appends as-value, so `/interface/get =.id=*2 =value-name=name` went
            // out as `:put [/interface get as-value]` — a singleton read of a list menu, answered "no such
            // item". The singleton form was broken the same way, and worse: `as-value` landed where the
            // value name goes, so `/system/identity/get =value-name=name` was refused with "input does not
            // match any value of value-name". The binary API honours both, and ITikCommand is the level
            // whose whole promise is that one command runs everywhere, so this is ours to translate.
            if (printVerb == "get")
                return await RunGetAsync(descriptor, cancellationToken).ConfigureAwait(false);

            // A command whose rows answer a question its own arguments ask — /console/inspect request=child
            // path=ip,route,add. BuildPrint knows only print modifiers and 'where', so it dropped request= and
            // path= and sent ':put [/console inspect as-value]', which the router answered with no rows: an empty
            // answer indistinguishable from a real one. The monitor snapshot passes the arguments through; with no
            // modifier it is ':put [/console inspect request=child path=ip,route,add as-value]' (7.24.4: 14 rows).
            if (IsArgumentRead(printVerb))
                return await RunArgumentReadAsync(descriptor, cancellationToken).ConfigureAwait(false);

            // .proplist: the fields the caller asked for, and only those — the binary API's contract. It never
            // reaches the wire: the CLI's own proplist= refuses the whole read when one name is unknown
            // ("input does not match any value of value-name", 7.24) where the API ignores the name. So the
            // read asks for the full field set and the base trims the rows. A plain print is the SUMMARY
            // columns only (/interface/ethernet omits disable-running-check), which is why 'detail' is added;
            // a singleton menu refuses it ("bad parameter detail") and prints every field without it.
            var proplist = descriptor.Parameters.FirstOrDefault(p => p.Name == TikSpecialProperties.Proplist);
            if (proplist != null)
            {
                var rest = descriptor.Parameters.Where(p => p.Name != TikSpecialProperties.Proplist).ToList();
                // A field the caller named that the plain read leaves out — a flag, on RouterOS before 7.20 — is
                // read by name, as the API returns it (SupplyFlagsAsync; it asks only for names no row carries).
                string named = string.Join(",", (proplist.Value ?? string.Empty).Split(',')
                    .Select(n => n.Trim()).Where(n => n.Length > 0 && n[0] != '.'));
                if (named.Length > 0 && !rest.Any(p => p.Name == TikSpecialProperties.CliFlags))
                    rest.Add(new TikCommandParameter(TikSpecialProperties.CliFlags, named, TikCommandParameterFormat.NameValue));
                var plain = new TikCommandDescriptor(descriptor.CommandText, rest);
                IList<TikRecordSentence> rows;
                if (rest.Any(p => p.ParameterFormat != TikCommandParameterFormat.Filter
                                  && string.Equals(p.Name, "detail", StringComparison.OrdinalIgnoreCase)))
                {
                    rows = await RunPrintAsync(plain, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var withDetail = new List<ITikCommandParameter>(rest)
                    {
                        new TikCommandParameter("detail", "", TikCommandParameterFormat.NameValue),
                    };
                    try
                    {
                        rows = await RunPrintAsync(new TikCommandDescriptor(descriptor.CommandText, withDetail),
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (TikConnectionResponseIncompleteException ex) when (IsDetailRefusal(ex.PartialResponse))
                    {
                        rows = await RunPrintAsync(plain, cancellationToken).ConfigureAwait(false);
                    }
                    catch (TikCommandTrapException ex) when (IsDetailRefusal(ex.Message))
                    {
                        // The refusal is a parse error ("… (line 1 column 27)"), which a counted read now reports
                        // as the router's error rather than as a lost count — see ThrowIfSyntaxError.
                        rows = await RunPrintAsync(plain, cancellationToken).ConfigureAwait(false);
                    }
                }
                return rows;   // the base trims them to .proplist (TikProplist)
            }

            bool needStats = descriptor.Parameters.Any(p => p.Name == TikSpecialProperties.CliStats);
            bool wantJson = descriptor.Parameters.Any(p => p.Name == TikSpecialProperties.CliJson);

            // show-sensitive: RouterOS 7 hides secrets from a terminal print unless asked. RouterOS 6 has no such
            // word ("expected end of command", 6.49.13) and prints them anyway, so a menu that refuses it is read
            // without it — and not asked again on this connection. Remembered only once the plain read succeeds:
            // a syntax error the word did not cause fails that read too, and is the caller's to see.
            bool wantSensitive = descriptor.Parameters.Any(p => p.Name == TikSpecialProperties.CliSensitive);
            if (wantSensitive && _host.Knowledge.MenuFacts.ShowSensitiveRefused.Contains(descriptor.CommandText))
            {
                descriptor = WithoutParameter(descriptor, TikSpecialProperties.CliSensitive);
                wantSensitive = false;
            }

            if (_host.Knowledge.MenuFacts.PrintWithoutAsValue.Contains(descriptor.CommandText))
                return await RunPlainTablePrintAsync(descriptor, cancellationToken).ConfigureAwait(false)
                    ?? throw new TikCommandTrapException(_host.CommandFor(descriptor),
                        new TikTrapSentenceResult("the plain print of " + descriptor.CommandText + " printed no table"));

            IList<TikRecordSentence> records;
            try
            {
                records = await RunPrintQueryAsync(descriptor, wantJson,
                    (pars, from) => CliCommandBuilder.BuildPrintExpression(descriptor.CommandText, pars, from),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (TikCommandTrapException ex) when (CliErrorParser.RefusedArgumentLine.IsMatch(ex.Message.Trim())
                                                     && descriptor.Parameters.All(p => p.Name.StartsWith(".", StringComparison.Ordinal)))
            {
                // A print with no as-value: RouterOS 6's '/routing bgp advertisements print' completes only file,
                // interval, peer and where, so 'as-value' is taken for a peer name ("input does not match any value
                // of peer", 6.49.13). The plain print is its table, read by column as a monitor's is; the menu is
                // remembered for the connection once that table has answered. Only for an unfiltered read — the
                // table cannot carry the caller's arguments — and never when the plain form printed no header.
                var table = await RunPlainTablePrintAsync(descriptor, cancellationToken).ConfigureAwait(false);
                if (table == null)
                    throw;
                _host.Knowledge.LearnFact(_host.Knowledge.MenuFacts.PrintWithoutAsValue, CliRouterKnowledge.FactPrintWithoutAsValue, descriptor.CommandText);
                return table;
            }
            catch (TikCommandTrapException ex) when (wantSensitive && CliErrorParser.SyntaxErrorLine.IsMatch(ex.Message.Trim()))
            {
                var plain = WithoutParameter(descriptor, TikSpecialProperties.CliSensitive);
                records = await RunPrintQueryAsync(plain, wantJson,
                    (pars, from) => CliCommandBuilder.BuildPrintExpression(plain.CommandText, pars, from),
                    cancellationToken).ConfigureAwait(false);
                _host.Knowledge.LearnFact(_host.Knowledge.MenuFacts.ShowSensitiveRefused, CliRouterKnowledge.FactShowSensitiveRefused, descriptor.CommandText);
                descriptor = plain;
            }

            if (needStats)
            {
                // Two-query path: detail (config) + stats (counters), merged by .id.
                IList<TikRecordSentence> statsRecords = await RunPrintQueryAsync(descriptor, wantJson,
                    (pars, from) => CliCommandBuilder.BuildPrintStatsExpression(descriptor.CommandText, pars, from),
                    cancellationToken).ConfigureAwait(false);
                records = MergeById(records, statsRecords);
            }

            if (wantJson && _host.Knowledge.Features.SerializeJson == false)
                records = await RereadFreeTextAsync(descriptor, records, cancellationToken).ConfigureAwait(false);

            return await _flags.SupplyFlagsAsync(descriptor, records, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// On a router without <c>:serialize</c> (before 7.13) the free-text fields the <see cref="TikSpecialProperties.CliJson"/>
        /// marker names are read again, row by row, with <c>get</c>: the <c>as-value</c> read cut a script source at its
        /// first <c>;</c> and ran the rest into further fields (<c>:local a 1</c> for a three-line source, 6.49.13).
        /// <c>get</c> prints the value as stored, <c>;</c>, <c>=</c>, <c>$</c> and <c>\</c> included, and a marker behind it
        /// keeps its trailing line breaks, which the terminal read would otherwise trim
        /// (<see cref="CliCommandBuilder.BuildFreeTextQuery"/>). The terminal ends its lines with CRLF, so a stored CRLF
        /// reads as LF.
        /// </summary>
        /// <remarks>
        /// One command per row and field — affordable for the menus that carry free text (scripts, schedulers, files,
        /// profiles), and only on such a router. A field the menu refuses is left as the plain read gave it.
        /// </remarks>
        private async Task<IList<TikRecordSentence>> RereadFreeTextAsync(TikCommandDescriptor descriptor,
            IList<TikRecordSentence> records, CancellationToken cancellationToken)
        {
            string[] fields = (descriptor.Parameters.First(p => p.Name == TikSpecialProperties.CliJson).Value ?? string.Empty)
                .Split(',').Select(f => f.Trim()).Where(f => f.Length > 0).ToArray();
            if (fields.Length == 0 || records.Count == 0)
                return records;

            var result = new List<TikRecordSentence>(records.Count);
            foreach (var row in records)
            {
                string? id = row.GetResponseFieldOrDefault(TikSpecialProperties.Id, null);
                var words = row.Words.ToDictionary(w => w.Key, w => w.Value, StringComparer.OrdinalIgnoreCase);
                foreach (string field in fields)
                {
                    string output;
                    try
                    {
                        output = await _host.ExecuteAsync(
                            CliCommandBuilder.BuildFreeTextQuery(descriptor.CommandText, id ?? string.Empty, field),
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (TikCommandException)
                    {
                        continue;
                    }

                    // value + "\n" + marker; nothing at all for an empty value; anything else is the router refusing.
                    string text = output.Replace("\r\n", "\n");
                    string end = "\n" + CliCommandBuilder.FreeTextEnd;
                    if (text.EndsWith(end, StringComparison.Ordinal))
                        words[field] = text.Substring(0, text.Length - end.Length);
                    else if (text.Trim().Length == 0 && words.ContainsKey(field))
                        words[field] = string.Empty;
                }
                result.Add(new TikRecordSentence(words));
            }
            return result;
        }

        /// <summary>
        /// Adds to each record of <paramref name="primary"/> the fields it lacks from the record of
        /// <paramref name="extra"/> with the same <c>.id</c>. A field <paramref name="primary"/> already has is
        /// kept; a record with no counterpart is kept as it is.
        /// </summary>
        internal static IList<TikRecordSentence> MergeById(IList<TikRecordSentence> primary,
            IList<TikRecordSentence> extra)
        {
            var extraById = new Dictionary<string, TikRecordSentence>(StringComparer.OrdinalIgnoreCase);
            foreach (var er in extra)
            {
                string? id = er.GetResponseFieldOrDefault(TikSpecialProperties.Id, null);
                if (id != null)
                    extraById[id] = er;
            }
            if (extraById.Count == 0)
                return primary;

            var merged = new List<TikRecordSentence>(primary.Count);
            foreach (var row in primary)
            {
                string? id = row.GetResponseFieldOrDefault(TikSpecialProperties.Id, null);
                if (id == null || !extraById.TryGetValue(id, out TikRecordSentence? er))
                {
                    merged.Add(row);
                    continue;
                }

                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in row.Words)
                    fields[kv.Key] = kv.Value;
                foreach (var kv in er.Words)
                    if (!fields.ContainsKey(kv.Key))
                        fields[kv.Key] = kv.Value;
                merged.Add(new TikRecordSentence(fields));
            }
            return merged;
        }

        private static TikCommandDescriptor WithoutParameter(TikCommandDescriptor descriptor, string name)
            => new TikCommandDescriptor(descriptor.CommandText, descriptor.Parameters.Where(p => p.Name != name).ToList());

        /// <summary>
        /// Whether a counted read failed because the menu has no <c>detail</c> modifier — a singleton answers
        /// <c>bad parameter detail (line 1 column 27)</c> and no count marker.
        /// </summary>
        private static bool IsDetailRefusal(string? response)
            => response != null && response.IndexOf("bad parameter detail", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Runs one monitor snapshot synchronously and returns its records — the read-method counterpart of
        /// <see cref="CliMonitorDriver"/>'s poll loop's single iteration.
        /// </summary>
        /// <remarks>
        /// <c>includeFilters</c> is what makes this correct on the read path: by the time a descriptor gets
        /// here, <c>TikGenericCommand.ResolveParamsForRead</c> has rewritten the caller's parameters to Filter
        /// format, and a monitor has no query semantics for them to mean anything else.
        /// </remarks>
        private Task<IList<TikRecordSentence>> RunMonitorSnapshotAsync(
            TikCommandDescriptor descriptor, string verb, CancellationToken cancellationToken)
            => RunMonitorSnapshotAsync(descriptor, CliMonitorVerbs.SnapshotModifier(verb), includeFilters: true,
                cancellationToken);

        /// <summary>
        /// One monitor snapshot — the as-value form, or on a router that refuses it (RouterOS 6) the plain table —
        /// for a one-shot read and for each round of a polled stream alike.
        /// </summary>
        internal async Task<IList<TikRecordSentence>> RunMonitorSnapshotAsync(TikCommandDescriptor descriptor,
            string modifier, bool includeFilters, CancellationToken cancellationToken)
        {
            await _host.Knowledge.EnsureSchemaStoreAsync(cancellationToken).ConfigureAwait(false);
            if (!_host.Knowledge.MenuFacts.MonitorWithoutAsValue.Contains(descriptor.CommandText))
            {
                string cliText = CliCommandBuilder.BuildMonitorSnapshot(
                    descriptor.CommandText, descriptor.Parameters, modifier, includeFilters);

                string output = await _host.ExecuteAsync(cliText, cancellationToken).ConfigureAwait(false);
                if (!CliErrorParser.IsSyntaxErrorOnly(output))
                {
                    CliErrorParser.ThrowIfError(output, _host.CommandFor(descriptor));
                    return ParseRecords(output, descriptor);
                }
            }

            // RouterOS 6 has no as-value on its monitors: 6.49.13 answers ':put [/ping address=… count=2 as-value]'
            // with "expected end of command (line 1 column 43)" — column 43 is as-value — and traceroute the same.
            // The plain command prints its table, read by column like a streaming monitor. Remembered per command
            // once the plain form has answered with its table, so a syntax error the inputs caused is not.
            string plainText = CliCommandBuilder.BuildInteractiveMonitor(
                descriptor.CommandText, descriptor.Parameters, modifier, includeFilters);
            string plain = await _host.ExecuteAsync(plainText, cancellationToken).ConfigureAwait(false);
            CliErrorParser.ThrowIfError(plain, _host.CommandFor(descriptor));

            var table = new CliTableParser();
            var rows = new List<TikRecordSentence>();
            foreach (string line in plain.Split((char)10))
            {
                TikRecordSentence? row = table.Feed(line.TrimEnd((char)13));
                if (row != null)
                    rows.Add(row);
            }
            // The header is the acceptance signal, as on the streaming path: text that never produced one is the
            // router's refusal.
            if (!table.HasHeader && !string.IsNullOrWhiteSpace(plain))
                throw new TikCommandTrapException(_host.CommandFor(descriptor),
                    new TikTrapSentenceResult(CliErrorParser.ExtractErrorLine(plain)));
            _host.Knowledge.LearnFact(_host.Knowledge.MenuFacts.MonitorWithoutAsValue, CliRouterKnowledge.FactMonitorWithoutAsValue, descriptor.CommandText);
            return rows;
        }

        /// <summary>
        /// The plain <c>print</c> of a menu, read by column (<see cref="CliTableParser"/>), or <c>null</c> when it
        /// printed no table header — the router's refusal, which the caller reports as the as-value read's own.
        /// </summary>
        /// <remarks>
        /// Sent inside <c>:put [ … ]</c>, which still prints the table (6.49.13) but is a script, so it does not page and
        /// the terminal transports do not add <c>without-paging</c> to it — a word such a print takes for an argument
        /// value too (<c>input does not match any value of peer</c>).
        /// <para>
        /// The table has no <c>.id</c> column, and <c>get</c> answers such a menu with the id alone (6.49.13), so the ids
        /// come from the menu's <c>find</c> in the same script, one line in front of the table: <c>find</c> and
        /// <c>print</c> walk the rows in one order. When the counts differ the rows are returned without ids rather
        /// than paired wrongly.
        /// </para>
        /// </remarks>
        private async Task<IList<TikRecordSentence>?> RunPlainTablePrintAsync(TikCommandDescriptor descriptor,
            CancellationToken cancellationToken)
        {
            string print = CliCommandBuilder.ApiPathToCli(descriptor.CommandText);
            string menu = print.Substring(0, print.LastIndexOf(' '));
            string plain = await _host.ExecuteAsync(
                ":put (\"" + PlainTableIdsMarker + "\" . [:tostr [" + menu + " find]]); :put [" + print + "]",
                cancellationToken).ConfigureAwait(false);
            var table = new CliTableParser();
            var rows = new List<TikRecordSentence>();
            string[]? ids = null;
            foreach (string raw in plain.Split((char)10))
            {
                string line = raw.TrimEnd((char)13);
                // Starts with the marker: the echo of the command carries it too, after ':put ("'.
                if (ids == null && !table.HasHeader && line.TrimStart().StartsWith(PlainTableIdsMarker, StringComparison.Ordinal))
                {
                    ids = line.TrimStart().Substring(PlainTableIdsMarker.Length).Trim()
                        .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                    continue;
                }
                TikRecordSentence? row = table.Feed(line);
                if (row != null)
                    rows.Add(row);
            }
            if (!table.HasHeader)
                return null;
            if (ids == null || ids.Length != rows.Count)
                return rows;
            return rows.Select((row, i) =>
            {
                var words = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [TikSpecialProperties.Id] = ids[i] };
                foreach (var word in row.Words)
                    words[word.Key] = word.Value;
                return new TikRecordSentence(words);
            }).ToList();
        }

        /// <summary>Put in front of the ids a plain-table read asks <c>find</c> for, so the line is told from the table's.</summary>
        private const string PlainTableIdsMarker = "#t4n-ids=";

        /// <summary>
        /// Parses <c>as-value</c> output, treating text that yields no record as the router's refusal
        /// rather than as an empty result.
        /// </summary>
        /// <remarks>
        /// The positional rule in its general form, and it needs no phrase list: as-value output is
        /// <c>key=value;…</c> or nothing at all, so text that parses to no record is not as-value output —
        /// it is the router saying why there is none. An EMPTY table is empty output, and stays a legitimate
        /// empty result.
        /// <para>This covers ordinary reads as well as monitors, which is not where it started. A print that
        /// the router refuses answers with its complaint and no records: asking <c>/ip/dns</c> for
        /// <c>detail</c> — a modifier a singleton menu does not have — answers
        /// <c>bad parameter detail (line 1 column 27)</c>, and with that swallowed the caller was told the
        /// singleton simply had no rows. Every menu the transport audit compared this way looked like a
        /// transport that could not read singletons at all.</para>
        /// <para>Measured on 7.23.2:
        /// <c>:put [/interface monitor-traffic interface=kjdshfkjdhf once as-value]</c> answers
        /// <c>input does not match any value of interface</c> — a phrase no classifier in
        /// <see cref="CliErrorParser"/> matches, so the call returned "no rows" and the O/R layer reported
        /// success for a command the router had rejected. Unlike
        /// <see cref="CliErrorParser.IsSilentOnSuccessVerb"/> this needs no phrase list and no verb whitelist:
        /// output-with-no-record is the signal.</para>
        /// <para>
        /// Empty output is deliberately NOT an error — <c>/tool torch … as-value</c> legitimately prints
        /// nothing at all (see <see cref="CliMonitorVerbs"/>), and "nothing happened" is not a refusal.
        /// </para>
        /// </remarks>
        internal IList<TikRecordSentence> ParseRecords(string output, TikCommandDescriptor descriptor)
        {
            IList<TikRecordSentence> rows = CliOutputParser.ParseAsValue(output);
            if (rows.Count == 0 && !string.IsNullOrWhiteSpace(output))
                throw new TikCommandTrapException(_host.CommandFor(descriptor),
                    new TikTrapSentenceResult(CliErrorParser.ExtractErrorLine(output)));
            return rows;
        }

        /// <summary>
        /// Asks the router, once per connection and separator, whether it serializes DSV — with a command of ~50
        /// characters rather than by trying the first read.
        /// </summary>
        /// <remarks>
        /// Trying the read itself cost too much where it fails. RouterOS 6 answers <c>bad command name serialize</c>,
        /// but only after echoing the whole typed line, and it repaints that line after every character: over the
        /// RoMON relay the ~300-character DSV read left its echo behind and spoiled the as-value read that followed
        /// (RomonRelayTest, 6.49.13 target, every leg). RouterOS 7 answers <c>str</c>, and a separator it refuses
        /// ("invalid delimiter") reads as unsupported too, so the connection falls back to as-value instead of failing
        /// every read.
        /// </remarks>
        private async Task EnsureDsvSupportKnownAsync(CancellationToken cancellationToken)
        {
            // Every read passes here first, so this is where a connection meets its build's stored facts.
            await _host.Knowledge.EnsureSchemaStoreAsync(cancellationToken).ConfigureAwait(false);
            string? separator = _host.CliFieldSeparator;
            if (separator == null || (_host.Knowledge.Features.Dsv != null && _host.Knowledge.Features.DsvProbedSeparator == separator))
                return;
            if (_host.Knowledge.Store != null && _host.Knowledge.Store.TryGetFeature(CliRouterKnowledge.FeatureDsv + separator, out bool stored))
            {
                _host.Knowledge.Features.DsvProbedSeparator = separator;
                _host.Knowledge.Features.Dsv = stored;
                return;
            }

            string answer = await _host.ExecuteAsync(
                CliCommandBuilder.BuildDsvProbe(separator), cancellationToken).ConfigureAwait(false);
            _host.Knowledge.Features.DsvProbedSeparator = separator;
            _host.Knowledge.Features.Dsv = CliCommandBuilder.IsDsvProbeAccepted(answer);
            _host.Knowledge.StoreFeature(CliRouterKnowledge.FeatureDsv + separator, _host.Knowledge.Features.Dsv.Value);
            if (_host.Knowledge.Features.Dsv == false)
                TikWireTrace.Emit("cli.dsv", TikWireDir.Note,
                    "':serialize to=dsv' not available (" + answer.Trim() + ") — reading as-value on this connection; "
                        + "a value holding ';' (a comment) is not read intact on this router");
        }

        /// <summary>
        /// Runs the <c>get</c> verb and returns its answer as a single record.
        /// </summary>
        /// <remarks>
        /// The record carries one field, named after the requested <c>value-name</c> (or <c>ret</c> for a
        /// whole-row get). That name is what makes both scalar spellings work: <c>ExecuteScalar()</c> reads
        /// the row's one field, and <c>ExecuteScalar("name")</c> asks for it by name.
        /// <para>
        /// An empty answer is left as zero records rather than an empty-valued one. <c>get</c> is a read
        /// verb, so the layer above turns "no rows" into <see cref="TikNoSuchItemException"/> — which is what
        /// a get for a row that is not there means, and what the binary API reports for the same command.
        /// </para>
        /// </remarks>
        private async Task<IList<TikRecordSentence>> RunGetAsync(
            TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            string? id = TikGetResult.FindInput(descriptor.Parameters, TikSpecialProperties.Id);
            string? valueName = TikGetResult.FindInput(descriptor.Parameters, "value-name");

            string cliText = CliCommandBuilder.BuildGet(descriptor.CommandText, id, valueName);
            string output = await _host.ExecuteAsync(cliText, cancellationToken).ConfigureAwait(false);
            CliErrorParser.ThrowIfError(output, _host.CommandFor(descriptor));

            string value = output.Trim();
            if (value.Length == 0)
                return new List<TikRecordSentence>();

            return TikGetResult.One(string.IsNullOrEmpty(valueName) ? TikSpecialProperties.Ret : valueName!, value);
        }

        /// <summary>
        /// Runs one print query, in JSON form when <paramref name="wantJson"/> is set and this router has
        /// not already refused <c>:serialize</c>.
        /// <para>
        /// The fallback is deliberately driven by evidence rather than by phrase-matching the refusal (the
        /// wording differs by RouterOS version, so a phrase list silently stops matching): if the wrapped form
        /// is rejected while support is still unknown, the plain form is run, and only its SUCCESS
        /// downgrades this connection. When both fail, the plain form's error is what the caller sees —
        /// i.e. exactly today's behaviour — and nothing is concluded about <c>:serialize</c>. Once support
        /// is known either way, no retry happens again on this connection.
        /// </para>
        /// <para>
        /// <b>Either way the answer is counted.</b> A window states how many ids it held, and a whole-table
        /// read states how many records it produced (<see cref="CliCommandBuilder.BuildCountedRead"/>); the
        /// records parsed must match, or the read is refused with
        /// <see cref="TikConnectionResponseIncompleteException"/>. That turns "the answer ended at a prompt, so
        /// it is probably whole" into a statement by the router of what "whole" is.
        /// </para>
        /// </summary>
        /// <param name="descriptor">The read being run.</param>
        /// <param name="wantJson">Whether the caller asked for the JSON form.</param>
        /// <param name="buildExpression">
        /// The bare print expression (no <c>:put</c>) for a given parameter list and <c>from=</c> selector
        /// (<c>null</c> for the whole table) — wrapped here, differently for a window and for a counted
        /// whole-table read. The parameters are passed in rather than closed over because a window's print
        /// is built without the caller's filters: they go into the window's own <c>find</c> instead
        /// (<c>CliPagedReader.RunOneWindowAsync</c>).
        /// </param>
        /// <param name="cancellationToken">Cancels the read.</param>
        internal async Task<IList<TikRecordSentence>> RunPrintQueryAsync(
            TikCommandDescriptor descriptor, bool wantJson,
            Func<IList<ITikCommandParameter>, string?, string> buildExpression,
            CancellationToken cancellationToken)
        {
            await EnsureDsvSupportKnownAsync(cancellationToken).ConfigureAwait(false);

            if (_pagedReader.CanPage(descriptor, wantJson))
            {
                var paged = await _pagedReader.RunPagedPrintQueryAsync(descriptor, wantJson, buildExpression,
                    _host.CliReadPageSize, cancellationToken).ConfigureAwait(false);
                if (paged != null)
                    return paged;
            }

            _host.CountedReadDepth++;
            try
            {
                return await _pagedReader.RunOnePrintQueryAsync(descriptor, wantJson,
                        buildExpression(descriptor.Parameters, null), cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _host.CountedReadDepth--;
            }
        }

        // True for verbs that perform an action rather than read records (no result set over a terminal).
        private static bool IsActionVerb(string verb) => verb == "run";

        // Actions reached through a read method because the binary API returns an empty row for them.
        // Kept minimal and explicit (like CliCommandBuilder.CliPresenceFlagFields); extend as more surface.
        private static bool IsEmptyRowAction(string verb) => verb == "wol";

        // Reads whose arguments are the question, not a filter over a table (see RunPrintCoreAsync).
        private static bool IsArgumentRead(string verb) => verb == "inspect";

        /// <summary>
        /// A read whose arguments are the question (<see cref="IsArgumentRead"/>), as per-row DSV where the router takes
        /// it: inspect's completion rows carry values as-value cannot frame — <c>completion="</c>, which opened a quote
        /// that swallowed the rest of the answer (7.24.4), and free text with spaces. Elsewhere the as-value snapshot.
        /// </summary>
        private async Task<IList<TikRecordSentence>> RunArgumentReadAsync(TikCommandDescriptor descriptor,
            CancellationToken cancellationToken)
        {
            await EnsureDsvSupportKnownAsync(cancellationToken).ConfigureAwait(false);
            if (_host.CliFieldSeparator != null && _host.Knowledge.Features.Dsv == true && _host.Knowledge.Features.DsvProbedSeparator == _host.CliFieldSeparator)
            {
                // ":put [/console inspect request=… path=… as-value]" → the expression inside the brackets.
                string snapshot = CliCommandBuilder.BuildMonitorSnapshot(descriptor.CommandText, descriptor.Parameters,
                    string.Empty, includeFilters: true);
                string expression = snapshot.Substring(":put [".Length, snapshot.Length - ":put [".Length - 1);
                string output;
                _host.CountedReadDepth++;   // counted below, exactly — the MAC-layer loss heuristic stands down
                try
                {
                    output = await _host.ExecuteAsync(
                        CliCommandBuilder.BuildCountedDsvRead(expression, _host.CliFieldSeparator), cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _host.CountedReadDepth--;
                }
                CliErrorParser.ThrowIfError(output, _host.CommandFor(descriptor));
                string body = _pagedReader.SplitOffCountMarker(output, descriptor, out int expected);
                return _pagedReader.EnsureCounted(CliDsvParser.Parse(body, _host.CliFieldSeparator), expected, descriptor, body);
            }
            // As-value, once. Not the monitor path's plain-table retry: that is for RouterOS 6's monitors, and the only
            // argument read, inspect, does not exist there at all — the retry would repeat the refusal, at 6.x's
            // per-character echo cost.
            string asValue = await _host.ExecuteAsync(CliCommandBuilder.BuildMonitorSnapshot(descriptor.CommandText,
                descriptor.Parameters, string.Empty, includeFilters: true), cancellationToken).ConfigureAwait(false);
            CliErrorParser.ThrowIfError(asValue, _host.CommandFor(descriptor));
            return ParseRecords(asValue, descriptor);
        }

        // Misuse of a read method (ExecuteList/ExecuteScalar/…) on an action command — guide to ExecuteNonQuery.
        private NotSupportedException ActionVerbOnReadPath(string commandText)
            => new NotSupportedException(
                $"'{commandText}' is an action command and returns no result set over the {_host.TransportName} " +
                "transport. Invoke it with ExecuteNonQuery() instead of ExecuteList()/ExecuteScalar().");
    }
}
