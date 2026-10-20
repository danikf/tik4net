using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Connection;
using tik4net.Diagnostics;

namespace tik4net.Cli
{
    /// <summary>
    /// The counted and windowed print reads: a table read one window of rows at a time where the read may be
    /// windowed, otherwise as one counted command, each answer checked against the router's own count of it, as
    /// JSON, DSV or as-value. <see cref="CliConnectionBase"/>'s <c>RunPrintQueryAsync</c> picks between the two. One
    /// per connection; what it learns about the router lives in <see cref="CliRouterKnowledge"/>.
    /// </summary>
    internal sealed class CliPagedReader
    {
        private readonly ICliCommandHost _host;

        internal CliPagedReader(ICliCommandHost host)
        {
            _host = host;
        }

        // Whether this read is written as DSV: a separator is set, the router takes it, the read is not on the JSON
        // path, and the menu is not one read one id at a time (RouterOS 6 only).
        internal bool UsesDsv(TikCommandDescriptor descriptor, bool wantJson)
            => _host.CliFieldSeparator != null && _host.Knowledge.Features.Dsv == true && _host.Knowledge.Features.DsvProbedSeparator == _host.CliFieldSeparator
               && !(wantJson && _host.Knowledge.Features.SerializeJson != false)
               && !_host.Knowledge.MenuFacts.WindowPrintsNoId.Contains(descriptor.CommandText);

        // ParseRecords for a DSV answer: output that yields no record is the router's complaint, as there.
        internal IList<TikRecordSentence> ParseDsvRecords(string output, TikCommandDescriptor descriptor)
        {
            IList<TikRecordSentence> rows = CliDsvParser.Parse(output, _host.CliFieldSeparator!);
            if (rows.Count == 0 && !string.IsNullOrWhiteSpace(output))
                throw new TikCommandTrapException(_host.CommandFor(descriptor),
                    new TikTrapSentenceResult(CliErrorParser.ExtractErrorLine(output)));
            return rows;
        }

        /// <summary>
        /// Whether this read may be windowed. Paging is an optimisation for reading a whole table, and
        /// several shapes of read are not that.
        /// </summary>
        /// <remarks>
        /// <para>A read that names a row (<c>.id</c>) must not be windowed: the window is taken over the
        /// menu's id list and the caller's selector is applied inside it, so the row is simply absent from
        /// every window that does not contain it and the read fails with <c>no such item</c> — measured, 18
        /// tests on the MAC-Telnet leg.</para>
        /// <para>The JSON path is windowed too. Its <c>:serialize</c> discovery happens on the FIRST window
        /// rather than before the loop: a connection starts not knowing, and leaving the discovery read
        /// unpaged means the one read that most needs windowing — <c>/file print detail</c> — never gets it.
        /// Nothing has been returned at that point, so the loop simply restarts as as-value.</para>
        /// <para>A menu with no <c>find</c> verb cannot be windowed at all; that one is not predictable from
        /// the descriptor, so it is handled by falling back when the router says so.</para>
        /// <para>A <b>filtered</b> read is windowed: the clause goes into the window's <c>find</c>, which
        /// selects the same rows in the same order as the <c>where</c> it replaces (see
        /// <see cref="CliCommandBuilder.BuildPagedWindow"/>), so the windows cover matching rows only.</para>
        /// </remarks>
        internal bool CanPage(TikCommandDescriptor descriptor, bool wantJson)
        {
            if (_host.CliReadPageSize <= 0)
                return false;

            if (TikGetResult.FindInput(descriptor.Parameters, TikSpecialProperties.Id) != null)
                return false;
            // A FILTERED read is windowed like any other: its clause goes into the window's own 'find', so
            // the windows cover matching rows only (RunOneWindowAsync). What is refused is applying the
            // filter INSIDE an unfiltered window — correct, but it multiplies the work instead of shrinking
            // it (49 of 1672 mangle rows: 1190 ms that way, 72 ms with the filter in the 'find', router-side
            // at page size 20) and it breaks what '#w=' means, since a filter legitimately prints fewer rows
            // than the window held.
            return !_host.Knowledge.MenuFacts.PagingUnavailable.Contains(descriptor.CommandText);
        }

        /// <summary>
        /// Reads a table one window of rows at a time and concatenates them.
        /// </summary>
        /// <remarks>
        /// <para>Each window is a single command that picks a slice of the menu's id list, prints the rows it
        /// names and reports how many ids it held — so a table smaller than one page is answered by the first
        /// request and costs nothing extra, which is the common case. The loop ends when a window holds fewer
        /// ids than a full page.</para>
        /// <para><b>It is not a snapshot.</b> The windows are separate commands and each re-evaluates the id
        /// list, so a row inserted or removed part-way through shifts the ones after it and can be seen twice
        /// or missed. A single-command read cannot do that, which is why paging is off unless a transport or
        /// a caller asks for it.</para>
        /// </remarks>
        internal async Task<IList<TikRecordSentence>?> RunPagedPrintQueryAsync(
            TikCommandDescriptor descriptor, bool wantJson,
            Func<IList<ITikCommandParameter>, string?, string> buildExpression,
            int pageSize, CancellationToken cancellationToken)
        {
            var all = new List<TikRecordSentence>();
            int interruptions = 0;

            for (int offset = 0; ; offset += pageSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                PagedWindow window;
                _host.WindowedReadDepth++;
                try
                {
                    window = await RunOneWindowAsync(descriptor, wantJson, buildExpression, offset, pageSize,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (TikCommandException) when (offset == 0 && wantJson && _host.Knowledge.Features.SerializeJson == null)
                {
                    // The router refused ':serialize' and we did not yet know whether it can. Nothing has
                    // been handed back, so settle the question and take the same table again as as-value.
                    _host.Knowledge.Features.SerializeJson = false;
                    TikWireTrace.Emit("cli.json", TikWireDir.Note,
                        "':serialize to=json' refused inside a paged read — this router is pre-7.13; "
                            + "re-reading as-value for the rest of this connection");
                    offset = -pageSize;   // the loop's increment puts it back to 0
                    continue;
                }
                catch (TikNoSuchCommandException)
                {
                    // The menu has no 'find' (a singleton, or a command menu). Nothing to window over, so
                    // read it the way every read worked before paging — and remember, so the next read of
                    // this menu does not pay for the discovery again.
                    if (offset > 0)
                        throw;   // mid-table: this is a real failure, not a menu that cannot be paged
                    _host.Knowledge.LearnFact(_host.Knowledge.MenuFacts.PagingUnavailable, CliRouterKnowledge.FactPagingUnavailable, descriptor.CommandText);
                    TikWireTrace.Emit("cli.page", TikWireDir.Note,
                        "'" + descriptor.CommandText + "' has no 'find' verb — reading it in a single command");
                    return null;
                }
                finally
                {
                    _host.WindowedReadDepth--;
                }

                if (window.Interrupted)
                {
                    // A row this window named vanished before it was printed, and RouterOS aborted the line. Not
                    // a menu without 'find' (at offset 0 that mistake would switch paging off for the rest of
                    // the connection), and not a failure of the read: the same window taken again re-runs
                    // 'find' and names the rows that exist now.
                    if (++interruptions > MaxInterruptedWindowRetries)
                        throw new TikConnectionResponseIncompleteException(
                            _host.TransportName + ": RouterOS interrupted the window at offset " + offset + " of '"
                                + descriptor.CommandText + "' " + interruptions + " times in a row — rows are "
                                + "vanishing between the window's 'find' and its 'print' faster than it can be "
                                + "taken. A table that turns over this quickly reads reliably as one command: set "
                                + "_host.CliReadPageSize to 0 for it, or use a transport that is not on the MAC layer.",
                            0, window.Response);
                    TikWireTrace.Emit("cli.page", TikWireDir.Note,
                        "window at offset " + offset + " of '" + descriptor.CommandText
                            + "' interrupted (a row vanished under it) — taking it again");
                    offset -= pageSize;   // the loop's increment brings it back to the same window
                    continue;
                }
                interruptions = 0;

                if (window.WindowSize > 0 && !_host.Knowledge.MenuFacts.WindowPrintsNoId.Contains(descriptor.CommandText)
                    && window.Records.All(r => !r.Words.ContainsKey(TikSpecialProperties.Id)))
                {
                    // The window held rows and its print named none of them — the menu's 'print from=' leaves
                    // '.id' out (RouterOS 6.49.13: /system/package, /ip/ipsec/policy), and without it the parser
                    // cannot even tell the rows apart. Take the same window again one id at a time.
                    _host.Knowledge.LearnFact(_host.Knowledge.MenuFacts.WindowPrintsNoId, CliRouterKnowledge.FactWindowPrintsNoId, descriptor.CommandText);
                    TikWireTrace.Emit("cli.page", TikWireDir.Note,
                        "'" + descriptor.CommandText + "' prints no .id for a window — taking it one id at a time");
                    offset -= pageSize;   // the loop's increment brings it back to the same window
                    continue;
                }

                if (window.WindowSize < 0)
                {
                    // No '#w=' came back, so there is no way to know when to stop. Same treatment.
                    if (offset > 0)
                        throw new TikSentenceException(
                            "A paged read must be able to tell how many rows the window held, and the window "
                            + "at offset " + offset + " of '" + descriptor.CommandText + "' did not say.", null);
                    _host.Knowledge.MenuFacts.PagingUnavailable.Add(descriptor.CommandText);
                    TikWireTrace.Emit("cli.page", TikWireDir.Note,
                        "'" + descriptor.CommandText + "' did not report a window size — reading it in a single command");
                    return null;
                }

                // A window's print carries no filter (the filter is in its 'find'), so every id it held names a
                // row and the router has just said how many records this answer must carry. Anything else is an answer
                // that lost rows on the way — or a parser that split or merged them — and either way it is not
                // the table. This is exact, which the MAC layer's datagram heuristic is not, and it is the only
                // check a window gets: a vanished id is not a way to get here, since 'print from=' answers
                // 'no such item' for it rather than skipping it (measured on 7.24).
                if (window.Records.Count != window.WindowSize)
                    throw new TikConnectionResponseIncompleteException(
                        _host.TransportName + ": the window at offset " + offset + " of '" + descriptor.CommandText
                            + "' held " + window.WindowSize + " row(s) by the router's own count, and "
                            + window.Records.Count + " record(s) were read from its answer. The answer is not the "
                            + "table, so it is refused rather than returned. Retry the read.",
                        0, window.Response);

                foreach (var record in window.Records)
                    all.Add(record);

                // The window's OWN size ends the loop — the two are equal by now, but the size is what the
                // router stated and the record count is only what we made of it.
                if (window.WindowSize < pageSize)
                    return all;
            }
        }

        /// <summary>
        /// How many times one window is taken again after RouterOS interrupted it. Measured under heavy
        /// conntrack churn (~5000 rows turning over every 30 s), 17 of 26 reads of about 50 windows each hit
        /// one without retry — 1.4–2.4 % of windows — so three retries leave well under one in a million per
        /// window; a window that still fails is a table no windowed read can keep up with.
        /// </summary>
        private const int MaxInterruptedWindowRetries = 3;

        private readonly struct PagedWindow
        {
            internal PagedWindow(IList<TikRecordSentence> records, int windowSize, string? response,
                                 bool interrupted = false)
            {
                Records = records;
                WindowSize = windowSize;
                Response = response;
                Interrupted = interrupted;
            }

            internal IList<TikRecordSentence> Records { get; }
            internal int WindowSize { get; }
            internal string? Response { get; }

            /// <summary>RouterOS aborted the window's line: it answered <c>interrupted</c> and nothing else.</summary>
            internal bool Interrupted { get; }
        }

        /// <summary>
        /// True when the router refused a window because the menu's <c>print</c> takes no <c>from=</c> —
        /// <c>bad parameter from (line 1 column N)</c>, which is what <c>/log print</c> answers (7.24). Such a
        /// menu cannot be windowed and is read in one command, like one without <c>find</c>.
        /// </summary>
        private static bool IsPrintWithoutFrom(string? output)
            => (output ?? string.Empty).Trim().StartsWith("bad parameter from (line ", StringComparison.Ordinal);

        /// <summary>
        /// True when RouterOS aborted the command line — what it answers, with no rows, no marker and no error
        /// text, when a window's <c>print from=</c> names an id that vanished after its <c>find</c> (measured on
        /// 7.24 under conntrack churn; Docs/findings-cli.md §1). The word alone on a line, so a field value
        /// cannot be mistaken for it.
        /// </summary>
        private static bool IsInterruptedAnswer(string? output)
            => (output ?? string.Empty).Split((char)10)
                .Any(line => string.Equals(line.Trim(), "interrupted", StringComparison.Ordinal));

        /// <summary>
        /// Takes one window: the caller's filters become the window's <c>find</c> clause, and the print
        /// inside it is built WITHOUT them.
        /// </summary>
        /// <remarks>
        /// The filter belongs in exactly one of the two places. In the <c>find</c> it shrinks the window to
        /// matching rows, which is the point; left in the print as well it would be redundant on every row
        /// the window already holds, and a row that stopped matching between the <c>find</c> and the
        /// <c>print</c> would make the answer shorter than the window's own <c>#w=</c> count — refused as an
        /// incomplete response, with no retry path. So it is dropped from the print, and a windowed read
        /// stays what it already was: not a snapshot, the rows as the router had them when each window ran.
        /// </remarks>
        private async Task<PagedWindow> RunOneWindowAsync(
            TikCommandDescriptor descriptor, bool wantJson,
            Func<IList<ITikCommandParameter>, string?, string> buildExpression,
            int offset, int pageSize, CancellationToken cancellationToken)
        {
            bool idPerRow = _host.Knowledge.MenuFacts.WindowPrintsNoId.Contains(descriptor.CommandText);
            bool asJson = wantJson && _host.Knowledge.Features.SerializeJson != false && !idPerRow;
            bool asDsv = UsesDsv(descriptor, wantJson);
            string findClause = CliCommandBuilder.BuildWhereClause(descriptor.Parameters);
            string cliText = idPerRow
                ? CliCommandBuilder.BuildPagedWindowIdPerRow(descriptor.CommandText,
                    buildExpression(WithoutFilters(descriptor.Parameters), CliCommandBuilder.RowVariable),
                    offset, pageSize, findClause)
                : CliCommandBuilder.BuildPagedWindow(descriptor.CommandText,
                    asDsv
                        ? CliCommandBuilder.WrapDsv(
                            buildExpression(WithoutFilters(descriptor.Parameters), CliCommandBuilder.WindowVariable), _host.CliFieldSeparator!)
                        : CliCommandBuilder.WrapPut(
                            buildExpression(WithoutFilters(descriptor.Parameters), CliCommandBuilder.WindowVariable), asJson),
                    offset, pageSize, findClause);

            string output = await _host.ExecuteAsync(cliText, cancellationToken).ConfigureAwait(false);
            CliErrorParser.ThrowIfError(output, _host.CommandFor(descriptor));

            string body = SplitOffWindowMarker(output, out int windowSize);
            // A bad filter is the caller's error, not a menu that cannot be windowed — unless what the router
            // refused is the window's own 'from=', which a print such as '/log print' does not take.
            if (windowSize < 0 && !IsPrintWithoutFrom(output))
                _host.ThrowIfSyntaxError(output, descriptor);
            if (windowSize < 0)   // caller decides: take it again if interrupted, otherwise fall back
                return new PagedWindow(new List<TikRecordSentence>(), -1, output, IsInterruptedAnswer(output));

            IList<TikRecordSentence> parsed = asJson
                ? CliJsonParser.ParseJson(body)
                : asDsv
                    ? ParseDsvRecords(body, descriptor)
                    : _host.ParseRecords(body, descriptor);
            if (idPerRow)
                parsed = parsed.Select(WithoutNextId).ToList();
            // Only a window that held ids ran its print — the print sits inside ':if ([:len $w] > 0) do={ … }' —
            // so an empty one says nothing about ':serialize'. Concluding from it let a free-text menu with no
            // rows mark RouterOS 6.49.13 as supporting it, and the next such menu with rows failed with
            // 'bad command name serialize' and no fallback.
            if (asJson && windowSize > 0)
                _host.Knowledge.Features.SerializeJson = true;
            return new PagedWindow(parsed, windowSize, body);
        }

        // The '.nextid' a per-row print carries where 6.49.13 left '.id' out: the ordered-list cursor, which the API
        // does not print for these rows and which is not a field of the row.
        private static TikRecordSentence WithoutNextId(TikRecordSentence record)
        {
            if (!record.Words.ContainsKey(".nextid"))
                return record;
            return new TikRecordSentence(record.Words.Where(w => w.Key != ".nextid")
                .ToDictionary(w => w.Key, w => w.Value));
        }

        /// <summary>
        /// The parameters a window's print is built from: everything except the Filter-format ones, which
        /// the window's <c>find</c> has taken over. The rest are print modifiers and inputs
        /// (<c>detail</c>, <c>numbers</c>, <c>once</c>) and must stay.
        /// </summary>
        private static IList<ITikCommandParameter> WithoutFilters(IList<ITikCommandParameter> parameters)
        {
            var kept = new List<ITikCommandParameter>(parameters.Count);
            foreach (var p in parameters)
            {
                if (p.ParameterFormat != TikCommandParameterFormat.Filter)
                    kept.Add(p);
            }
            return kept;
        }

        /// <summary>
        /// Removes the trailing <c>#w=</c> line a paged window ends with and reports the number it carried
        /// (<c>-1</c> when the line is absent).
        /// </summary>
        // CR, LF, the field separator and a space: whatever the marker line was preceded by.
        private static readonly char[] WindowMarkerTrim = { (char)13, (char)10, ';', ' ' };

        private static string SplitOffWindowMarker(string output, out int windowSize)
        {
            windowSize = -1;
            if (string.IsNullOrEmpty(output))
                return output;

            int at = output.LastIndexOf(CliCommandBuilder.WindowMarker, StringComparison.Ordinal);
            if (at < 0)
                return output;

            string tail = output.Substring(at + CliCommandBuilder.WindowMarker.Length).Trim();
            if (!int.TryParse(tail, NumberStyles.None, CultureInfo.InvariantCulture, out int size))
                return output;

            windowSize = size;
            return output.Substring(0, at).TrimEnd(WindowMarkerTrim);
        }

        internal async Task<IList<TikRecordSentence>> RunOnePrintQueryAsync(
            TikCommandDescriptor descriptor, bool wantJson, string expression, CancellationToken cancellationToken)
        {
            if (wantJson && _host.Knowledge.Features.SerializeJson != false)
            {
                try
                {
                    string jsonOutput = await _host.ExecuteAsync(
                        CliCommandBuilder.BuildCountedRead(expression, asJson: true), cancellationToken).ConfigureAwait(false);
                    CliErrorParser.ThrowIfError(jsonOutput, _host.CommandFor(descriptor));
                    string jsonBody = SplitOffCountMarker(jsonOutput, descriptor, out int expectedJson);
                    IList<TikRecordSentence> records = CliJsonParser.ParseJson(jsonBody);
                    _host.Knowledge.Features.SerializeJson = true;
                    return EnsureCounted(records, expectedJson, descriptor, jsonBody);
                }
                catch (TikCommandException ex) when (_host.Knowledge.Features.SerializeJson == null)
                {
                    // Router refused the wrapped command and we do not yet know whether it can serialise at
                    // all. Fall through to the plain form; if THAT works, the router is pre-7.13.
                    TikWireTrace.Emit("cli.json", TikWireDir.Note,
                        "':serialize to=json' refused (" + ex.GetType().Name
                            + ") — retrying as-value to establish whether this router supports it");
                }
            }

            if (UsesDsv(descriptor, wantJson))
            {
                string dsvOutput = await _host.ExecuteAsync(
                    CliCommandBuilder.BuildCountedDsvRead(expression, _host.CliFieldSeparator!), cancellationToken).ConfigureAwait(false);
                CliErrorParser.ThrowIfError(dsvOutput, _host.CommandFor(descriptor));
                string dsvBody = SplitOffCountMarker(dsvOutput, descriptor, out int expectedDsv);
                return EnsureCounted(ParseDsvRecords(dsvBody, descriptor), expectedDsv, descriptor, dsvBody);
            }

            string output = await _host.ExecuteAsync(
                CliCommandBuilder.BuildCountedRead(expression, asJson: false), cancellationToken).ConfigureAwait(false);
            CliErrorParser.ThrowIfError(output, _host.CommandFor(descriptor));
            string body = SplitOffCountMarker(output, descriptor, out int expected);

            if (wantJson && _host.Knowledge.Features.SerializeJson == null)
            {
                // The plain form worked where the wrapped one did not: RouterOS < 7.13. Say so — a
                // connection reading free-text fields through as-value can silently shred them,
                // and a degradation nobody can observe returns wrong data instead of an error.
                _host.Knowledge.Features.SerializeJson = false;
                TikWireTrace.Emit("cli.json", TikWireDir.Note,
                    "router does not support ':serialize' (pre-7.13) — falling back to as-value for the "
                        + "rest of this connection; fields holding free-form text may parse incorrectly");
            }

            return EnsureCounted(_host.ParseRecords(body, descriptor), expected, descriptor, body);
        }

        /// <summary>
        /// Removes the trailing <see cref="CliCommandBuilder.CountMarker"/> line of a counted read and returns
        /// what precedes it, with the record count the marker states.
        /// </summary>
        /// <remarks>
        /// A counted read that does not end with a well-formed marker is refused, not passed through. The
        /// answer has already ended at a prompt and carried no error, so the router ran the whole line; the
        /// marker is its last output, and an answer missing it has lost something — nothing legitimate reaches
        /// here without one. The marker must also start its own line, so a field value that happens to contain
        /// the same text cannot be taken for it.
        /// </remarks>
        internal string SplitOffCountMarker(string output, TikCommandDescriptor descriptor, out int expected)
        {
            expected = -1;
            string text = output ?? string.Empty;
            int at = text.LastIndexOf(CliCommandBuilder.CountMarker, StringComparison.Ordinal);
            if (at >= 0 && (at == 0 || text[at - 1] == (char)10 || text[at - 1] == (char)13))
                expected = CliCommandBuilder.ExpectedRecordCount(text.Substring(at + CliCommandBuilder.CountMarker.Length));

            if (expected < 0)
                _host.ThrowIfSyntaxError(text, descriptor);
            if (expected < 0)
                throw new TikConnectionResponseIncompleteException(
                    _host.TransportName + ": the read of '" + descriptor.CommandText + "' ended without the router's "
                        + "count of the records it holds, which it always writes last"
                        + (IsInterruptedAnswer(text) ? " — RouterOS interrupted the command" : "")
                        + ". The answer is incomplete, so it is refused rather than returned. Retry the read.",
                    0, output);

            return text.Substring(0, at).TrimEnd(WindowMarkerTrim);
        }

        /// <summary>
        /// Returns <paramref name="records"/> when there are as many as the router said, and refuses them
        /// otherwise — a short answer lost rows on the way, a long one had a row split by the parser, and
        /// neither is the table. The refusal names the body's non-empty lines and its length: a trace is
        /// rarely running when it happens, and those two tell bytes lost on the way (too few lines for the
        /// rows) from rows the parser joined or dropped (enough lines).
        /// </summary>
        internal IList<TikRecordSentence> EnsureCounted(IList<TikRecordSentence> records, int expected,
            TikCommandDescriptor descriptor, string response)
        {
            if (records.Count == expected)
                return records;

            string body = response ?? string.Empty;
            int lines = 0;
            foreach (string line in body.Split('\n'))
                if (line.Trim().Length > 0)
                    lines++;
            throw new TikConnectionResponseIncompleteException(
                _host.TransportName + ": the router counted " + expected + " record(s) in its answer to '"
                    + descriptor.CommandText + "', and " + records.Count + " were read from it. The body held "
                    + lines + " non-empty line(s), " + body.Length + " character(s). The answer is not the table, "
                    + "so it is refused rather than returned. Retry the read.",
                0, response);
        }
    }
}
