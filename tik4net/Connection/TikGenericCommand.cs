using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.Connection
{
    /// <summary>
    /// Transport-neutral RouterOS command. Holds path + parameters and delegates execution to the
    /// owning <see cref="TikCommandConnectionBase"/> CRUD hooks (RunPrint/RunAdd/RunNonQuery);
    /// it does not itself build any transport-specific (CLI text / native M2) payload.
    /// </summary>
    internal class TikGenericCommand : TikCommandBase, ITikRawCommand, ITikCommandAsync
    {
        private TikMonitorHandle? _monitorHandle; // only set once ExecuteWithCallback has been called; see Cancel/CancelAndJoin's null-conditional use

        // ── Raw pass-through (ITikRawCommand) ──────────────────────────────────
        // Set by the CreateRawCommand factory. When IsRaw, CommandText is sent verbatim through the
        // transport (no CliCommandBuilder, no multiline/param normalization); see the raw branches below.
        bool ITikRawCommand.IsRaw { get => _isRaw; set => _isRaw = value; }
        bool ITikRawCommand.WrapAsValue { get => _wrapAsValue; set => _wrapAsValue = value; }
        private bool _isRaw;
        private bool _wrapAsValue;

        private TikCommandDescriptor BuildRawDescriptor()
            => new TikCommandDescriptor(CommandText, new List<ITikCommandParameter>(), isRaw: true, wrapAsValue: _wrapAsValue);

        /// <summary>The connection, as the transport base the setter accepted.</summary>
        private TikCommandConnectionBase Transport => (TikCommandConnectionBase)Connection;

        internal TikGenericCommand(TikCommandConnectionBase connection, string? commandText,
            TikCommandParameterFormat defaultParameterFormat, params ITikCommandParameter[] parameters)
            : base(connection, commandText, defaultParameterFormat, parameters)
        {
        }

        private protected override ITikConnection AcceptConnection(ITikConnection value)
        {
            Guard.ArgumentNotNull(value, "value");
            Guard.ArgumentOfType<TikCommandConnectionBase>(value, "value");
            return (TikCommandConnectionBase)value;
        }

        private protected override ITikCommandParameter CreateParameter(string name, string? value,
            TikCommandParameterFormat format)
            => new TikCommandParameter(name, value, format);

        // ── Execute methods ───────────────────────────────────────────────────

        public override void ExecuteNonQuery()
        {
            EnsureConnectionSet();
            EnsureCommandTextSet();
            BeginRun();
            try
            {
                if (_isRaw)
                {
                    // Raw send: the answer text is discarded, a router error still throws (RunRawText).
                    Transport.InvokeRunRawText(BuildRawDescriptor());
                    return;
                }
                var (cmd, p) = NormalizeMultilineCommand(CommandText, Parameters);
                Transport.InvokeRunNonQuery(BuildCommand(cmd, p));
            }
            finally
            {
                EndRun();
            }
        }

        public override string ExecuteScalar()
        {
            // throwIfMissing:true - ExecuteScalarInternal either returns a real value or throws, never null.
            return ExecuteScalarInternal(null, throwIfMissing: true, defaultValue: null)!;
        }

        public override string ExecuteScalar(string target)
        {
            // throwIfMissing:true - ExecuteScalarInternal either returns a real value or throws, never null.
            return ExecuteScalarInternal(target, throwIfMissing: true, defaultValue: null)!;
        }

        public override string? ExecuteScalarOrDefault()
        {
            // Returns null (or the caller's defaultValue, which may be null) when nothing matched —
            // the interface says so and now types it that way.
            return ExecuteScalarInternal(null, throwIfMissing: false, defaultValue: null);
        }

        public override string? ExecuteScalarOrDefault(string? defaultValue)
        {
            return ExecuteScalarInternal(null, throwIfMissing: false, defaultValue: defaultValue);
        }

        public override string? ExecuteScalarOrDefault(string? defaultValue, string target)
        {
            return ExecuteScalarInternal(target, throwIfMissing: false, defaultValue: defaultValue);
        }

        private string? ExecuteScalarInternal(string? target, bool throwIfMissing, string? defaultValue)
        {
            EnsureConnectionSet();
            EnsureCommandTextSet();
            BeginRun();
            try
            {
                if (_isRaw)
                    return ScalarFromRawText(Transport.InvokeRunRawText(BuildRawDescriptor()), throwIfMissing, defaultValue);

                var (normalCmd, normalParams) = NormalizeMultilineCommand(CommandText, Parameters);
                string verb = TikPath.Verb(normalCmd);

                if (verb == "add")
                    // The new .id, or TikAddIdNotReadException from the connection: never a default, which would hide
                    // a row that is on the router.
                    return Transport.InvokeRunAdd(BuildCommand(normalCmd, normalParams));

                var cmd = BuildCommand(normalCmd, ResolveParamsForRead(normalParams));
                IList<TikRecordSentence> rows;
                try
                {
                    rows = Transport.InvokeRunPrint(cmd);
                }
                catch (TikNoSuchItemException)
                {
                    // Filtering by a non-matching/invalid .id yields "expected item id" on RouterOS.
                    // For the *OrDefault variants this means "not found" → return the default.
                    if (throwIfMissing)
                        throw;
                    return defaultValue;
                }
                return ScalarFromRows(rows, verb, target, throwIfMissing, defaultValue);
            }
            finally
            {
                EndRun();
            }
        }

        // ── Scalar result interpretation ──────────────────────────────────────
        // Shared by the synchronous and the Task-based scalar paths, which differ only in how they obtain the
        // response. Keeping the interpretation in one place matters more than the few lines it saves: these
        // three helpers encode which empty answer is "not found" and which is "succeeded with nothing to
        // return", and the two must not drift apart between the sync and async surfaces.

        /// <summary>
        /// Raw scalar: the cleaned response text as-is (e.g. /export, /system routerboard print). No verb
        /// detection or row extraction — raw has no record model.
        /// </summary>
        private string? ScalarFromRawText(string rawText, bool throwIfMissing, string? defaultValue)
            // Empty output is not "no such item": a raw command has no record model, and the silent answer is what a
            // successful write prints over the CLI (set/unset/remove/enable/comment all echo nothing).
            => !string.IsNullOrEmpty(rawText) ? rawText : TikCommandResult.EmptyRawScalar(this, !throwIfMissing, defaultValue);

        /// <summary>
        /// Scalar of a read: pick <paramref name="target"/> (or the first non-<c>.id</c> field) out of the single
        /// returned row. Reads go through <c>print</c> rather than <c>get value-name=…</c> on purpose: RouterOS
        /// rejects <c>get .id=*N</c> ("syntax error") and <c>value-name=.id</c> ("input does not match any value
        /// of value-name"), while <c>:put [/path print as-value where .id=*N]</c> works for every field.
        /// </summary>
        private string? ScalarFromRows(IList<TikRecordSentence> rows, string verb, string? target, bool throwIfMissing, string? defaultValue)
        {
            if (rows.Count == 0)
                // A read that matched nothing really is 'no such item'; any other verb printing nothing has simply
                // succeeded without a return value (the binary API's bare !done, told apart the same way).
                return TikCommandResult.EmptyScalar(this, verb, !throwIfMissing, defaultValue, "returned no output");
            if (rows.Count > 1)
                throw new TikCommandUnexpectedResponseException("Single value expected but multiple rows returned.", this, rows.Cast<ITikSentence>());

            var single = rows[0];
            string fieldToRead = target
                ?? single.Words.Keys.FirstOrDefault(k => k != TikSpecialProperties.Id && k != TikSpecialProperties.Tag)
                ?? TikSpecialProperties.Id;
            return TikCommandResult.FieldOfRow(single, fieldToRead, !throwIfMissing, defaultValue);
        }

        // ExecuteSingleRow's throwIfMissing:true never lets TikCommandResult.SingleRow return null.
        public override ITikReSentence ExecuteSingleRow() => ExecuteSingleRowInternal(throwIfMissing: true)!;

        public override ITikReSentence? ExecuteSingleRowOrDefault() => ExecuteSingleRowInternal(throwIfMissing: false);

        private ITikReSentence? ExecuteSingleRowInternal(bool throwIfMissing)
        {
            EnsureConnectionSet();
            EnsureCommandTextSet();
            BeginRun();
            try
            {
                return TikCommandResult.SingleRow(this, Transport.InvokeRunPrint(BuildReadDescriptor()), throwIfMissing);
            }
            finally
            {
                EndRun();
            }
        }

        public override IEnumerable<ITikReSentence> ExecuteList()
        {
            return ExecuteListInternal(null);
        }

        public override IEnumerable<ITikReSentence> ExecuteList(params string[] proplistFields)
        {
            return ExecuteListInternal(proplistFields);
        }

        private IEnumerable<ITikReSentence> ExecuteListInternal(string[]? proplist)
        {
            EnsureConnectionSet();
            EnsureCommandTextSet();
            BeginRun();
            try
            {
                return Transport.InvokeRunPrint(BuildReadDescriptor(proplist));
            }
            finally
            {
                EndRun();
            }
        }

        /// <summary>
        /// The read descriptor, carrying <paramref name="proplist"/> as a <c>.proplist</c> parameter when the
        /// caller named fields — the same word the binary API sends, which each transport honours its own way
        /// (REST as <c>?.proplist=</c>, the CLI and WinBox native by trimming the rows). A command with its own
        /// <c>.proplist</c> is refused (<see cref="TikCommandBase.FieldListParameter"/>).
        /// </summary>
        private TikCommandDescriptor BuildReadDescriptor(string[]? proplist)
        {
            var descriptor = BuildReadDescriptor();
            if (descriptor.IsRaw)
                return descriptor;
            var fieldList = FieldListParameter(proplist);
            if (fieldList == null)
                return descriptor;
            var parameters = new List<ITikCommandParameter>(descriptor.Parameters) { fieldList };
            return new TikCommandDescriptor(descriptor.CommandText, parameters);
        }

        /// <summary>
        /// The descriptor the read paths (ExecuteList/ExecuteSingleRow and their async siblings) hand to the print
        /// hook: verbatim for a raw command, otherwise the multiline command + params normalized and with Default
        /// parameters resolved to Filter.
        /// </summary>
        private TikCommandDescriptor BuildReadDescriptor()
        {
            if (_isRaw)
                return BuildRawDescriptor();
            var (cmd, baseParams) = NormalizeMultilineCommand(CommandText, Parameters);
            return BuildCommand(cmd, ResolveParamsForRead(baseParams));
        }

        // ── ITikCommandAsync ──────────────────────────────────────────────────
        //
        // Deliberately mirrors the synchronous methods above instead of either side wrapping the other: the sync
        // path must keep working on transports whose async hooks are not implemented yet (they throw), and running
        // the async path over a blocking hook would be the Task.Run façade the design rules out. What the two
        // paths must NOT do is interpret a response differently, so everything after the Run*/Run*Async call is
        // the shared Scalar* helpers and TikCommandResult.
        //
        // Explicit implementation: consumers reach these through the Execute*Async extension methods on
        // ITikCommand, which apply the fail-closed AsyncCommands check first.

        Task ITikCommandAsync.ExecuteNonQueryAsync(CancellationToken cancellationToken)
            => ExecuteNonQueryInternalAsync(cancellationToken);

        async Task<string> ITikCommandAsync.ExecuteScalarAsync(CancellationToken cancellationToken)
            // never null: the helper throws when nothing matched (throwIfMissing: true)
            => (await ExecuteScalarInternalAsync(null, throwIfMissing: true, defaultValue: null, cancellationToken: cancellationToken).ConfigureAwait(false))!;

        async Task<string> ITikCommandAsync.ExecuteScalarAsync(string target, CancellationToken cancellationToken)
            => (await ExecuteScalarInternalAsync(target, throwIfMissing: true, defaultValue: null, cancellationToken: cancellationToken).ConfigureAwait(false))!;

        Task<string?> ITikCommandAsync.ExecuteScalarOrDefaultAsync(string? defaultValue, string? target, CancellationToken cancellationToken)
            => ExecuteScalarInternalAsync(target, throwIfMissing: false, defaultValue: defaultValue, cancellationToken: cancellationToken);

        async Task<ITikReSentence> ITikCommandAsync.ExecuteSingleRowAsync(CancellationToken cancellationToken)
            // never null: the helper throws when no row came back (throwIfMissing: true)
            => (await ExecuteSingleRowInternalAsync(throwIfMissing: true, cancellationToken: cancellationToken).ConfigureAwait(false))!;

        Task<ITikReSentence?> ITikCommandAsync.ExecuteSingleRowOrDefaultAsync(CancellationToken cancellationToken)
            => ExecuteSingleRowInternalAsync(throwIfMissing: false, cancellationToken: cancellationToken);

        Task<IList<ITikReSentence>> ITikCommandAsync.ExecuteListAsync(CancellationToken cancellationToken)
            => ExecuteListInternalAsync(cancellationToken);

        Task<IList<ITikReSentence>> ITikCommandAsync.ExecuteListAsync(string[] proplistFields, CancellationToken cancellationToken)
            => ExecuteListInternalAsync(cancellationToken, proplistFields);

        private async Task ExecuteNonQueryInternalAsync(CancellationToken cancellationToken)
        {
            EnsureConnectionSet();
            EnsureCommandTextSet();
            // Level 0 of the cancellation contract, applied here so it holds on every transport rather than
            // depending on each one remembering: a token that is already cancelled sends nothing at all.
            cancellationToken.ThrowIfCancellationRequested();
            BeginRun();
            try
            {
                if (_isRaw)
                {
                    // Raw send: the answer text is discarded, a router error still throws (RunRawText).
                    await Transport.InvokeRunRawTextAsync(BuildRawDescriptor(), cancellationToken).ConfigureAwait(false);
                    return;
                }
                var (cmd, p) = NormalizeMultilineCommand(CommandText, Parameters);
                await Transport.InvokeRunNonQueryAsync(BuildCommand(cmd, p), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                EndRun();
            }
        }

        private async Task<string?> ExecuteScalarInternalAsync(string? target, bool throwIfMissing, string? defaultValue,
            CancellationToken cancellationToken)
        {
            EnsureConnectionSet();
            EnsureCommandTextSet();
            cancellationToken.ThrowIfCancellationRequested();   // Level 0 — see ExecuteNonQueryInternalAsync
            BeginRun();
            try
            {
                if (_isRaw)
                {
                    string rawText = await Transport.InvokeRunRawTextAsync(BuildRawDescriptor(), cancellationToken).ConfigureAwait(false);
                    return ScalarFromRawText(rawText, throwIfMissing, defaultValue);
                }

                var (normalCmd, normalParams) = NormalizeMultilineCommand(CommandText, Parameters);
                string verb = TikPath.Verb(normalCmd);

                if (verb == "add")
                {
                    return await Transport.InvokeRunAddAsync(BuildCommand(normalCmd, normalParams), cancellationToken).ConfigureAwait(false);
                }

                var readCmd = BuildCommand(normalCmd, ResolveParamsForRead(normalParams));
                IList<TikRecordSentence> rows;
                try
                {
                    rows = await Transport.InvokeRunPrintAsync(readCmd, cancellationToken).ConfigureAwait(false);
                }
                catch (TikNoSuchItemException)
                {
                    if (throwIfMissing)
                        throw;
                    return defaultValue;
                }
                return ScalarFromRows(rows, verb, target, throwIfMissing, defaultValue);
            }
            finally
            {
                EndRun();
            }
        }

        private async Task<ITikReSentence?> ExecuteSingleRowInternalAsync(bool throwIfMissing, CancellationToken cancellationToken)
        {
            EnsureConnectionSet();
            EnsureCommandTextSet();
            cancellationToken.ThrowIfCancellationRequested();   // Level 0 — see ExecuteNonQueryInternalAsync
            BeginRun();
            try
            {
                var rows = await Transport.InvokeRunPrintAsync(BuildReadDescriptor(), cancellationToken).ConfigureAwait(false);
                return TikCommandResult.SingleRow(this, rows, throwIfMissing);
            }
            finally
            {
                EndRun();
            }
        }

        private async Task<IList<ITikReSentence>> ExecuteListInternalAsync(CancellationToken cancellationToken,
            string[]? proplist = null)
        {
            EnsureConnectionSet();
            EnsureCommandTextSet();
            cancellationToken.ThrowIfCancellationRequested();   // Level 0 — see ExecuteNonQueryInternalAsync
            BeginRun();
            try
            {
                var rows = await Transport.InvokeRunPrintAsync(BuildReadDescriptor(proplist), cancellationToken).ConfigureAwait(false);
                return rows.Cast<ITikReSentence>().ToList();
            }
            finally
            {
                EndRun();
            }
        }

        // ── Unsupported: streaming / async ────────────────────────────────────

        public override IEnumerable<ITikReSentence> ExecuteListWithDuration(TimeSpan duration)
        {
            throw new TikConnectionCapabilityNotSupportedException(TikConnectionCapability.Streaming,
                "This transport does not support streaming commands (ExecuteListWithDuration). Use a transport that reports the Streaming capability (binary API).");
        }

        public override IEnumerable<ITikReSentence> ExecuteListWithDuration(TimeSpan duration, out bool wasAborted, out string? abortReason)
        {
            throw new TikConnectionCapabilityNotSupportedException(TikConnectionCapability.Streaming,
                "This transport does not support streaming commands (ExecuteListWithDuration). Use a transport that reports the Streaming capability (binary API).");
        }

        public override IEnumerable<ITikReSentence> ExecuteListUntilDone(TimeSpan? timeout = null)
        {
            // A self-terminating read is one read on these transports: the print path (the CLI family reaches its
            // monitor verbs from there). With a timeout it runs under a deadline on the transport's async path — the
            // path every built-in transport's synchronous print awaits with no token — so the timeout is honoured where
            // the transport can be interrupted. A transport without AsyncCommands has nothing a deadline can reach.
            if (!timeout.HasValue || !Connection.Supports(TikConnectionCapability.AsyncCommands))
                return ExecuteList();

            var limit = TikTimeSpans.ToDuration(timeout.Value);
            using (var deadline = new CancellationTokenSource(limit))
            {
                IList<ITikReSentence> rows;
                try
                {
                    rows = ExecuteListInternalAsync(deadline.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested)
                {
                    throw UntilDoneTimeout(timeout.Value);
                }
                // A transport that cancels cooperatively (the CLI's default) lets the read run out and hands the rows
                // over late: the deadline still passed first.
                if (deadline.IsCancellationRequested)
                    throw UntilDoneTimeout(timeout.Value);
                return rows;
            }
        }

        /// <summary>The binary API's wording for an <c>ExecuteListUntilDone</c> that outlived its timeout.</summary>
        private TikCommandAbortException UntilDoneTimeout(TimeSpan timeout)
            => new TikCommandAbortException(this, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Command did not finish within {0} second(s).", timeout.TotalSeconds));

        public override void ExecuteWithCallback(Action<ITikReSentence> oneResponseCallback,
            Action<ITikTrapSentence>? errorCallback = null,
            Action? onDoneCallback = null)
        {
            // Streaming monitors are an opt-in transport capability (ITikMonitorTransport): native WinBox M2
            // implements it, the CLI transports do not. Only those that cannot do it throw NotSupported.
            if (!(Transport is ITikMonitorTransport monitorTransport))
                throw new TikConnectionCapabilityNotSupportedException(TikConnectionCapability.Listen,
                    "This transport does not support asynchronous/listen commands. Use a transport that reports the Listen capability.");

            // Normalize a multi-line command (e.g. "/interface/print\n?type=ether\n?#|") into a clean command
            // path plus parsed Filter parameters, exactly like the synchronous read paths do.
            var (cmdText, allParams) = NormalizeMultilineCommand(CommandText, Parameters);
            var descriptor = new TikCommandDescriptor(cmdText, allParams);
            BeginRun();
            try
            {
                _monitorHandle = monitorTransport.RunMonitorAsync(descriptor,
                    row => oneResponseCallback?.Invoke(row),
                    trap => errorCallback?.Invoke(trap),
                    () => { EndRun(); onDoneCallback?.Invoke(); });
            }
            catch
            {
                EndRun();
                throw;
            }
        }

        public override void Cancel() { _monitorHandle?.Cancel(); }
        public override void CancelAndJoin() { _monitorHandle?.Join(-1); }
        public override bool CancelAndJoin(TimeSpan timeout)
        {
            int budget = TikTimeSpans.ToWaitMilliseconds(timeout, nameof(timeout));
            return _monitorHandle == null || _monitorHandle.Join(budget);
        }

        // ── Internal builder helpers ──────────────────────────────────────────

        /// <summary>
        /// Converts a normalized (command, params) pair into a <see cref="TikCommandDescriptor"/> used by RunPrint/RunNonQuery etc.
        /// </summary>
        private static TikCommandDescriptor BuildCommand(string commandText, IList<ITikCommandParameter> parameters)
        {
            return new TikCommandDescriptor(commandText, parameters);
        }

        // ── Multi-line normalization ──────────────────────────────────────────

        /// <summary>
        /// Splits a multi-line <see cref="TikCommandBase.CommandText"/> — the whole sentence, one word per line — into the
        /// command and the parameters its rows describe, appended to any the caller added directly.
        /// </summary>
        /// <remarks>
        /// The row parsing is <see cref="TikCommandRow"/>'s, not a second copy of it. A lenient inline version
        /// that recognises <c>?…</c> and <c>=…</c> and lets everything else fall out of the loop in silence
        /// turns a row one leading <c>=</c> short into no parameter at all, and the command goes out missing a
        /// filter and answers with the whole table — the defect <see cref="TikCommandRow"/> exists to end.
        /// <para>
        /// Blank lines are dropped before parsing: they are formatting rather than words, so they must not
        /// reach a parser that (correctly) refuses an empty row.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentException">A row is neither a parameter nor an API sentence marker.</exception>
        private static (string command, IList<ITikCommandParameter> parameters) NormalizeMultilineCommand(
            string commandText, IList<ITikCommandParameter> parameters)
        {
            if (!commandText.Contains('\n'))
                return (commandText, parameters);

            var lines = commandText.Split('\n');
            var filtered = new List<string>();
            foreach (var l in lines)
            {
                string t = l.Trim();
                if (!string.IsNullOrEmpty(t))
                    filtered.Add(t);
            }

            if (filtered.Count == 0)
                return (commandText, parameters);

            var allParams = new List<ITikCommandParameter>(parameters);
            allParams.AddRange(TikCommandRow.ParseParameters(filtered, 1));

            return (filtered[0], allParams);
        }

        /// <summary>
        /// Gives every <see cref="TikCommandParameterFormat.Default"/> parameter of a READ its actual format through
        /// <see cref="TikParameterFormat.Resolve"/>, with <see cref="TikCommandParameterFormat.Filter"/> as the use
        /// case: <c>.proplist</c> is the field list and <c>.tag</c> a tag, then the command's own
        /// <see cref="TikCommandBase.DefaultParameterFormat"/> when one was set, then a filter.
        /// </summary>
        /// <remarks>
        /// The command default is honoured: ignored, every unformatted parameter of a read would become a
        /// filter whatever the caller had said, and <c>CreateCommandAndParameters(path + "/print",
        /// NameValue, "detail", "")</c> — the documented way to say what the parameters ARE — would go out as
        /// <c>where detail=""</c>, which matches no row. Not an error, not a wrong value — an empty table, on a
        /// command the router never saw. The binary API resolves by the same function, so the two command
        /// implementations agree about what the same call means.
        /// <para>The Filter fallback is what makes a bare <c>CreateCommandAndParameters(path + "/print",
        /// "name", "ether1")</c> a filtered read, and it stays: it applies only when the caller has said
        /// nothing at all.</para>
        /// <para>Names with a <c>=</c> or <c>?</c> prefix carry their format in the name and are left as they are.</para>
        /// </remarks>
        private List<ITikCommandParameter> ResolveParamsForRead(IList<ITikCommandParameter> original)
        {
            var result = new List<ITikCommandParameter>(original.Count);
            foreach (var p in original)
            {
                if (p.ParameterFormat == TikCommandParameterFormat.Default
                    && !p.Name.StartsWith("=")
                    && !p.Name.StartsWith("?"))
                {
                    result.Add(new TikCommandParameter(p.Name, p.Value,
                        TikParameterFormat.Resolve(p, DefaultParameterFormat, TikCommandParameterFormat.Filter)));
                }
                else
                {
                    result.Add(p);
                }
            }
            return result;
        }
    }
}
