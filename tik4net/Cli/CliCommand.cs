using System;
using System.Collections.Generic;
using System.Linq;

namespace tik4net.Cli
{
    /// <summary>
    /// RouterOS CLI transport command. Mirrors <c>RestCommand</c> in structure;
    /// delegates execution to <see cref="CliConnectionBase"/>.
    /// </summary>
    internal class CliCommand : ITikCommand
    {
        private readonly List<ITikCommandParameter> _parameters = new List<ITikCommandParameter>();
        private CliConnectionBase _connection;
        private string _commandText;
        private TikCommandParameterFormat _defaultParameterFormat;
        private volatile bool _isRunning;

        public ITikConnection Connection
        {
            get { return _connection; }
            set
            {
                Guard.ArgumentOfType<CliConnectionBase>(value, "connection");
                _connection = (CliConnectionBase)value;
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

        public CliCommand()
        {
            _defaultParameterFormat = TikCommandParameterFormat.Default;
        }

        public CliCommand(TikCommandParameterFormat defaultParameterFormat)
        {
            _defaultParameterFormat = defaultParameterFormat;
        }

        public CliCommand(CliConnectionBase connection) : this()
        {
            Connection = connection;
        }

        public CliCommand(CliConnectionBase connection, TikCommandParameterFormat defaultParameterFormat)
            : this(defaultParameterFormat)
        {
            Connection = connection;
        }

        public CliCommand(CliConnectionBase connection, string commandText)
            : this(connection)
        {
            CommandText = commandText;
        }

        public CliCommand(CliConnectionBase connection, string commandText, TikCommandParameterFormat defaultParameterFormat)
            : this(connection, defaultParameterFormat)
        {
            CommandText = commandText;
        }

        public CliCommand(CliConnectionBase connection, string commandText, params ITikCommandParameter[] parameters)
            : this(connection, commandText)
        {
            _parameters.AddRange(parameters);
        }

        public CliCommand(CliConnectionBase connection, string commandText, TikCommandParameterFormat defaultParameterFormat, params ITikCommandParameter[] parameters)
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

                // For reads: resolve Default params to Filter, then read list
                var paramsForRead = ResolveParamsForRead(normalParams);
                var cmd = BuildCommand(normalCmd, paramsForRead);

                // If a specific target field is requested and the entity has an .id, use get
                string idValue = FindIdParam(paramsForRead);
                if (!string.IsNullOrEmpty(target) && !string.IsNullOrEmpty(idValue))
                {
                    string getCmd = CliCommandBuilder.BuildGetScalar(normalCmd, idValue, target);
                    string rawVal = _connection.RunScalarGet(getCmd);
                    if (rawVal == null)
                    {
                        if (throwIfMissing)
                            throw new TikNoSuchItemException(this);
                        return defaultValue;
                    }
                    return rawVal;
                }

                var rows = _connection.RunPrint(cmd);
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
            throw new NotSupportedException("CLI transport does not support asynchronous/listen commands. Use a transport that reports Listen capability.");
        }

        public void Cancel() { /* no-op for CLI */ }
        public void CancelAndJoin() { /* no-op for CLI */ }
        public bool CancelAndJoin(int milisecondsTimeout) { return true; }

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
            return new CliCommandParameter(name, value, fmt);
        }

        // ── Internal builder helpers ──────────────────────────────────────────

        /// <summary>
        /// Converts a normalized (command, params) pair into a CliCommandDescriptor used by RunPrint/RunNonQuery etc.
        /// </summary>
        private static CliCommandDescriptor BuildCommand(string commandText, IList<ITikCommandParameter> parameters)
        {
            return new CliCommandDescriptor(commandText, parameters);
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
                        allParams.Add(new CliCommandParameter(raw.Substring(0, eq), raw.Substring(eq + 1), TikCommandParameterFormat.Filter));
                    else
                        allParams.Add(new CliCommandParameter(raw, "", TikCommandParameterFormat.Filter));
                }
                else if (line.StartsWith("="))
                {
                    string raw = line.Substring(1);
                    int eq = raw.IndexOf('=');
                    if (eq >= 0)
                        allParams.Add(new CliCommandParameter(raw.Substring(0, eq), raw.Substring(eq + 1), TikCommandParameterFormat.NameValue));
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
                    result.Add(new CliCommandParameter(p.Name, p.Value, TikCommandParameterFormat.Filter));
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

    /// <summary>
    /// Lightweight descriptor passed from <see cref="CliCommand"/> to <see cref="CliConnectionBase"/>.
    /// Avoids exposing the full CliCommand internals to the connection.
    /// </summary>
    internal sealed class CliCommandDescriptor
    {
        internal string CommandText { get; }
        internal IList<ITikCommandParameter> Parameters { get; }

        internal CliCommandDescriptor(string commandText, IList<ITikCommandParameter> parameters)
        {
            CommandText = commandText;
            Parameters = parameters;
        }
    }
}
