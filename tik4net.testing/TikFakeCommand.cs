using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace tik4net.Testing
{
    /// <summary>
    /// Fake <see cref="ITikCommand"/> returned by <see cref="TikFakeConnection.CreateCommand()"/>.
    /// Delegates all Execute* calls back to <see cref="TikFakeConnection.CallCommandSync(System.Collections.Generic.IEnumerable{string})"/> so that
    /// registered handlers are invoked and the real sentence-processing logic runs.
    /// </summary>
    public sealed class TikFakeCommand : ITikCommand
    {
        private readonly TikFakeConnection _fakeConnection;
        private Thread _asyncThread;
        private string _asyncTag;

        /// <inheritdoc/>
        public ITikConnection Connection { get; set; }

        /// <inheritdoc/>
        public string CommandText { get; set; }

        /// <inheritdoc/>
        public bool IsRunning => _asyncThread != null && _asyncThread.IsAlive;

        /// <inheritdoc/>
        public IList<ITikCommandParameter> Parameters { get; } = new List<ITikCommandParameter>();

        /// <inheritdoc/>
        public TikCommandParameterFormat DefaultParameterFormat { get; set; } = TikCommandParameterFormat.NameValue;

        internal TikFakeCommand(TikFakeConnection connection)
        {
            _fakeConnection = connection;
            Connection = connection;
        }

        private IEnumerable<string> BuildCommandRows()
        {
            var rows = new List<string> { CommandText };
            foreach (var p in Parameters)
            {
                var fmt = p.ParameterFormat == TikCommandParameterFormat.Default
                    ? DefaultParameterFormat
                    : p.ParameterFormat;

                // Already prefixed with ? or = → keep as-is with value appended
                if (p.Name.Length > 0 && (p.Name[0] == '?' || p.Name[0] == '='))
                    rows.Add(p.Name + "=" + p.Value);
                else if (fmt == TikCommandParameterFormat.Filter)
                    rows.Add("?" + p.Name + "=" + p.Value);
                else
                    rows.Add("=" + p.Name + "=" + p.Value);
            }
            return rows;
        }

        private IList<ITikSentence> ExecuteSync()
            => _fakeConnection.CallCommandSync(BuildCommandRows()).ToList();

        private void ThrowOnTrap(IEnumerable<ITikSentence> sentences)
        {
            var trap = sentences.OfType<ITikTrapSentence>().FirstOrDefault();
            if (trap != null)
                throw new TikCommandTrapException(this, trap);
        }

        /// <inheritdoc/>
        public void ExecuteNonQuery()
        {
            ThrowOnTrap(ExecuteSync());
        }

        /// <inheritdoc/>
        public string ExecuteScalar()
        {
            var sentences = ExecuteSync();
            ThrowOnTrap(sentences);

            // Primary path: =ret= in !done (e.g. /add responses)
            var done = sentences.OfType<ITikDoneSentence>().FirstOrDefault();
            if (done != null)
            {
                string ret = done.GetResponseWordOrDefault(null);
                if (ret != null)
                    return ret;
            }

            // Secondary path: single field in single !re (e.g. /system/identity/print)
            var reList = sentences.OfType<ITikReSentence>().ToList();
            if (reList.Count == 1 && reList[0].Words.Count == 1)
                return reList[0].Words.Values.First();

            throw new TikCommandUnexpectedResponseException(
                "ExecuteScalar: response contains neither =ret= nor a single-field !re sentence.", this, sentences);
        }

        /// <inheritdoc/>
        public string ExecuteScalar(string target)
        {
            var sentences = ExecuteSync();
            ThrowOnTrap(sentences);
            return sentences.OfType<ITikReSentence>().Single().GetResponseField(target);
        }

        /// <inheritdoc/>
        public string ExecuteScalarOrDefault() => ExecuteScalarOrDefault((string)null);

        /// <inheritdoc/>
        public string ExecuteScalarOrDefault(string defaultValue)
        {
            var sentences = ExecuteSync();
            ThrowOnTrap(sentences);

            var done = sentences.OfType<ITikDoneSentence>().FirstOrDefault();
            if (done != null)
            {
                string ret = done.GetResponseWordOrDefault(null);
                if (ret != null)
                    return ret;
            }

            var reList = sentences.OfType<ITikReSentence>().ToList();
            if (reList.Count == 1 && reList[0].Words.Count == 1)
                return reList[0].Words.Values.First();

            return defaultValue;
        }

        /// <inheritdoc/>
        public string ExecuteScalarOrDefault(string defaultValue, string target)
        {
            var sentences = ExecuteSync();
            ThrowOnTrap(sentences);
            return sentences.OfType<ITikReSentence>().SingleOrDefault()
                ?.GetResponseFieldOrDefault(target, defaultValue)
                ?? defaultValue;
        }

        /// <inheritdoc/>
        public ITikReSentence ExecuteSingleRow()
        {
            var sentences = ExecuteSync();
            ThrowOnTrap(sentences);
            var rows = sentences.OfType<ITikReSentence>().ToList();
            if (rows.Count == 0)
                throw new TikCommandUnexpectedResponseException("ExecuteSingleRow: no !re sentence in response.", this, sentences);
            if (rows.Count > 1)
                throw new TikCommandAmbiguousResultException(this, rows.Count);
            return rows[0];
        }

        /// <inheritdoc/>
        public ITikReSentence ExecuteSingleRowOrDefault()
        {
            var sentences = ExecuteSync();
            ThrowOnTrap(sentences);
            var rows = sentences.OfType<ITikReSentence>().ToList();
            if (rows.Count > 1)
                throw new TikCommandAmbiguousResultException(this, rows.Count);
            return rows.SingleOrDefault();
        }

        /// <inheritdoc/>
        public IEnumerable<ITikReSentence> ExecuteList()
            => ExecuteListCore();

        /// <inheritdoc/>
        public IEnumerable<ITikReSentence> ExecuteList(params string[] proplistFields)
            => ExecuteListCore();   // proplist filtering is a router-side concern; fake ignores it

        private IEnumerable<ITikReSentence> ExecuteListCore()
        {
            var sentences = ExecuteSync();
            ThrowOnTrap(sentences);
            return sentences.OfType<ITikReSentence>().ToList();
        }

        /// <inheritdoc/>
        public IEnumerable<ITikReSentence> ExecuteListWithDuration(int durationSec)
        {
            // No real duration in fake — return all registered sentences immediately
            return ExecuteListCore();
        }

        /// <inheritdoc/>
        public IEnumerable<ITikReSentence> ExecuteListWithDuration(int durationSec, out bool wasAborted, out string abortReason)
        {
            wasAborted = false;
            abortReason = null;
            return ExecuteListWithDuration(durationSec);
        }

        /// <inheritdoc/>
        public IEnumerable<ITikReSentence> ExecuteListUntilDone(int? timeoutSec = null)
            => ExecuteListCore();

        /// <inheritdoc/>
        public void ExecuteAsync(
            Action<ITikReSentence> oneResponseCallback,
            Action<ITikTrapSentence> errorCallback = null,
            Action onDoneCallback = null)
        {
            _asyncTag = Guid.NewGuid().ToString();
            _asyncThread = _fakeConnection.CallCommandAsync(BuildCommandRows(), _asyncTag, sentence =>
            {
                if (sentence is ITikReSentence re)
                    oneResponseCallback(re);
                else if (sentence is ITikTrapSentence trap)
                    errorCallback?.Invoke(trap);
                else if (sentence is ITikDoneSentence)
                    onDoneCallback?.Invoke();
            });
        }

        /// <inheritdoc/>
        public ITikCommandParameter AddParameter(string name, string value)
        {
            var p = _fakeConnection.CreateParameter(name, value, DefaultParameterFormat);
            Parameters.Add(p);
            return p;
        }

        /// <inheritdoc/>
        public ITikCommandParameter AddParameter(string name, string value, TikCommandParameterFormat parameterFormat)
        {
            var p = _fakeConnection.CreateParameter(name, value, parameterFormat);
            Parameters.Add(p);
            return p;
        }

        /// <inheritdoc/>
        public ITikCommand WithParameter(string name, string value)
        {
            AddParameter(name, value);
            return this;
        }

        /// <inheritdoc/>
        public ITikCommand WithParameter(string name, string value, TikCommandParameterFormat parameterFormat)
        {
            AddParameter(name, value, parameterFormat);
            return this;
        }

        /// <inheritdoc/>
        public IEnumerable<ITikCommandParameter> AddParameterAndValues(params string[] parameterNamesAndValues)
        {
            var added = new List<ITikCommandParameter>();
            for (int i = 0; i + 1 < parameterNamesAndValues.Length; i += 2)
                added.Add(AddParameter(parameterNamesAndValues[i], parameterNamesAndValues[i + 1]));
            return added;
        }

        /// <inheritdoc/>
        public void Cancel()
        {
            if (_asyncTag != null)
                _fakeConnection.CancelTag(_asyncTag);
        }

        /// <inheritdoc/>
        public void CancelAndJoin()
        {
            Cancel();
            _asyncThread?.Join();
        }

        /// <inheritdoc/>
        public bool CancelAndJoin(int milisecondsTimeout)
        {
            Cancel();
            return _asyncThread?.Join(milisecondsTimeout) ?? true;
        }

        /// <inheritdoc/>
        public override string ToString()
            => CommandText + " [" + string.Join(", ", Parameters.Select(p => $"{p.Name}={p.Value}")) + "]";
    }
}
