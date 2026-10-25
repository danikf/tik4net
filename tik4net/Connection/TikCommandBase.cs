using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace tik4net.Connection
{
    /// <summary>
    /// What the two command classes share: the command's state (connection, command text, parameters, default
    /// format), the one-run-at-a-time guard, the parameter builders and <see cref="ToString"/>. How a command is sent
    /// and answered is each class's own: <c>ApiCommand</c> writes binary API sentences, <c>TikGenericCommand</c> hands
    /// a descriptor to its transport's hooks.
    /// </summary>
    internal abstract class TikCommandBase : ITikCommand
    {
        private readonly List<ITikCommandParameter> _parameters = new List<ITikCommandParameter>();
        private ITikConnection _connection = null!;   // set by the constructor
        private string _commandText = null!;          // null until set; EnsureCommandTextSet guards the execute paths
        private int _running;                         // 1 while a run holds the command: set and cleared only by BeginRun / EndRun

        /// <summary>
        /// The one constructor. <paramref name="commandText"/> may be null (a command whose text is set later), and
        /// <paramref name="parameters"/> are added as they are.
        /// </summary>
        private protected TikCommandBase(ITikConnection connection, string? commandText,
            TikCommandParameterFormat defaultParameterFormat, IEnumerable<ITikCommandParameter>? parameters)
        {
            DefaultParameterFormat = defaultParameterFormat;
            Connection = connection;   // AcceptConnection is a type check and touches no state of the derived class
            if (commandText != null)
                CommandText = commandText;
            if (parameters != null)
                _parameters.AddRange(parameters);
        }

        // ── State ─────────────────────────────────────────────────────────────

        public ITikConnection Connection
        {
            get { return _connection; }
            set
            {
                EnsureNotRunning();
                _connection = AcceptConnection(value);
            }
        }

        public string CommandText
        {
            get { return _commandText; }
            set
            {
                EnsureNotRunning();
                _commandText = value;
            }
        }

        public IList<ITikCommandParameter> Parameters => _parameters;

        public TikCommandParameterFormat DefaultParameterFormat { get; set; }

        public bool IsRunning => Volatile.Read(ref _running) != 0;

        /// <summary>The connection this class can run on, or the exception for one it cannot.</summary>
        private protected abstract ITikConnection AcceptConnection(ITikConnection value);

        /// <summary>A parameter of this class's own type.</summary>
        private protected abstract ITikCommandParameter CreateParameter(string name, string? value,
            TikCommandParameterFormat format);

        // ── Guards ────────────────────────────────────────────────────────────
        // One run at a time on every transport: a second execute, or a change of what the command is, while it runs is
        // refused with the same message. The check and the set are one atomic step, so two threads racing to execute
        // cannot both pass. Only the outermost method of a delegation begins a run, and the run that began it ends it
        // (for a callback command, the thread that delivers its end).

        private protected void EnsureConnectionSet()
        {
            if (_connection == null)
                throw new InvalidOperationException("Connection is not assigned.");
        }

        private protected void EnsureCommandTextSet()
        {
            if (string.IsNullOrWhiteSpace(_commandText))
                throw new InvalidOperationException("CommandText is not set.");
        }

        private protected void EnsureNotRunning()
        {
            if (IsRunning)
                throw new InvalidOperationException("Command is already running.");
        }

        private protected void BeginRun()
        {
            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
                throw new InvalidOperationException("Command is already running.");
        }

        private protected void EndRun() => Volatile.Write(ref _running, 0);

        // ── Parameters ────────────────────────────────────────────────────────

        public ITikCommandParameter AddParameter(string name, string? value)
            => AddParameter(name, value, TikCommandParameterFormat.Default);

        public ITikCommandParameter AddParameter(string name, string? value, TikCommandParameterFormat parameterFormat)
        {
            var parameter = CreateParameter(name, value, parameterFormat);
            _parameters.Add(parameter);
            return parameter;
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

        /// <summary>
        /// Adds <c>name, value, name, value, …</c>. An unpaired last name is refused, and so is any name the
        /// parameter type refuses, before anything is added.
        /// </summary>
        public IEnumerable<ITikCommandParameter> AddParameterAndValues(params string[] parameterNamesAndValues)
        {
            Guard.NamesAndValuesArePaired(parameterNamesAndValues, nameof(parameterNamesAndValues));
            var added = new List<ITikCommandParameter>(parameterNamesAndValues.Length / 2);
            for (int i = 0; i < parameterNamesAndValues.Length / 2; i++)   // name, value, name, value, ... sequence
                added.Add(CreateParameter(parameterNamesAndValues[i * 2], parameterNamesAndValues[i * 2 + 1],
                    TikCommandParameterFormat.Default));
            _parameters.AddRange(added);
            return added;
        }

        /// <summary>
        /// The <c>.proplist</c> parameter <c>ExecuteList(fields)</c> adds: one comma-joined field list, or none for no
        /// fields. A command that already has its own <c>.proplist</c> is refused before anything is sent: the fields
        /// are named in one place.
        /// </summary>
        /// <exception cref="ArgumentException">The command already has a <c>.proplist</c> parameter.</exception>
        private protected ITikCommandParameter? FieldListParameter(string[]? proplistFields)
        {
            if (proplistFields == null || proplistFields.Length == 0)
                return null;
            if (_parameters.Any(p => p.Name == TikSpecialProperties.Proplist))
                throw new ArgumentException("The command already has a .proplist parameter; name the fields either "
                    + "there or in ExecuteList(…), not both.", nameof(proplistFields));
            return CreateParameter(TikSpecialProperties.Proplist, string.Join(",", proplistFields),
                TikCommandParameterFormat.NameValue);
        }

        public override string ToString()
            => CommandText + " PARAMS: " + string.Join("; ", _parameters.Select(p => $"{p.Name}:{p.Value}"));

        // ── Execute: each class's own ─────────────────────────────────────────

        public abstract void ExecuteNonQuery();
        public abstract string ExecuteScalar();
        public abstract string ExecuteScalar(string target);
        public abstract string? ExecuteScalarOrDefault();
        public abstract string? ExecuteScalarOrDefault(string? defaultValue);
        public abstract string? ExecuteScalarOrDefault(string? defaultValue, string target);
        public abstract ITikReSentence ExecuteSingleRow();
        public abstract ITikReSentence? ExecuteSingleRowOrDefault();
        public abstract IEnumerable<ITikReSentence> ExecuteList();
        public abstract IEnumerable<ITikReSentence> ExecuteList(params string[] proplistFields);
        public abstract IEnumerable<ITikReSentence> ExecuteListWithDuration(TimeSpan duration);
        public abstract IEnumerable<ITikReSentence> ExecuteListWithDuration(TimeSpan duration, out bool wasAborted,
            out string? abortReason);
        public abstract IEnumerable<ITikReSentence> ExecuteListUntilDone(TimeSpan? timeout = null);
        public abstract void ExecuteWithCallback(Action<ITikReSentence> oneResponseCallback,
            Action<ITikTrapSentence>? errorCallback = null, Action? onDoneCallback = null);
        public abstract void Cancel();
        public abstract void CancelAndJoin();
        public abstract bool CancelAndJoin(TimeSpan timeout);
    }
}
