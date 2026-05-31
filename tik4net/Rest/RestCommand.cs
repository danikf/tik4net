using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace tik4net.Rest
{
    internal class RestCommand : ITikCommand
    {
        private readonly List<ITikCommandParameter> _parameters = new List<ITikCommandParameter>();
        private RestConnection _connection;
        private string _commandText;
        private TikCommandParameterFormat _defaultParameterFormat;
        private volatile bool _isRunning;

        public ITikConnection Connection
        {
            get { return _connection; }
            set
            {
                Guard.ArgumentOfType<RestConnection>(value, "connection");
                _connection = (RestConnection)value;
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

        public RestCommand()
        {
            _defaultParameterFormat = TikCommandParameterFormat.Default;
        }

        public RestCommand(TikCommandParameterFormat defaultParameterFormat)
        {
            _defaultParameterFormat = defaultParameterFormat;
        }

        public RestCommand(RestConnection connection) : this()
        {
            Connection = connection;
        }

        public RestCommand(RestConnection connection, TikCommandParameterFormat defaultParameterFormat)
            : this(defaultParameterFormat)
        {
            Connection = connection;
        }

        public RestCommand(RestConnection connection, string commandText)
            : this(connection)
        {
            CommandText = commandText;
        }

        public RestCommand(RestConnection connection, string commandText, TikCommandParameterFormat defaultParameterFormat)
            : this(connection, defaultParameterFormat)
        {
            CommandText = commandText;
        }

        public RestCommand(RestConnection connection, string commandText, params ITikCommandParameter[] parameters)
            : this(connection, commandText)
        {
            _parameters.AddRange(parameters);
        }

        public RestCommand(RestConnection connection, string commandText, TikCommandParameterFormat defaultParameterFormat, params ITikCommandParameter[] parameters)
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
                _connection.ExecuteRequest(cmd, p);
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

                // For add commands: the PUT response carries the new .id
                string verb = GetVerb(normalCmd);
                if (verb == "add")
                {
                    var row = _connection.ExecuteRequestSingle(normalCmd, normalParams);
                    if (row == null)
                    {
                        if (throwIfMissing)
                            throw new TikNoSuchItemException(this);
                        return defaultValue;
                    }
                    // Return .id (or target field if specified)
                    string field = target ?? TikSpecialProperties.Id;
                    if (row.TryGetResponseField(field, out var idVal))
                        return idVal;
                    if (throwIfMissing)
                        throw new TikSentenceException($"Field '{field}' not found in REST add response.", row);
                    return defaultValue;
                }

                // For reads: resolve Default params to Filter, add proplist if target specified
                var paramsForRead = ResolveParamsForRead(normalParams);
                if (!string.IsNullOrEmpty(target))
                {
                    paramsForRead.Add(CreateParameter(TikSpecialProperties.Proplist, target, TikCommandParameterFormat.NameValue));
                }

                var rows = _connection.ExecuteRequestList(normalCmd, paramsForRead);
                if (rows.Count == 0)
                {
                    if (throwIfMissing)
                        throw new TikNoSuchItemException(this);
                    return defaultValue;
                }
                if (rows.Count > 1)
                    throw new TikCommandUnexpectedResponseException("Single value expected but multiple rows returned.", this, rows.Cast<ITikSentence>());

                var single = rows[0];
                string fieldToRead = target ?? single.Words.Keys.FirstOrDefault(k => k != TikSpecialProperties.Id && k != TikSpecialProperties.Tag) ?? TikSpecialProperties.Id;
                if (single.TryGetResponseField(fieldToRead, out var val))
                    return val;
                if (throwIfMissing)
                    throw new TikSentenceException($"Field '{fieldToRead}' not found in REST response.", single);
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
                var rows = _connection.ExecuteRequestList(cmd, ResolveParamsForRead(p));
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
                var rows = _connection.ExecuteRequestList(cmd, ResolveParamsForRead(p));
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
                if (proplist != null && proplist.Length > 0)
                {
                    paramsForRead.Add(CreateParameter(TikSpecialProperties.Proplist, string.Join(",", proplist), TikCommandParameterFormat.NameValue));
                }
                return _connection.ExecuteRequestList(cmd, paramsForRead);
            }
            finally
            {
                _isRunning = false;
            }
        }

        /// <summary>
        /// Multi-line API command text (used e.g. in ApiCommand) embeds parameters as additional lines.
        /// This extracts the single-line command and merges inline parameters with the parameter list.
        /// </summary>
        private static (string command, IList<ITikCommandParameter> parameters) NormalizeMultilineCommand(
            string commandText, IList<ITikCommandParameter> parameters)
        {
            if (!commandText.Contains('\n'))
                return (commandText, parameters);

            var lines = commandText.Split('\n')
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l))
                .ToArray();

            string actualCommand = lines[0];
            var allParams = new List<ITikCommandParameter>(parameters);

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.StartsWith("?"))
                {
                    string raw = line.Substring(1);  // strip leading ?
                    int eq = raw.IndexOf('=');
                    if (eq >= 0)
                        allParams.Add(new RestCommandParameter(raw.Substring(0, eq), raw.Substring(eq + 1), TikCommandParameterFormat.Filter));
                    else
                        allParams.Add(new RestCommandParameter(raw, "", TikCommandParameterFormat.Filter));
                }
                else if (line.StartsWith("="))
                {
                    string raw = line.Substring(1);
                    int eq = raw.IndexOf('=');
                    if (eq >= 0)
                        allParams.Add(new RestCommandParameter(raw.Substring(0, eq), raw.Substring(eq + 1), TikCommandParameterFormat.NameValue));
                }
            }

            return (actualCommand, allParams);
        }

        /// <summary>
        /// For read operations: Default-format params are treated as Filter (matching ApiCommand behaviour).
        /// </summary>
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
                    result.Add(new RestCommandParameter(p.Name, p.Value, TikCommandParameterFormat.Filter));
                }
                else
                {
                    result.Add(p);
                }
            }
            return result;
        }

        // ── Unsupported: streaming / async ────────────────────────────────────

        public IEnumerable<ITikReSentence> ExecuteListWithDuration(int durationSec)
        {
            throw new NotSupportedException("REST transport does not support streaming commands. Use a transport that reports Streaming capability.");
        }

        public IEnumerable<ITikReSentence> ExecuteListWithDuration(int durationSec, out bool wasAborted, out string abortReason)
        {
            throw new NotSupportedException("REST transport does not support streaming commands.");
        }

        public IEnumerable<ITikReSentence> ExecuteListUntilDone(int? timeoutSec = null)
        {
            // ExecuteListUntilDone is used for commands that self-terminate (e.g. ping count=N).
            // For REST we can attempt a single synchronous call — if the router returns immediately, this works.
            // For commands that would hang forever on a non-REST API, we just do a regular list call.
            return ExecuteList();
        }

        public void ExecuteAsync(Action<ITikReSentence> oneResponseCallback,
            Action<ITikTrapSentence> errorCallback = null,
            Action onDoneCallback = null)
        {
            throw new NotSupportedException("REST transport does not support asynchronous/listen commands. Use a transport that reports Listen capability.");
        }

        public void Cancel() { /* no-op for REST */ }

        public void CancelAndJoin() { /* no-op for REST */ }

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
            return new RestCommandParameter(name, value, fmt);
        }

        // ── Verb detection helper ─────────────────────────────────────────────

        private static string GetVerb(string commandText)
        {
            if (string.IsNullOrWhiteSpace(commandText))
                return "print";
            var segments = commandText.TrimStart('/').Split('/');
            return segments.Last().ToLowerInvariant();
        }

        public override string ToString()
            => CommandText + " PARAMS: " + string.Join("; ", _parameters.Select(p => $"{p.Name}:{p.Value}"));
    }
}
