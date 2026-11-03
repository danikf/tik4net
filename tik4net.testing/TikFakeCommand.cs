using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Api;
using tik4net.Connection;

namespace tik4net.Testing
{
    /// <summary>
    /// Fake <see cref="ITikCommand"/> returned by <see cref="TikFakeConnection.CreateCommand()"/>. It sends what the binary
    /// API's own command sends and reads the scripted answer by the same rules, so a test against it sees the rows, the
    /// values and the exceptions a router-facing command produces.
    /// </summary>
    /// <remarks>
    /// <para>The rows follow the binary API: a parameter without a format of its own takes the command's default, then the
    /// method's — a filter (<c>?name=value</c>) for a read, a value (<c>=name=value</c>) for a write; <c>.tag</c> is sent as
    /// a tag; the client-side markers (<c>.cli-flags</c>, …) are not sent; a path gets its leading <c>/</c>.
    /// <c>ExecuteList(fields)</c> and <c>ExecuteScalar(target)</c> add <c>=.proplist=</c>.</para>
    /// <para>The answers follow the shared rules every transport uses: an empty answer is <see cref="TikNoSuchItemException"/>
    /// after a read, <see cref="TikCommandEmptyResponseException"/> after any other verb, and
    /// <see cref="TikAddIdNotReadException"/> after an <c>add</c>; several rows where one is expected are
    /// <see cref="TikCommandUnexpectedResponseException"/> or <see cref="TikCommandAmbiguousResultException"/>.
    /// <c>tik4net.unittests</c>' <c>FakeCommandParityTests</c> holds both against the binary API's command.</para>
    /// <para>One run at a time, as on every transport: a second execute, or a change of the command text or connection,
    /// while it runs is refused with <see cref="InvalidOperationException"/>.</para>
    /// <para>The fake has no wire and nothing to wait for: a bounded read whose script never ends in <c>!done</c> returns
    /// its rows at once, as if its duration had passed.</para>
    /// </remarks>
    public sealed class TikFakeCommand : ITikCommand, ITikCommandAsync
#if NET8_0_OR_GREATER
        , ITikStreamingCommand
#endif
    {
        private readonly TikFakeConnection _fakeConnection;
        private readonly List<ITikCommandParameter> _parameters = new List<ITikCommandParameter>();
        private ITikConnection _connection;
        private string _commandText = null!;   // null until set; EnsureCommandTextSet guards the execute paths
        private int _running;                  // 1 while a run holds the command
        private volatile string? _asyncTag;    // set while ExecuteWithCallback runs
        private volatile Thread? _asyncThread;

        internal TikFakeCommand(TikFakeConnection connection)
        {
            _fakeConnection = connection;
            _connection = connection;
        }

        /// <inheritdoc/>
        /// <exception cref="ArgumentException">The connection is not this command's <see cref="TikFakeConnection"/>.</exception>
        public ITikConnection Connection
        {
            get => _connection;
            set
            {
                EnsureNotRunning();
                Guard.ArgumentNotNull(value, nameof(value));
                if (!ReferenceEquals(value, _fakeConnection))
                    throw new ArgumentException("A fake command runs on the TikFakeConnection that created it.", nameof(value));
                _connection = value;
            }
        }

        /// <inheritdoc/>
        public string CommandText
        {
            get => _commandText;
            set
            {
                EnsureNotRunning();
                _commandText = value;
            }
        }

        /// <inheritdoc/>
        public bool IsRunning => Volatile.Read(ref _running) != 0;

        /// <inheritdoc/>
        public IList<ITikCommandParameter> Parameters => _parameters;

        /// <inheritdoc/>
        public TikCommandParameterFormat DefaultParameterFormat { get; set; } = TikCommandParameterFormat.Default;

        // ── Guards ────────────────────────────────────────────────────────────

        private void EnsureNotRunning()
        {
            if (IsRunning)
                throw new InvalidOperationException("Command is already running.");
        }

        private void BeginRun()
        {
            if (string.IsNullOrWhiteSpace(_commandText))
                throw new InvalidOperationException("CommandText is not set.");
            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
                throw new InvalidOperationException("Command is already running.");
        }

        private void EndRun() => Volatile.Write(ref _running, 0);

        // ── The rows ──────────────────────────────────────────────────────────

        // ApiCommand.ConstructCommandText, without the filter check against the router's grammar (the fake has none).
        private string[] Rows(TikCommandParameterFormat useCaseDefault, params ITikCommandParameter[] additional)
        {
            foreach (var p in additional)
            {
                if (_parameters.Any(q => q.Name == p.Name))
                    throw new ArgumentException($"Parameter {p.Name} already defined (could not be additionalParameter / proplist / etc.).");
            }

            string text = CommandText;
            if (!text.Contains("\n") && !text.StartsWith("/", StringComparison.Ordinal))
                text = "/" + text;
            var rows = text.Contains("\n")
                ? text.Split('\n').Select(row => row.Trim()).ToList()
                : new List<string> { text };

            foreach (var p in _parameters.Concat(additional).Where(p => !IsUnsentMarker(p.Name)))
            {
                if (p.Name.StartsWith("=", StringComparison.Ordinal) || p.Name.StartsWith("?", StringComparison.Ordinal))
                {
                    rows.Add(p.Name + "=" + p.Value);
                    continue;
                }
                var format = TikParameterFormat.Resolve(p, DefaultParameterFormat, useCaseDefault);
                switch (format)
                {
                    case TikCommandParameterFormat.Filter: rows.Add("?" + p.Name + "=" + p.Value); break;
                    case TikCommandParameterFormat.NameValue: rows.Add("=" + p.Name + "=" + p.Value); break;
                    case TikCommandParameterFormat.Tag: rows.Add(p.Name + "=" + p.Value); break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(useCaseDefault), format, "Unknown " + nameof(TikCommandParameterFormat) + ".");
                }
            }
            return rows.ToArray();
        }

        // The client-side markers stay off the wire, as on the binary API — except the WinBox labels on a fake that declares
        // FieldLabels: it then stands in for WinBox native, the transport that reads them.
        private bool IsUnsentMarker(string name)
            => TikTransportMarkers.IsClientMarker(name)
               && !(name == TikSpecialProperties.WinboxLabels && _fakeConnection.Supports(TikConnectionCapability.FieldLabels));

        private ITikCommandParameter? FieldList(string[]? fields)
        {
            if (fields == null || fields.Length == 0)
                return null;
            if (_parameters.Any(p => p.Name == TikSpecialProperties.Proplist))
                throw new ArgumentException("The command already has a .proplist parameter; name the fields either "
                    + "there or in ExecuteList(…), not both.", nameof(fields));
            return new TikCommandParameter(TikSpecialProperties.Proplist, string.Join(",", fields), TikCommandParameterFormat.NameValue);
        }

        private static ITikCommandParameter[] Only(ITikCommandParameter? p)
            => p == null ? new ITikCommandParameter[0] : new[] { p };

        private T Run<T>(TikCommandParameterFormat useCaseDefault, ITikCommandParameter[] additional, Func<IList<ITikSentence>, T> interpret)
        {
            BeginRun();
            try
            {
                return interpret(_fakeConnection.Dispatch(Rows(useCaseDefault, additional)));
            }
            finally
            {
                EndRun();
            }
        }

        private string Verb => TikPath.Verb(CommandText);

        // ── Reading the answer: ApiCommand's rules over the fake's sentences ──

        private void ThrowOnError(IEnumerable<ITikSentence> sentences)
        {
            foreach (var sentence in sentences)
            {
                if (sentence is ITikTrapSentence trap)
                    throw TikTrapClassifier.ToException(TikTrapClassifier.Classify(trap.Message), this, trap);
                if (sentence is TikFakeFatalSentence fatal)
                    throw new TikCommandFatalException(this, fatal.Message);
            }
        }

        private void EnsureDone(ITikSentence sentence)
        {
            if (!(sentence is ITikDoneSentence))
                throw new TikCommandUnexpectedResponseException("!done sentence expected as result.", this, sentence);
        }

        private void EnsureOneRowAndDone(IList<ITikSentence> answer)
        {
            if (answer.Count != 2)
            {
                if (answer.Count == 1 && answer[0] is ITikDoneSentence)
                    throw new TikNoSuchItemException(this);
                throw new TikCommandUnexpectedResponseException(
                    $"Command expected 1x !re and 1x !done sentences as response, but got {answer.Count} response sentences.", this, answer);
            }
            if (!(answer[0] is ITikReSentence))
                throw new TikCommandUnexpectedResponseException("!re sentence expected as result.", this, answer[0]);
            EnsureDone(answer[1]);
        }

        private object? NonQuery(IList<ITikSentence> answer)
        {
            // !fatal means the router closed the session after running the command (/system/reboot): success.
            if (answer.Any(s => s is TikFakeFatalSentence))
                return null;
            ThrowOnError(answer);
            if (answer.Count == 0)
                throw new TikCommandUnexpectedResponseException("!done sentence expected as result.", this, answer);
            EnsureDone(answer[answer.Count - 1]);
            return null;
        }

        private string? Scalar(IList<ITikSentence> answer, string? target, bool allowDefault, string? defaultValue)
        {
            ThrowOnError(answer);
            if (answer.Count == 1)
            {
                EnsureDone(answer[0]);
                var ret = ((ITikDoneSentence)answer[0]).GetResponseWordOrDefault(null);
                if (ret != null)
                    return ret;
                return TikCommandResult.EmptyScalar(this, Verb, allowDefault, defaultValue, "answered !done without a =ret= value");
            }
            if (answer.Count == 2)
            {
                EnsureOneRowAndDone(answer);
                var row = (ITikReSentence)answer[0];
                return target != null
                    ? TikCommandResult.FieldOfRow(row, target, allowDefault, defaultValue)
                    : TikCommandResult.ScalarOfRow(this, row, Verb, allowDefault, defaultValue);
            }
            throw new TikCommandUnexpectedResponseException(
                "Single !done response or exactly one !re sentences expected. (1x!done or 1x!re + 1x!done )", this, answer);
        }

        private string? RunScalar(string? target, bool allowDefault, string? defaultValue)
        {
            var targetParameter = target != null
                ? new TikCommandParameter(TikSpecialProperties.Proplist, target, TikCommandParameterFormat.NameValue)
                : null;
            return Run(ApiCommand.ScalarParameterFormat(CommandText ?? string.Empty, _parameters), Only(targetParameter),
                answer => Scalar(answer, target, allowDefault, defaultValue));
        }

        private ITikReSentence SingleRow(IList<ITikSentence> answer)
        {
            ThrowOnError(answer);
            var rows = answer.OfType<ITikReSentence>().ToList();
            if (rows.Count <= 1)
                EnsureOneRowAndDone(answer);
            return TikCommandResult.SingleRow(this, rows, throwIfMissing: true)!;
        }

        private IList<ITikReSentence> List(IList<ITikSentence> answer)
        {
            ThrowOnError(answer);
            if (answer.Count == 0)
                throw new TikCommandUnexpectedResponseException("!done sentence expected as result.", this, answer);
            for (int i = 0; i < answer.Count - 1; i++)
            {
                if (!(answer[i] is ITikReSentence))
                    throw new TikCommandUnexpectedResponseException("!re sentence expected as result.", this, answer[i]);
            }
            EnsureDone(answer[answer.Count - 1]);
            var rows = answer.Take(answer.Count - 1).Cast<ITikReSentence>().ToList();
            // get answers in the !done's =ret= word, as ApiCommand reads it.
            if (rows.Count == 0 && Verb == "get")
                return TikGetResult.FromRet(_parameters, ((ITikDoneSentence)answer[answer.Count - 1]).GetResponseWordOrDefault(null))
                    .Cast<ITikReSentence>().ToList();
            return rows;
        }

        // ── ITikCommand ───────────────────────────────────────────────────────

        /// <inheritdoc/>
        public void ExecuteNonQuery() => Run(TikCommandParameterFormat.NameValue, new ITikCommandParameter[0], NonQuery);

        /// <inheritdoc/>
        public string ExecuteScalar() => RunScalar(null, false, null)!;

        /// <inheritdoc/>
        public string ExecuteScalar(string target) => RunScalar(target, false, null)!;

        /// <inheritdoc/>
        public string? ExecuteScalarOrDefault() => RunScalar(null, true, null);

        /// <inheritdoc/>
        public string? ExecuteScalarOrDefault(string? defaultValue) => RunScalar(null, true, defaultValue);

        /// <inheritdoc/>
        public string? ExecuteScalarOrDefault(string? defaultValue, string target) => RunScalar(target, true, defaultValue);

        /// <inheritdoc/>
        public ITikReSentence ExecuteSingleRow()
            => Run(TikCommandParameterFormat.Filter, new ITikCommandParameter[0], SingleRow);

        /// <inheritdoc/>
        public ITikReSentence? ExecuteSingleRowOrDefault()
            => TikCommandResult.SingleRow(this, ExecuteList().ToList(), throwIfMissing: false);

        /// <inheritdoc/>
        public IEnumerable<ITikReSentence> ExecuteList()
            => Run(TikGetResult.ListParameterFormat(CommandText), new ITikCommandParameter[0], List);

        /// <inheritdoc/>
        public IEnumerable<ITikReSentence> ExecuteList(params string[] proplistFields)
        {
            Guard.ArgumentNotNull(proplistFields, nameof(proplistFields));
            return Run(TikGetResult.ListParameterFormat(CommandText), Only(FieldList(proplistFields)), List);
        }

        // ── Bounded reads ─────────────────────────────────────────────────────
        //
        // The binary API runs these through its callback engine, so the parameters go as values, the 'interrupted' trap of
        // a cancel is not an error, and the read ends on the first trap, !fatal or !done.

        private enum Ending { Done, Trap, ConnectionLost, StillRunning }

        private sealed class BoundedAnswer
        {
            public readonly List<ITikReSentence> Rows = new List<ITikReSentence>();
            public Ending Ending = Ending.StillRunning;
            public ITikTrapSentence? Trap;
            public TikFakeFatalSentence? Fatal;
        }

        private static bool IsCancelTrap(ITikTrapSentence trap) => trap.CategoryCode == "2" && trap.Message == "interrupted";

        private static BoundedAnswer Bounded(IEnumerable<ITikSentence> answer)
        {
            var result = new BoundedAnswer();
            foreach (var sentence in answer)
            {
                switch (sentence)
                {
                    case ITikReSentence row:
                        result.Rows.Add(row);
                        continue;
                    case ITikTrapSentence trap when !IsCancelTrap(trap):
                        result.Trap = trap;
                        result.Ending = Ending.Trap;
                        return result;
                    case TikFakeFatalSentence fatal:
                        result.Fatal = fatal;
                        result.Ending = Ending.ConnectionLost;
                        return result;
                    case ITikDoneSentence _:
                        result.Ending = Ending.Done;
                        return result;
                }
            }
            return result;
        }

        private BoundedAnswer RunBounded()
            => Run(TikCommandParameterFormat.NameValue, new ITikCommandParameter[0], Bounded);

        private TikCommandTrapException TrapException(ITikTrapSentence trap)
            => TikTrapClassifier.ToException(TikTrapClassifier.Classify(trap.Message), this, trap);

        private TikCommandFatalException FatalException(TikFakeFatalSentence fatal)
            => new TikCommandFatalException(this, string.IsNullOrEmpty(fatal.Message)
                ? "Connection has been closed."
                : "Connection has been closed: " + fatal.Message);

        private static string FatalReason(TikFakeFatalSentence fatal)
            => string.IsNullOrEmpty(fatal.Message) ? "Connection has been closed" : fatal.Message;

        /// <inheritdoc/>
        public IEnumerable<ITikReSentence> ExecuteListWithDuration(TimeSpan duration)
        {
            var read = RunBounded();
            switch (read.Ending)
            {
                case Ending.Trap:
                    throw new TikCommandAbortException(this, read.Trap!.Message, TrapException(read.Trap));
                case Ending.ConnectionLost:
                    throw new TikCommandAbortException(this, FatalReason(read.Fatal!), FatalException(read.Fatal!));
                default:
                    return read.Rows;
            }
        }

        /// <inheritdoc/>
        public IEnumerable<ITikReSentence> ExecuteListWithDuration(TimeSpan duration, out bool wasAborted, out string? abortReason)
        {
            var read = RunBounded();
            wasAborted = read.Ending == Ending.Trap || read.Ending == Ending.ConnectionLost;
            abortReason = read.Ending == Ending.Trap ? read.Trap!.Message
                : read.Ending == Ending.ConnectionLost ? FatalReason(read.Fatal!)
                : null;   // meaningful only when wasAborted is true, per the interface doc
            return read.Rows;
        }

        /// <inheritdoc/>
        /// <exception cref="InvalidOperationException">The script never ends in <c>!done</c> and no
        /// <paramref name="timeout"/> was given: on a router the read would never return.</exception>
        public IEnumerable<ITikReSentence> ExecuteListUntilDone(TimeSpan? timeout = null)
        {
            var read = RunBounded();
            switch (read.Ending)
            {
                case Ending.Trap:
                    throw TrapException(read.Trap!);
                case Ending.ConnectionLost:
                    throw FatalException(read.Fatal!);
                case Ending.StillRunning:
                    throw NeverEnds(timeout);
                default:
                    return read.Rows;
            }
        }

        private Exception NeverEnds(TimeSpan? timeout)
            => timeout.HasValue
                ? new TikCommandAbortException(this, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "Command did not finish within {0} second(s).", timeout.Value.TotalSeconds))
                : (Exception)new InvalidOperationException("The scripted answer never ends in !done, so on a router this "
                    + "read would never return. Give it a timeout, or end the script with !done.");

        // ── ITikCommandAsync ──────────────────────────────────────────────────
        //
        // The fake has no wire, so every one of these completes synchronously — awaiting them never yields, and a test can
        // assert on the result immediately after. The token is honoured before dispatch, the one point that has a meaning
        // here; with nothing in flight to cancel, TikFakeConnection declares AsyncCommands but not CancelInFlight.

        /// <inheritdoc/>
        public Task ExecuteNonQueryAsync(CancellationToken cancellationToken = default(CancellationToken))
            => Completed(() => { ExecuteNonQuery(); return (object?)null; }, cancellationToken);

        /// <inheritdoc/>
        public Task<string> ExecuteScalarAsync(CancellationToken cancellationToken = default(CancellationToken))
            => Completed(ExecuteScalar, cancellationToken);

        /// <inheritdoc/>
        public Task<string> ExecuteScalarAsync(string target, CancellationToken cancellationToken = default(CancellationToken))
            => Completed(() => ExecuteScalar(target), cancellationToken);

        /// <inheritdoc/>
        public Task<string?> ExecuteScalarOrDefaultAsync(string? defaultValue = null, string? target = null,
            CancellationToken cancellationToken = default(CancellationToken))
            => Completed(() => target == null ? ExecuteScalarOrDefault(defaultValue) : ExecuteScalarOrDefault(defaultValue, target), cancellationToken);

        /// <inheritdoc/>
        public Task<ITikReSentence> ExecuteSingleRowAsync(CancellationToken cancellationToken = default(CancellationToken))
            => Completed(ExecuteSingleRow, cancellationToken);

        /// <inheritdoc/>
        public Task<ITikReSentence?> ExecuteSingleRowOrDefaultAsync(CancellationToken cancellationToken = default(CancellationToken))
            => Completed(ExecuteSingleRowOrDefault, cancellationToken);

        /// <inheritdoc/>
        public Task<IList<ITikReSentence>> ExecuteListAsync(CancellationToken cancellationToken = default(CancellationToken))
            => Completed(() => (IList<ITikReSentence>)ExecuteList().ToList(), cancellationToken);

        /// <inheritdoc/>
        public Task<IList<ITikReSentence>> ExecuteListAsync(string[] proplistFields, CancellationToken cancellationToken = default(CancellationToken))
            => Completed(() => (IList<ITikReSentence>)ExecuteList(proplistFields).ToList(), cancellationToken);

        /// <summary>
        /// Runs the synchronous body and reports its outcome through an already-completed <see cref="Task{T}"/> —
        /// exceptions included, so <c>await</c> throws exactly what the synchronous call would.
        /// </summary>
        private static Task<T> Completed<T>(Func<T> body, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<T>();
            if (cancellationToken.IsCancellationRequested)
            {
                tcs.SetCanceled();
                return tcs.Task;
            }
            try
            {
                tcs.SetResult(body());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
            return tcs.Task;
        }

#if NET8_0_OR_GREATER
        // ── ITikStreamingCommand ──────────────────────────────────────────────
        //
        // Reached only when a test adds TikConnectionCapability.Streaming to the fake's flags. The rows come first, then
        // the ending, as on the binary API: a trap or a lost connection ends the stream with TikCommandAbortException.

        /// <inheritdoc/>
        public IAsyncEnumerable<ITikReSentence> ExecuteListWithDurationAsync(TimeSpan duration,
            CancellationToken cancellationToken = default)
            => Stream(null, cancellationToken);

        /// <inheritdoc/>
        public IAsyncEnumerable<ITikReSentence> ExecuteListUntilDoneAsync(TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
            => Stream(timeout ?? Timeout.InfiniteTimeSpan, cancellationToken);

        // untilDone: null for a duration read (no !done is a normal end); the timeout of an until-done read otherwise.
        private async IAsyncEnumerable<ITikReSentence> Stream(TimeSpan? untilDone,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = RunBounded();
            foreach (var row in read.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return row;
            }
            await Task.CompletedTask.ConfigureAwait(false);
            switch (read.Ending)
            {
                case Ending.Trap:
                    throw new TikCommandAbortException(this, read.Trap!.Message, TrapException(read.Trap));
                case Ending.ConnectionLost:
                    throw new TikCommandAbortException(this, FatalReason(read.Fatal!), FatalException(read.Fatal!));
                case Ending.StillRunning when untilDone.HasValue:
                    throw NeverEnds(untilDone == Timeout.InfiniteTimeSpan ? (TimeSpan?)null : untilDone);
            }
        }
#endif

        // ── Callbacks ─────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public void ExecuteWithCallback(
            Action<ITikReSentence> oneResponseCallback,
            Action<ITikTrapSentence>? errorCallback = null,
            Action? onDoneCallback = null)
        {
            BeginRun();
            try
            {
                string tag = Guid.NewGuid().ToString();
                var ending = new TikCallbackEnding();   // onDone only when no error was reported (command-layer D21)
                _asyncTag = tag;
                _asyncThread = _fakeConnection.CallCommandAsync(Rows(TikCommandParameterFormat.NameValue), tag, sentence =>
                {
                    switch (sentence)
                    {
                        case ITikReSentence row:
                            oneResponseCallback(row);
                            break;
                        case ITikTrapSentence trap:
                            if (!IsCancelTrap(trap))   // the cancel's own trap is not an error
                                ending.Error(trap, errorCallback);
                            break;
                        case TikFakeFatalSentence fatal:
                            Ended();
                            ending.Error(new TikTrapSentenceResult(fatal.Message), errorCallback);
                            break;
                        case ITikDoneSentence _:
                            Ended();
                            ending.Done(onDoneCallback);
                            break;
                    }
                });
            }
            catch
            {
                Ended();
                throw;
            }
        }

        private void Ended()
        {
            _asyncTag = null;
            _asyncThread = null;
            EndRun();
        }

        /// <inheritdoc/>
        public ITikCommandParameter AddParameter(string name, string? value)
            => AddParameter(name, value, TikCommandParameterFormat.Default);

        /// <inheritdoc/>
        public ITikCommandParameter AddParameter(string name, string? value, TikCommandParameterFormat parameterFormat)
        {
            var p = _fakeConnection.CreateParameter(name, value, parameterFormat);
            _parameters.Add(p);
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
            Guard.NamesAndValuesArePaired(parameterNamesAndValues, nameof(parameterNamesAndValues));
            var added = new List<ITikCommandParameter>(parameterNamesAndValues.Length / 2);
            for (int i = 0; i + 1 < parameterNamesAndValues.Length; i += 2)
                added.Add(_fakeConnection.CreateParameter(parameterNamesAndValues[i], parameterNamesAndValues[i + 1]));
            _parameters.AddRange(added);
            return added;
        }

        /// <inheritdoc/>
        public void Cancel()
        {
            var tag = _asyncTag;
            if (tag != null)
                _fakeConnection.CancelTag(tag);
        }

        /// <inheritdoc/>
        public void CancelAndJoin()
        {
            var thread = _asyncThread;
            Cancel();
            thread?.Join();
        }

        /// <inheritdoc/>
        public bool CancelAndJoin(TimeSpan timeout)
        {
            // The same contract as the real commands: a positive budget, or Timeout.InfiniteTimeSpan.
            if (timeout <= TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(timeout), timeout,
                    "A wait budget is positive, or Timeout.InfiniteTimeSpan to wait without a bound.");
            var thread = _asyncThread;
            Cancel();
            return thread?.Join(timeout) ?? true;
        }

        /// <inheritdoc/>
        public override string ToString()
            => CommandText + " [" + string.Join(", ", _parameters.Select(p => $"{p.Name}={p.Value}")) + "]";
    }
}
