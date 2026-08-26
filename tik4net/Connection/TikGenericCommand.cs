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
    internal class TikGenericCommand : ITikCommand, ITikRawCommand, ITikCommandAsync
    {
        private readonly List<ITikCommandParameter> _parameters = new List<ITikCommandParameter>();
        private TikCommandConnectionBase _connection = null!; // set via Connection property before EnsureConnectionSet() is called
        private string _commandText = null!; // set via CommandText property before EnsureCommandTextSet() is called
        private TikCommandParameterFormat _defaultParameterFormat;
        private volatile bool _isRunning;
        private TikMonitorHandle? _monitorHandle; // only set once ExecuteAsync has been called; see Cancel/CancelAndJoin's null-conditional use

        // ── Raw pass-through (ITikRawCommand) ──────────────────────────────────
        // Set by the CreateRawCommand factory. When IsRaw, CommandText is sent verbatim through the
        // transport (no CliCommandBuilder, no multiline/param normalization); see the raw branches below.
        bool ITikRawCommand.IsRaw { get => _isRaw; set => _isRaw = value; }
        bool ITikRawCommand.WrapAsValue { get => _wrapAsValue; set => _wrapAsValue = value; }
        private bool _isRaw;
        private bool _wrapAsValue;

        private TikCommandDescriptor BuildRawDescriptor()
            => new TikCommandDescriptor(_commandText, new List<ITikCommandParameter>(), isRaw: true, wrapAsValue: _wrapAsValue);

        public ITikConnection Connection
        {
            get { return _connection; }
            set
            {
                Guard.ArgumentOfType<TikCommandConnectionBase>(value, "connection");
                _connection = (TikCommandConnectionBase)value;
            }
        }

        public string CommandText
        {
            get { return _commandText; }
            set { _commandText = value; }
        }

        public bool IsRunning => _isRunning;

        public IList<ITikCommandParameter> Parameters => _parameters;

        public TikCommandParameterFormat DefaultParameterFormat
        {
            get { return _defaultParameterFormat; }
            set { _defaultParameterFormat = value; }
        }

        public TikGenericCommand()
        {
            _defaultParameterFormat = TikCommandParameterFormat.Default;
        }

        public TikGenericCommand(TikCommandParameterFormat defaultParameterFormat)
        {
            _defaultParameterFormat = defaultParameterFormat;
        }

        public TikGenericCommand(TikCommandConnectionBase connection) : this()
        {
            Connection = connection;
        }

        public TikGenericCommand(TikCommandConnectionBase connection, TikCommandParameterFormat defaultParameterFormat)
            : this(defaultParameterFormat)
        {
            Connection = connection;
        }

        public TikGenericCommand(TikCommandConnectionBase connection, string commandText)
            : this(connection)
        {
            CommandText = commandText;
        }

        public TikGenericCommand(TikCommandConnectionBase connection, string commandText, TikCommandParameterFormat defaultParameterFormat)
            : this(connection, defaultParameterFormat)
        {
            CommandText = commandText;
        }

        public TikGenericCommand(TikCommandConnectionBase connection, string commandText, params ITikCommandParameter[] parameters)
            : this(connection, commandText)
        {
            _parameters.AddRange(parameters);
        }

        public TikGenericCommand(TikCommandConnectionBase connection, string commandText, TikCommandParameterFormat defaultParameterFormat, params ITikCommandParameter[] parameters)
            : this(connection, commandText, defaultParameterFormat)
        {
            _parameters.AddRange(parameters);
        }

        private void EnsureConnectionSet()
        {
            if (_connection == null)
                throw new InvalidOperationException("Connection is not assigned.");
        }

        private void EnsureCommandTextSet()
        {
            if (string.IsNullOrWhiteSpace(_commandText))
                throw new InvalidOperationException("CommandText is not set.");
        }

        // ── Execute methods ───────────────────────────────────────────────────

        public void ExecuteNonQuery()
        {
            EnsureConnectionSet();
            EnsureCommandTextSet();
            _isRunning = true;
            try
            {
                if (_isRaw)
                {
                    // Fire-and-forget raw send; response discarded.
                    _connection.InvokeRunRawText(BuildRawDescriptor());
                    return;
                }
                var (cmd, p) = NormalizeMultilineCommand(_commandText, _parameters);
                _connection.InvokeRunNonQuery(BuildCommand(cmd, p));
            }
            finally
            {
                _isRunning = false;
            }
        }

        public string ExecuteScalar()
        {
            // throwIfMissing:true - ExecuteScalarInternal either returns a real value or throws, never null.
            return ExecuteScalarInternal(null, throwIfMissing: true, defaultValue: null)!;
        }

        public string ExecuteScalar(string target)
        {
            // throwIfMissing:true - ExecuteScalarInternal either returns a real value or throws, never null.
            return ExecuteScalarInternal(target, throwIfMissing: true, defaultValue: null)!;
        }

        public string? ExecuteScalarOrDefault()
        {
            // Returns null (or the caller's defaultValue, which may be null) when nothing matched —
            // the interface says so and now types it that way.
            return ExecuteScalarInternal(null, throwIfMissing: false, defaultValue: null);
        }

        public string? ExecuteScalarOrDefault(string? defaultValue)
        {
            return ExecuteScalarInternal(null, throwIfMissing: false, defaultValue: defaultValue);
        }

        public string? ExecuteScalarOrDefault(string? defaultValue, string target)
        {
            return ExecuteScalarInternal(target, throwIfMissing: false, defaultValue: defaultValue);
        }

        private string? ExecuteScalarInternal(string? target, bool throwIfMissing, string? defaultValue)
        {
            EnsureConnectionSet();
            EnsureCommandTextSet();
            _isRunning = true;
            try
            {
                if (_isRaw)
                    return ScalarFromRawText(_connection.InvokeRunRawText(BuildRawDescriptor()), throwIfMissing, defaultValue);

                var (normalCmd, normalParams) = NormalizeMultilineCommand(_commandText, _parameters);
                string verb = TikPath.Verb(normalCmd);

                if (verb == "add")
                    return ScalarFromAddedId(_connection.InvokeRunAdd(BuildCommand(normalCmd, normalParams)), throwIfMissing, defaultValue);

                var cmd = BuildCommand(normalCmd, ResolveParamsForRead(normalParams));
                IList<TikRecordSentence> rows;
                try
                {
                    rows = _connection.InvokeRunPrint(cmd);
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
                _isRunning = false;
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
        {
            if (!string.IsNullOrEmpty(rawText))
                return rawText;

            // Empty output is NOT "no such item": a raw command has no record model, and the silent answer is
            // exactly what a successful write prints over the CLI (set/unset/remove/enable/comment all echo
            // nothing). Reporting that as TikNoSuchItemException fabricated a router error for a command that
            // had worked.
            if (throwIfMissing)
                throw new TikCommandEmptyResponseException(this,
                    "Raw command returned no output, so there is no scalar value to return. "
                    + "Commands that print nothing (set/unset/remove/enable/comment/…) succeed silently — "
                    + "run them with ExecuteNonQuery(), or use ExecuteScalarOrDefault() when the output is optional.");
            return defaultValue;
        }

        /// <summary>Scalar of an <c>add</c>: the new record's <c>.id</c> (<c>:put [/path add k=v …]</c>).</summary>
        private string? ScalarFromAddedId(string newId, bool throwIfMissing, string? defaultValue)
        {
            if (newId != null)
                return newId;

            // The add itself did not fail — a failing add raises a trap in RunAdd. Getting here means the
            // router accepted the row but answered without the new .id, so saying "no such item" pointed the
            // caller at a nonexistent record instead of at the missing return value (and the row is on the
            // router either way).
            if (throwIfMissing)
                throw new TikCommandEmptyResponseException(this,
                    "The router accepted the add but did not return the new .id. "
                    + "The record was most probably created — re-read the table to obtain its id.");
            return defaultValue;
        }

        /// <summary>
        /// Scalar of a read: pick <paramref name="target"/> (or the first non-<c>.id</c> field) out of the single
        /// returned row. Reads go through <c>print</c> rather than <c>get value-name=…</c> on purpose: RouterOS
        /// rejects <c>get .id=*N</c> ("syntax error") and <c>value-name=.id</c> ("input does not match any value
        /// of value-name"), while <c>:put [/path print as-value where .id=*N]</c> works for every field.
        /// </summary>
        private string? ScalarFromRows(IList<TikRecordSentence> rows, string verb, string? target, bool throwIfMissing, string? defaultValue)
        {
            if (rows.Count == 0)
            {
                if (throwIfMissing)
                    // A read that matched nothing really is 'no such item'; any other verb printing
                    // nothing has simply succeeded without a return value (same rule as the binary API
                    // applies to a bare !done — the two shapes are indistinguishable without the verb).
                    throw IsReadVerb(verb)
                        ? (TikCommandException)new TikNoSuchItemException(this)
                        : new TikCommandEmptyResponseException(this,
                            $"'{verb}' returned no output, so there is no scalar value to return. "
                            + "Commands that print nothing (set/unset/remove/enable/comment/…) succeed silently — "
                            + "run them with ExecuteNonQuery(), or use ExecuteScalarOrDefault() when the output is optional.");
                return defaultValue;
            }
            if (rows.Count > 1)
                throw new TikCommandUnexpectedResponseException("Single value expected but multiple rows returned.", this, rows.Cast<ITikSentence>());

            var single = rows[0];
            string fieldToRead = target
                ?? single.Words.Keys.FirstOrDefault(k => k != TikSpecialProperties.Id && k != TikSpecialProperties.Tag)
                ?? TikSpecialProperties.Id;
            if (single.TryGetResponseField(fieldToRead, out var val))
                return val;
            if (throwIfMissing)
                throw new TikSentenceException($"Field '{fieldToRead}' not found in CLI response.", single);
            return defaultValue;
        }

        /// <summary>Single-row result interpretation, shared by the sync and Task-based paths.</summary>
        private ITikReSentence? SingleRowFromRows(IList<TikRecordSentence> rows, bool throwIfMissing)
        {
            if (rows.Count == 0)
            {
                if (throwIfMissing)
                    throw new TikNoSuchItemException(this);
                return null;
            }
            if (rows.Count > 1)
                throw new TikCommandAmbiguousResultException(this, rows.Count);
            return rows[0];
        }

        // ExecuteSingleRow's throwIfMissing:true never lets SingleRowFromRows return null.
        public ITikReSentence ExecuteSingleRow() => ExecuteSingleRowInternal(throwIfMissing: true)!;

        // ITikCommand.ExecuteSingleRowOrDefault() declares a non-nullable return, but the XML doc says it
        // returns null when nothing matched. The `!` matches the (non-nullable) interface signature, not
        // actual runtime behaviour - same tradeoff as ExecuteScalarOrDefault() above.
        public ITikReSentence ExecuteSingleRowOrDefault() => ExecuteSingleRowInternal(throwIfMissing: false)!;

        private ITikReSentence? ExecuteSingleRowInternal(bool throwIfMissing)
        {
            EnsureConnectionSet();
            EnsureCommandTextSet();
            _isRunning = true;
            try
            {
                return SingleRowFromRows(_connection.InvokeRunPrint(BuildReadDescriptor()), throwIfMissing);
            }
            finally
            {
                _isRunning = false;
            }
        }

        public IEnumerable<ITikReSentence> ExecuteList()
        {
            return ExecuteListInternal(null);
        }

        public IEnumerable<ITikReSentence> ExecuteList(params string[] proplistFields)
        {
            return ExecuteListInternal(proplistFields);
        }

        private IEnumerable<ITikReSentence> ExecuteListInternal(string[]? proplist)
        {
            EnsureConnectionSet();
            EnsureCommandTextSet();
            _isRunning = true;
            try
            {
                // proplist is ignored for CLI (as-value always returns all fields)
                return _connection.InvokeRunPrint(BuildReadDescriptor());
            }
            finally
            {
                _isRunning = false;
            }
        }

        /// <summary>
        /// True for the read verbs, where an empty result set means "nothing matched". Every other verb
        /// prints nothing on <b>success</b>, so there an empty result set means "this command has no return
        /// value" (<see cref="TikCommandEmptyResponseException"/>). Kept in step with the binary API's
        /// equivalent test in <c>ApiCommand.IsReadVerb</c>.
        /// </summary>
        private static bool IsReadVerb(string verb)
        {
            switch (verb)
            {
                case "print":
                case "get":
                case "getall":
                    return true;
                default:
                    return false;
            }
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
            var (cmd, baseParams) = NormalizeMultilineCommand(_commandText, _parameters);
            return BuildCommand(cmd, ResolveParamsForRead(baseParams));
        }

        // ── ITikCommandAsync ──────────────────────────────────────────────────
        //
        // Deliberately mirrors the synchronous methods above instead of either side wrapping the other: the sync
        // path must keep working on transports whose async hooks are not implemented yet (they throw), and running
        // the async path over a blocking hook would be the Task.Run façade the design rules out. What the two
        // paths must NOT do is interpret a response differently, so everything after the Run*/Run*Async call is
        // the shared Scalar*/SingleRowFromRows helpers.
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

        // proplist is ignored here for the same reason as in the synchronous ExecuteList: the CLI's as-value form
        // always returns every field, and REST answers with the whole object.
        Task<IList<ITikReSentence>> ITikCommandAsync.ExecuteListAsync(string[] proplistFields, CancellationToken cancellationToken)
            => ExecuteListInternalAsync(cancellationToken);

        private async Task ExecuteNonQueryInternalAsync(CancellationToken cancellationToken)
        {
            EnsureConnectionSet();
            EnsureCommandTextSet();
            // Level 0 of the cancellation contract, applied here so it holds on every transport rather than
            // depending on each one remembering: a token that is already cancelled sends nothing at all.
            cancellationToken.ThrowIfCancellationRequested();
            _isRunning = true;
            try
            {
                if (_isRaw)
                {
                    // Fire-and-forget raw send; response discarded.
                    await _connection.InvokeRunRawTextAsync(BuildRawDescriptor(), cancellationToken).ConfigureAwait(false);
                    return;
                }
                var (cmd, p) = NormalizeMultilineCommand(_commandText, _parameters);
                await _connection.InvokeRunNonQueryAsync(BuildCommand(cmd, p), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _isRunning = false;
            }
        }

        // Task<string> here is ITikCommandAsync's declared (non-nullable) return type, but throwIfMissing:false
        // callers genuinely complete with a null defaultValue - see ExecuteScalarOrDefault()'s note above. The
        // `!` on every branch below matches the interface signature, not actual runtime behaviour.
        private async Task<string?> ExecuteScalarInternalAsync(string? target, bool throwIfMissing, string? defaultValue,
            CancellationToken cancellationToken)
        {
            EnsureConnectionSet();
            EnsureCommandTextSet();
            cancellationToken.ThrowIfCancellationRequested();   // Level 0 — see ExecuteNonQueryInternalAsync
            _isRunning = true;
            try
            {
                if (_isRaw)
                {
                    string rawText = await _connection.InvokeRunRawTextAsync(BuildRawDescriptor(), cancellationToken).ConfigureAwait(false);
                    return ScalarFromRawText(rawText, throwIfMissing, defaultValue)!;
                }

                var (normalCmd, normalParams) = NormalizeMultilineCommand(_commandText, _parameters);
                string verb = TikPath.Verb(normalCmd);

                if (verb == "add")
                {
                    string newId = await _connection.InvokeRunAddAsync(BuildCommand(normalCmd, normalParams), cancellationToken).ConfigureAwait(false);
                    return ScalarFromAddedId(newId, throwIfMissing, defaultValue)!;
                }

                var readCmd = BuildCommand(normalCmd, ResolveParamsForRead(normalParams));
                IList<TikRecordSentence> rows;
                try
                {
                    rows = await _connection.InvokeRunPrintAsync(readCmd, cancellationToken).ConfigureAwait(false);
                }
                catch (TikNoSuchItemException)
                {
                    if (throwIfMissing)
                        throw;
                    return defaultValue!;
                }
                return ScalarFromRows(rows, verb, target, throwIfMissing, defaultValue)!;
            }
            finally
            {
                _isRunning = false;
            }
        }

        // Task<ITikReSentence> here is ITikCommandAsync's declared (non-nullable) return type, but
        // throwIfMissing:false genuinely completes with null - see ExecuteSingleRowOrDefault()'s note above.
        private async Task<ITikReSentence?> ExecuteSingleRowInternalAsync(bool throwIfMissing, CancellationToken cancellationToken)
        {
            EnsureConnectionSet();
            EnsureCommandTextSet();
            cancellationToken.ThrowIfCancellationRequested();   // Level 0 — see ExecuteNonQueryInternalAsync
            _isRunning = true;
            try
            {
                var rows = await _connection.InvokeRunPrintAsync(BuildReadDescriptor(), cancellationToken).ConfigureAwait(false);
                return SingleRowFromRows(rows, throwIfMissing)!;
            }
            finally
            {
                _isRunning = false;
            }
        }

        private async Task<IList<ITikReSentence>> ExecuteListInternalAsync(CancellationToken cancellationToken)
        {
            EnsureConnectionSet();
            EnsureCommandTextSet();
            cancellationToken.ThrowIfCancellationRequested();   // Level 0 — see ExecuteNonQueryInternalAsync
            _isRunning = true;
            try
            {
                var rows = await _connection.InvokeRunPrintAsync(BuildReadDescriptor(), cancellationToken).ConfigureAwait(false);
                return rows.Cast<ITikReSentence>().ToList();
            }
            finally
            {
                _isRunning = false;
            }
        }

        // ── Unsupported: streaming / async ────────────────────────────────────

        public IEnumerable<ITikReSentence> ExecuteListWithDuration(int durationSec)
        {
            throw new TikConnectionCapabilityNotSupportedException(TikConnectionCapability.Streaming,
                "This transport does not support streaming commands (ExecuteListWithDuration). Use a transport that reports the Streaming capability (binary API).");
        }

        public IEnumerable<ITikReSentence> ExecuteListWithDuration(int durationSec, out bool wasAborted, out string abortReason)
        {
            throw new TikConnectionCapabilityNotSupportedException(TikConnectionCapability.Streaming,
                "This transport does not support streaming commands (ExecuteListWithDuration). Use a transport that reports the Streaming capability (binary API).");
        }

        public IEnumerable<ITikReSentence> ExecuteListUntilDone(int? timeoutSec = null)
        {
            // CLI is synchronous and single-shot — fallback to regular list (same as REST).
            return ExecuteList();
        }

        public void ExecuteAsync(Action<ITikReSentence> oneResponseCallback,
            Action<ITikTrapSentence>? errorCallback = null,
            Action? onDoneCallback = null)
        {
            // Streaming monitors are an opt-in transport capability (ITikMonitorTransport): native WinBox M2
            // implements it, the CLI transports do not. Only those that cannot do it throw NotSupported.
            if (!(_connection is ITikMonitorTransport monitorTransport))
                throw new TikConnectionCapabilityNotSupportedException(TikConnectionCapability.Listen,
                    "This transport does not support asynchronous/listen commands. Use a transport that reports the Listen capability.");

            // Normalize a multi-line command (e.g. "/interface/print\n?type=ether\n?#|") into a clean command
            // path plus parsed Filter parameters, exactly like the synchronous read paths do.
            var (cmdText, allParams) = NormalizeMultilineCommand(_commandText, _parameters);
            var descriptor = new TikCommandDescriptor(cmdText, allParams);
            _isRunning = true;
            try
            {
                _monitorHandle = monitorTransport.RunMonitorAsync(descriptor,
                    row => oneResponseCallback?.Invoke(row),
                    trap => errorCallback?.Invoke(trap),
                    () => { _isRunning = false; onDoneCallback?.Invoke(); });
            }
            catch
            {
                _isRunning = false;
                throw;
            }
        }

        public void Cancel() { _monitorHandle?.Cancel(); }
        public void CancelAndJoin() { _monitorHandle?.Join(-1); }
        public bool CancelAndJoin(int milisecondsTimeout)
            => _monitorHandle == null || _monitorHandle.Join(milisecondsTimeout);

        // ── Parameter helpers ─────────────────────────────────────────────────

        public ITikCommandParameter AddParameter(string name, string value)
        {
            var p = CreateParameter(name, value);
            _parameters.Add(p);
            return p;
        }

        public ITikCommandParameter AddParameter(string name, string value, TikCommandParameterFormat parameterFormat)
        {
            var p = CreateParameter(name, value, parameterFormat);
            _parameters.Add(p);
            return p;
        }

        public ITikCommand WithParameter(string name, string value)
        {
            AddParameter(name, value);
            return this;
        }

        public ITikCommand WithParameter(string name, string value, TikCommandParameterFormat parameterFormat)
        {
            AddParameter(name, value, parameterFormat);
            return this;
        }

        public IEnumerable<ITikCommandParameter> AddParameterAndValues(params string[] parameterNamesAndValues)
        {
            var result = new List<ITikCommandParameter>();
            for (int i = 0; i < parameterNamesAndValues.Length / 2; i++)
            {
                var p = CreateParameter(parameterNamesAndValues[i * 2], parameterNamesAndValues[i * 2 + 1]);
                _parameters.Add(p);
                result.Add(p);
            }
            return result;
        }

        private static ITikCommandParameter CreateParameter(string name, string value, TikCommandParameterFormat fmt = TikCommandParameterFormat.Default)
        {
            return new TikCommandParameter(name, value, fmt);
        }

        // ── Internal builder helpers ──────────────────────────────────────────

        /// <summary>
        /// Converts a normalized (command, params) pair into a <see cref="TikCommandDescriptor"/> used by RunPrint/RunNonQuery etc.
        /// </summary>
        private static TikCommandDescriptor BuildCommand(string commandText, IList<ITikCommandParameter> parameters)
        {
            return new TikCommandDescriptor(commandText, parameters);
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

        // ── Multi-line normalization (same logic as RestCommand) ──────────────

        /// <summary>
        /// Splits a multi-line <see cref="CommandText"/> — the whole sentence, one word per line — into the
        /// command and the parameters its rows describe, appended to any the caller added directly.
        /// </summary>
        /// <remarks>
        /// The row parsing is <see cref="TikCommandRow"/>'s, not a second copy of it. It used to be one: a
        /// lenient inline version that recognised <c>?…</c> and <c>=…</c> and let everything else fall out
        /// of the bottom of the loop in silence — so a row one leading <c>=</c> short became no parameter at
        /// all, and the command went out missing a filter and answered with the whole table. That is the
        /// exact defect <see cref="TikCommandRow"/> was written to end, and keeping two parsers of one
        /// format meant only one of them knew it.
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
        /// Gives every <see cref="TikCommandParameterFormat.Default"/> parameter of a READ its actual
        /// format: the command's own <see cref="DefaultParameterFormat"/> when one was set, and
        /// <see cref="TikCommandParameterFormat.Filter"/> otherwise.
        /// </summary>
        /// <remarks>
        /// The command default used to be ignored here, so every unformatted parameter of a read became a
        /// filter whatever the caller had said. <c>CreateCommandAndParameters(path + "/print",
        /// NameValue, "detail", "")</c> — the documented way to say what the parameters ARE — went out as
        /// <c>where detail=""</c>, which matches no row: two rows over Telnet when the same parameter was
        /// built with <c>CreateParameter(..., NameValue)</c>, and none this way. Not an error, not a wrong
        /// value — an empty table, on a command the router never saw. The binary API has always resolved it
        /// in this order (parameter → command default → use-case default; see <c>ApiCommand</c>), so the two
        /// command implementations no longer disagree about what the same call means.
        /// <para>The Filter fallback is what makes a bare <c>CreateCommandAndParameters(path + "/print",
        /// "name", "ether1")</c> a filtered read, and it stays: it applies only when the caller has said
        /// nothing at all.</para>
        /// </remarks>
        private List<ITikCommandParameter> ResolveParamsForRead(IList<ITikCommandParameter> original)
        {
            TikCommandParameterFormat forUnformatted =
                _defaultParameterFormat != TikCommandParameterFormat.Default
                    ? _defaultParameterFormat
                    : TikCommandParameterFormat.Filter;

            var result = new List<ITikCommandParameter>(original.Count);
            foreach (var p in original)
            {
                if (p.ParameterFormat == TikCommandParameterFormat.Default
                    && p.Name != TikSpecialProperties.Proplist
                    && p.Name != TikSpecialProperties.Tag
                    && !p.Name.StartsWith("=")
                    && !p.Name.StartsWith("?"))
                {
                    result.Add(new TikCommandParameter(p.Name, p.Value, forUnformatted));
                }
                else
                {
                    result.Add(p);
                }
            }
            return result;
        }

        public override string ToString()
            => CommandText + " PARAMS: " + string.Join("; ", _parameters.Select(p => $"{p.Name}:{p.Value}"));
    }
}
