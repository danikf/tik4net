using System;
using System.Collections.Generic;
using System.Linq;

namespace tik4net.Connection
{
    /// <summary>
    /// Transport-neutral RouterOS command. Holds path + parameters and delegates execution to the
    /// owning <see cref="TikCommandConnectionBase"/> CRUD hooks (RunPrint/RunAdd/RunNonQuery);
    /// it does not itself build any transport-specific (CLI text / native M2) payload.
    /// </summary>
    internal class TikGenericCommand : ITikCommand
    {
        private readonly List<ITikCommandParameter> _parameters = new List<ITikCommandParameter>();
        private TikCommandConnectionBase _connection;
        private string _commandText;
        private TikCommandParameterFormat _defaultParameterFormat;
        private volatile bool _isRunning;
        private TikMonitorHandle _monitorHandle;

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
                var (cmd, p) = NormalizeMultilineCommand(_commandText, _parameters);
                _connection.RunNonQuery(BuildCommand(cmd, p));
            }
            finally
            {
                _isRunning = false;
            }
        }

        public string ExecuteScalar()
        {
            return ExecuteScalarInternal(null, throwIfMissing: true, defaultValue: null);
        }

        public string ExecuteScalar(string target)
        {
            return ExecuteScalarInternal(target, throwIfMissing: true, defaultValue: null);
        }

        public string ExecuteScalarOrDefault()
        {
            return ExecuteScalarInternal(null, throwIfMissing: false, defaultValue: null);
        }

        public string ExecuteScalarOrDefault(string defaultValue)
        {
            return ExecuteScalarInternal(null, throwIfMissing: false, defaultValue: defaultValue);
        }

        public string ExecuteScalarOrDefault(string defaultValue, string target)
        {
            return ExecuteScalarInternal(target, throwIfMissing: false, defaultValue: defaultValue);
        }

        private string ExecuteScalarInternal(string target, bool throwIfMissing, string defaultValue)
        {
            EnsureConnectionSet();
            EnsureCommandTextSet();
            _isRunning = true;
            try
            {
                var (normalCmd, normalParams) = NormalizeMultilineCommand(_commandText, _parameters);
                string verb = GetVerb(normalCmd);

                if (verb == "add")
                {
                    // :put [/path add k=v …] returns new .id
                    string newId = _connection.RunAdd(BuildCommand(normalCmd, normalParams));
                    if (newId == null)
                    {
                        if (throwIfMissing)
                            throw new TikNoSuchItemException(this);
                        return defaultValue;
                    }
                    return newId;
                }

                // For reads: resolve Default params to Filter, then read via print and pick the target
                // field from the row. We deliberately do NOT use 'get value-name=…' here: RouterOS
                // rejects 'get .id=*N' ("syntax error") and 'value-name=.id' ("input does not match any
                // value of value-name"). The print path (':put [/path print as-value where .id=*N]')
                // works for all fields including '.id'.
                var paramsForRead = ResolveParamsForRead(normalParams);
                var cmd = BuildCommand(normalCmd, paramsForRead);

                IList<TikRecordSentence> rows;
                try
                {
                    rows = _connection.RunPrint(cmd);
                }
                catch (TikNoSuchItemException)
                {
                    // Filtering by a non-matching/invalid .id yields "expected item id" on RouterOS.
                    // For the *OrDefault variants this means "not found" → return the default.
                    if (throwIfMissing)
                        throw;
                    return defaultValue;
                }
                if (rows.Count == 0)
                {
                    if (throwIfMissing)
                        throw new TikNoSuchItemException(this);
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
            finally
            {
                _isRunning = false;
            }
        }

        public ITikReSentence ExecuteSingleRow()
        {
            EnsureConnectionSet();
            EnsureCommandTextSet();
            _isRunning = true;
            try
            {
                var (cmd, p) = NormalizeMultilineCommand(_commandText, _parameters);
                var rows = _connection.RunPrint(BuildCommand(cmd, ResolveParamsForRead(p)));
                if (rows.Count == 0)
                    throw new TikNoSuchItemException(this);
                if (rows.Count > 1)
                    throw new TikCommandAmbiguousResultException(this, rows.Count);
                return rows[0];
            }
            finally
            {
                _isRunning = false;
            }
        }

        public ITikReSentence ExecuteSingleRowOrDefault()
        {
            EnsureConnectionSet();
            EnsureCommandTextSet();
            _isRunning = true;
            try
            {
                var (cmd, p) = NormalizeMultilineCommand(_commandText, _parameters);
                var rows = _connection.RunPrint(BuildCommand(cmd, ResolveParamsForRead(p)));
                if (rows.Count == 0)
                    return null;
                if (rows.Count > 1)
                    throw new TikCommandAmbiguousResultException(this, rows.Count);
                return rows[0];
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

        private IEnumerable<ITikReSentence> ExecuteListInternal(string[] proplist)
        {
            EnsureConnectionSet();
            EnsureCommandTextSet();
            _isRunning = true;
            try
            {
                var (cmd, baseParams) = NormalizeMultilineCommand(_commandText, _parameters);
                var paramsForRead = ResolveParamsForRead(baseParams);
                // proplist is ignored for CLI (as-value always returns all fields)
                return _connection.RunPrint(BuildCommand(cmd, paramsForRead));
            }
            finally
            {
                _isRunning = false;
            }
        }

        // ── Unsupported: streaming / async ────────────────────────────────────

        public IEnumerable<ITikReSentence> ExecuteListWithDuration(int durationSec)
        {
            throw new NotSupportedException("CLI transport does not support streaming commands. Use a transport that reports Streaming capability.");
        }

        public IEnumerable<ITikReSentence> ExecuteListWithDuration(int durationSec, out bool wasAborted, out string abortReason)
        {
            throw new NotSupportedException("CLI transport does not support streaming commands.");
        }

        public IEnumerable<ITikReSentence> ExecuteListUntilDone(int? timeoutSec = null)
        {
            // CLI is synchronous and single-shot — fallback to regular list (same as REST).
            return ExecuteList();
        }

        public void ExecuteAsync(Action<ITikReSentence> oneResponseCallback,
            Action<ITikTrapSentence> errorCallback = null,
            Action onDoneCallback = null)
        {
            // Streaming monitors are an opt-in transport capability (ITikMonitorTransport): native WinBox M2
            // implements it, the CLI transports do not. Only those that cannot do it throw NotSupported.
            if (!(_connection is ITikMonitorTransport monitorTransport))
                throw new NotSupportedException(
                    "This transport does not support asynchronous/listen commands. Use a transport that reports Listen capability.");

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

        private static string FindIdParam(IList<ITikCommandParameter> parameters)
        {
            foreach (var p in parameters)
            {
                if (p.Name == TikSpecialProperties.Id)
                    return p.Value;
            }
            return null;
        }

        // ── Multi-line normalization (same logic as RestCommand) ──────────────

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

            string actualCommand = filtered.Count > 0 ? filtered[0] : commandText;
            var allParams = new List<ITikCommandParameter>(parameters);

            for (int i = 1; i < filtered.Count; i++)
            {
                string line = filtered[i];
                if (line.StartsWith("?"))
                {
                    string raw = line.Substring(1);
                    int eq = raw.IndexOf('=');
                    if (eq >= 0)
                        allParams.Add(new TikCommandParameter(raw.Substring(0, eq), raw.Substring(eq + 1), TikCommandParameterFormat.Filter));
                    else
                        allParams.Add(new TikCommandParameter(raw, "", TikCommandParameterFormat.Filter));
                }
                else if (line.StartsWith("="))
                {
                    string raw = line.Substring(1);
                    int eq = raw.IndexOf('=');
                    if (eq >= 0)
                        allParams.Add(new TikCommandParameter(raw.Substring(0, eq), raw.Substring(eq + 1), TikCommandParameterFormat.NameValue));
                }
            }

            return (actualCommand, allParams);
        }

        private static List<ITikCommandParameter> ResolveParamsForRead(IList<ITikCommandParameter> original)
        {
            var result = new List<ITikCommandParameter>(original.Count);
            foreach (var p in original)
            {
                if (p.ParameterFormat == TikCommandParameterFormat.Default
                    && p.Name != TikSpecialProperties.Proplist
                    && p.Name != TikSpecialProperties.Tag
                    && !p.Name.StartsWith("=")
                    && !p.Name.StartsWith("?"))
                {
                    result.Add(new TikCommandParameter(p.Name, p.Value, TikCommandParameterFormat.Filter));
                }
                else
                {
                    result.Add(p);
                }
            }
            return result;
        }

        private static string GetVerb(string commandText)
        {
            if (string.IsNullOrWhiteSpace(commandText))
                return "print";
            var segments = commandText.TrimStart('/').Split('/');
            return segments[segments.Length - 1].ToLowerInvariant();
        }

        public override string ToString()
            => CommandText + " PARAMS: " + string.Join("; ", _parameters.Select(p => $"{p.Name}:{p.Value}"));
    }
}
