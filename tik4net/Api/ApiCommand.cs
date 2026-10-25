using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Connection;

namespace tik4net.Api
{
    internal class ApiCommand : TikCommandBase, ITikCommandAsync
#if NET8_0_OR_GREATER
        , ITikStreamingCommand
#endif
    {
        // Null when nothing is running; a tag is an opaque string, so its absence is null rather than a
        // sentinel value.
        private volatile string? _asynchronouslyRunningTag;
        private volatile Thread? _asyncLoadingThread; // only set while an ExecuteWithCallback-family command is running; null otherwise

        /// <summary>The connection, as the binary API connection the setter accepted.</summary>
        private ApiConnection Session => (ApiConnection)Connection;

        internal ApiCommand(ITikConnection connection, string? commandText, TikCommandParameterFormat defaultParameterFormat,
            params ITikCommandParameter[] parameters)
            : base(connection, commandText, defaultParameterFormat, parameters)
        {
        }

        private protected override ITikConnection AcceptConnection(ITikConnection value)
        {
            Guard.ArgumentNotNull(value, "value");
            Guard.ArgumentOfType<ApiConnection>(value, "value");
            return (ApiConnection)value;
        }

        // ApiCommandParameter refuses an empty name; TikCommandParameter does not.
        private protected override ITikCommandParameter CreateParameter(string name, string? value,
            TikCommandParameterFormat format)
            => new ApiCommandParameter(name, value, format);

        /// <summary>
        /// The parameters that never reach the binary API wire: the client-side markers
        /// (<see cref="TikTransportMarkers.IsClientMarker"/>) and nothing more. The API understands <c>.proplist</c> and
        /// <c>.tag</c> natively, so it sends both; it frames values and always sends the flags and the secrets, so it
        /// needs none of the CLI's markers.
        /// </summary>
        private static bool IsSpecialParam(string name) => TikTransportMarkers.IsClientMarker(name);

        private string[] ConstructCommandText(TikCommandParameterFormat defaultParameterFormat, params ITikCommandParameter[] additionalParameters)
        {
            EnsureCommandTextSet();
            if (Session != null)
                TikFilterValidation.Check(Session, this, CommandText,
                    Parameters.Where(p => p.Name.StartsWith("?")
                            || (!p.Name.StartsWith("=") && !IsSpecialParam(p.Name)
                                && TikParameterFormat.Resolve(p, DefaultParameterFormat, defaultParameterFormat) == TikCommandParameterFormat.Filter))
                        .Select(p => p.Name),
                    null);
            foreach (var additionalParameter in additionalParameters)
            {
                if (Parameters.Any(p => p.Name == additionalParameter.Name))
                    throw new ArgumentException($"Parameter {additionalParameter.Name} already defined (could not be additionalParameter / proplist / etc.).");
            }

            string commandText = CommandText;
            if (!string.IsNullOrWhiteSpace(commandText) && !commandText.Contains("\n") && !commandText.StartsWith("/"))
                commandText = "/" + commandText;

            List<string> result;
            if (commandText.Contains('\n'))
            {
                result = new List<string>(commandText.Split('\n').Select(row => row.Trim()));
            }
            else
            {
                result = new List<string> { commandText };
            }

            //parameters
            result.AddRange(Parameters.Concat(additionalParameters)
                .Where(p => !IsSpecialParam(p.Name))
                .Select(p =>
            {
                if (p.Name.StartsWith("=")) //NameValue format in parameter name
                    return string.Format("{0}={1}", p.Name, p.Value);
                else if (p.Name.StartsWith("?")) //Filter format in parameter name
                    return string.Format("{0}={1}", p.Name, p.Value);
                else
                {
                    switch (TikParameterFormat.Resolve(p, DefaultParameterFormat, defaultParameterFormat))
                    {
                        case TikCommandParameterFormat.Filter:
                            return string.Format("?{0}={1}", p.Name, p.Value);
                        case TikCommandParameterFormat.NameValue:
                            return string.Format("={0}={1}", p.Name, p.Value);
                        case TikCommandParameterFormat.Tag:
                            return string.Format("{0}={1}", p.Name, p.Value);
                        default:
                            // Not a tik4net exception on purpose: an undefined enum value here is a bug in
                            // the caller's argument, nothing the router said.
                            throw new ArgumentOutOfRangeException(nameof(defaultParameterFormat),
                                TikParameterFormat.Resolve(p, DefaultParameterFormat, defaultParameterFormat),
                                "Unknown " + nameof(TikCommandParameterFormat) + ".");
                    }
                }
            }));
            return result.ToArray();
        }

        private IEnumerable<ApiSentence> EnsureApiSentences(IEnumerable<ITikSentence> sentences)
        {
            if (sentences.Any(sentence => !(sentence is ApiSentence)))
                throw new InvalidOperationException("ApiCommand expects ApiSentence as result from ApiConnection.");

            return sentences.Cast<ApiSentence>();
        }

        private ApiSentence EnsureSingleResponse(IEnumerable<ApiSentence> response)
        {
            // Ignore progress sentences (e.g. .section=0, .section=1) sent by long-running commands
            // like /system/script/run — they contain only a .section word and precede the real !done.
            if (response.Count(x => !(x.Words.Count == 1 && x.Words.ContainsKey(".section"))) != 1)
                throw new TikCommandUnexpectedResponseException("Single response sentence expected.", this, response.Cast<ITikSentence>());

            return response.Last();
        }

        private void EnsureOneReAndDone(IEnumerable<ApiSentence> response)
        {
            if (response.Count() != 2)
            {
                if (response.Count() == 1 && response.Single() is ITikDoneSentence)
                    throw new TikNoSuchItemException(this);
                else
                    throw new TikCommandUnexpectedResponseException($"Command expected 1x !re and 1x !done sentences as response, but got {response.Count()} response sentences.", this, response.Cast<ITikSentence>());
            }
            EnsureReResponse(response.First());
            EnsureDoneResponse(response.Last());
        }

        private void ThrowPossibleResponseError(params ApiSentence[] responseSentences)
        {
            foreach (ApiSentence responseSentence in responseSentences)
            {
                ApiTrapSentence? trapSentence = responseSentence as ApiTrapSentence;
                if (trapSentence != null)
                { //detect well known error responses and convert them to special exceptions
                    throw TikTrapClassifier.ToException(TikTrapClassifier.Classify(trapSentence.Message), this, trapSentence);
                }
                ApiFatalSentence? fatalSentence = responseSentence as ApiFatalSentence;
                if (fatalSentence != null)
                    throw new TikCommandFatalException(this, fatalSentence.Message, fatalSentence.Cause);
            }
        }

        private ApiDoneSentence EnsureDoneResponse(ApiSentence responseSentence)
        {
            ApiDoneSentence? doneSentence = responseSentence as ApiDoneSentence;
            if (doneSentence == null)
                throw new TikCommandUnexpectedResponseException("!done sentence expected as result.", this, responseSentence);

            return doneSentence;
        }

        private void EnsureReResponse(params ApiSentence[] responseSentences)
        {
            foreach (ApiSentence responseSentence in responseSentences)
            {
                ApiReSentence? reSentence = responseSentence as ApiReSentence;
                if (reSentence == null)
                    throw new TikCommandUnexpectedResponseException("!re sentence expected as result.", this, responseSentence);
            }
        }


        public override void ExecuteNonQuery() => ExecuteNonQuery(Session?.ReceiveTimeoutMs ?? 0);

        /// <summary>
        /// <see cref="ExecuteNonQuery()"/> with an explicit reply deadline. Used by
        /// <see cref="CancelInternal"/> so a bounded <see cref="CancelAndJoin(TimeSpan)"/> cannot spend the
        /// connection's <c>ReceiveTimeout</c> before its own budget is even consulted.
        /// </summary>
        private void ExecuteNonQuery(int receiveTimeoutMs)
        {
            EnsureConnectionSet();
            BeginRun();
            try
            {
                string[] commandRows = ConstructCommandText(TikCommandParameterFormat.NameValue);
                InterpretNonQuery(EnsureApiSentences(Session.CallCommandSync(commandRows, receiveTimeoutMs)).ToArray());
            }
            finally
            {
                EndRun();
            }
        }

        public override string ExecuteScalar()
        {
            // allowReturnDefault:false - ExecuteScalarInternal either returns a real value or throws, never null.
            return ExecuteScalarInternal(null, false)!;
        }

        public override string ExecuteScalar(string target)
        {
            // allowReturnDefault:false - ExecuteScalarInternal either returns a real value or throws, never null.
            return ExecuteScalarInternal(target, false)!;
        }

        public override string? ExecuteScalarOrDefault()
        {
            // Returns null (or the caller's defaultValue, which may be null) when nothing matched —
            // the interface says so and now types it that way.
            return ExecuteScalarInternal(null, true, null);
        }

        public override string? ExecuteScalarOrDefault(string? defaultValue)
        {
            return ExecuteScalarInternal(null, true, defaultValue);
        }

        public override string? ExecuteScalarOrDefault(string? defaultValue, string target)
        {
            return ExecuteScalarInternal(target, true, defaultValue);
        }

        private string? ExecuteScalarInternal(string? target, bool allowReturnDefault, string? defaultValue = null)
        {
            EnsureConnectionSet();
            BeginRun();
            try
            {
                var targetParameterInArray = target != null ? new ITikCommandParameter[] { new ApiCommandParameter(TikSpecialProperties.Proplist, target, TikCommandParameterFormat.NameValue) } : new ITikCommandParameter[] { };
                string[] commandRows = ConstructCommandText(TikCommandParameterFormat.NameValue, targetParameterInArray);
                IEnumerable<ApiSentence> response = EnsureApiSentences(Session.CallCommandSync(commandRows));
                return InterpretScalar(response, allowReturnDefault, defaultValue);
            }
            finally
            {
                EndRun();
            }
        }

        // ── Response interpretation, shared by the sync and async surfaces ────
        //
        // Extracted rather than duplicated: which answer means "not found", which means "succeeded with
        // nothing to return", and which is a protocol violation must not be allowed to drift between the two
        // paths (the same rule the CLI and REST transports follow).

        private void InterpretNonQuery(ApiSentence[] responseArray)
        {
            // !fatal means the router closed the connection after executing the command
            // (e.g. /system/reboot, /system/shutdown, /system/poweroff). Treat as success.
            if (responseArray.Any(s => s is ApiFatalSentence))
                return;

            ThrowPossibleResponseError(responseArray);
            ApiSentence responseSentence = EnsureSingleResponse(responseArray);
            EnsureDoneResponse(responseSentence);
        }

        private string? InterpretScalar(IEnumerable<ApiSentence> response, bool allowReturnDefault, string? defaultValue)
        {
            {
                ThrowPossibleResponseError(response.ToArray());

                if (response.Count() == 1) //!done + =ret=result word
                {
                    ApiDoneSentence doneSentence = EnsureDoneResponse(response.Single());
                    if (doneSentence.Words.ContainsKey(TikSpecialProperties.Ret))
                        return doneSentence.GetResponseWord();
                    // A bare !done: "nothing matched" after a read, "succeeded with nothing to return" after
                    // anything else (how the API answers every write), and an add that named no row.
                    return TikCommandResult.EmptyScalar(this, tik4net.Connection.TikPath.Verb(CommandText),
                        allowReturnDefault, defaultValue, "answered !done without a =ret= value");
                }
                else if (response.Count() == 2) //!re + !done
                {
                    EnsureOneReAndDone(response);
                    ApiReSentence reResponse = (ApiReSentence)response.First();

                    return reResponse.Words.Single(v => v.Key != TikSpecialProperties.Tag).Value; //single word value from !re  //NOTE - .tag could be added when Connection.SendTagWithSyncCommand=true
                }
                else
                    throw new TikCommandUnexpectedResponseException("Single !done response or exactly one !re sentences expected. (1x!done or 1x!re + 1x!done )", this, response.Cast<ITikSentence>());
            }
        }

        private ITikReSentence InterpretSingleRow(IEnumerable<ApiSentence> response)
        {
            ThrowPossibleResponseError(response.ToArray());

            var rows = response.OfType<ApiReSentence>().ToList();
            if (rows.Count <= 1)
                EnsureOneReAndDone(response);   // the shape around the one row; a bare !done is no such item
            return TikCommandResult.SingleRow(this, rows, throwIfMissing: true)!;
        }

        private IList<ITikReSentence> InterpretList(IEnumerable<ApiSentence> response)
        {
            ThrowPossibleResponseError(response.ToArray());

            EnsureReResponse(response.Take(response.Count() - 1).ToArray());   //!re  - reapeating
            EnsureDoneResponse(response.Last()); //!done

            return response.Take(response.Count() - 1).Cast<ITikReSentence>().ToList();
        }

        // ── ITikCommandAsync ───────────────────────────────────
        //
        // Mirrors the synchronous methods rather than either side wrapping the other — the same rule
        // the CLI and REST transports follow. Everything after the call is the shared Interpret* helpers
        // above, so the two surfaces cannot disagree about what an answer means.
        //
        // These are reached through the Execute*Async extension methods on ITikCommand, which apply the
        // fail-closed AsyncCommands check first, so consumers never cast.

        Task ITikCommandAsync.ExecuteNonQueryAsync(CancellationToken cancellationToken)
            => RunAsync(TikCommandParameterFormat.NameValue, null,
                r => { InterpretNonQuery(r.ToArray()); return (object?)null; }, cancellationToken);

        async Task<string> ITikCommandAsync.ExecuteScalarAsync(CancellationToken cancellationToken)
            // never null: the helper throws when nothing matched (allowReturnDefault: false)
            => (await ExecuteScalarInternalAsync(null, allowReturnDefault: false, defaultValue: null, cancellationToken).ConfigureAwait(false))!;

        async Task<string> ITikCommandAsync.ExecuteScalarAsync(string target, CancellationToken cancellationToken)
            => (await ExecuteScalarInternalAsync(target, allowReturnDefault: false, defaultValue: null, cancellationToken).ConfigureAwait(false))!;

        Task<string?> ITikCommandAsync.ExecuteScalarOrDefaultAsync(string? defaultValue, string? target, CancellationToken cancellationToken)
            => ExecuteScalarInternalAsync(target, allowReturnDefault: true, defaultValue, cancellationToken);

        Task<ITikReSentence> ITikCommandAsync.ExecuteSingleRowAsync(CancellationToken cancellationToken)
            => RunAsync(TikCommandParameterFormat.Filter, null, InterpretSingleRow, cancellationToken);

        async Task<ITikReSentence?> ITikCommandAsync.ExecuteSingleRowOrDefaultAsync(CancellationToken cancellationToken)
        {
            var rows = await RunAsync(TikCommandParameterFormat.Filter, null, InterpretList, cancellationToken)
                .ConfigureAwait(false);
            return TikCommandResult.SingleRow(this, rows, throwIfMissing: false);
        }

        Task<IList<ITikReSentence>> ITikCommandAsync.ExecuteListAsync(CancellationToken cancellationToken)
            => RunAsync(TikCommandParameterFormat.Filter, null, InterpretList, cancellationToken);

        Task<IList<ITikReSentence>> ITikCommandAsync.ExecuteListAsync(string[] proplistFields, CancellationToken cancellationToken)
        {
            Guard.ArgumentNotNull(proplistFields, nameof(proplistFields));
            return RunAsync(TikCommandParameterFormat.Filter, null, InterpretList, cancellationToken,
                proplistFields: proplistFields);
        }

        private Task<string?> ExecuteScalarInternalAsync(string? target, bool allowReturnDefault, string? defaultValue,
            CancellationToken cancellationToken, bool forceTag = true)
        {
            var targetParameter = target != null
                ? new ITikCommandParameter[] { new ApiCommandParameter(TikSpecialProperties.Proplist, target, TikCommandParameterFormat.NameValue) }
                : new ITikCommandParameter[] { };
            // InterpretScalar genuinely returns null when allowReturnDefault is set and defaultValue is null.
            return RunAsync(TikCommandParameterFormat.NameValue, targetParameter,
                r => InterpretScalar(r, allowReturnDefault, defaultValue), cancellationToken, forceTag);
        }

        // ── The login exchange (ApiConnection.Login_v3Async) ───────────────────
        //
        // Same two calls the synchronous login made, with the same interpreters — the only difference is
        // forceTag:false, which keeps login following the connection's SendTagWithSyncCommand policy exactly
        // as CallCommandSync does. The async surface otherwise tags unconditionally (so /cancel has something
        // to address), and letting that reach login would have changed the bytes of the one exchange that
        // must not change: it is not cancellable, and pre-6.43 routers speak a different login protocol that
        // no test in this repo can reach.

        internal Task<string?> LoginScalarOrDefaultAsync()
            => ExecuteScalarInternalAsync(null, allowReturnDefault: true, defaultValue: null,
                CancellationToken.None, forceTag: false);

        internal Task LoginNonQueryAsync()
            => RunAsync(TikCommandParameterFormat.NameValue, null,
                r => { InterpretNonQuery(r.ToArray()); return (object?)null; },
                CancellationToken.None, forceTag: false);

        // ExecuteList(fields): one =.proplist=a,b word, the documented single word every transport reads.
        private ITikCommandParameter[] ProplistParameters(string[]? proplist)
        {
            var fieldList = FieldListParameter(proplist);
            return fieldList == null ? new ITikCommandParameter[] { } : new[] { fieldList };
        }

        // The one place the async surface talks to the connection: build the sentence, await the answer,
        // hand it to the shared interpreter. The run begins and ends here exactly as in the sync methods,
        // so the two surfaces share the "one command at a time per ITikCommand" rule too.
        private async Task<T> RunAsync<T>(TikCommandParameterFormat format, ITikCommandParameter[]? extraParameters,
            Func<IEnumerable<ApiSentence>, T> interpret, CancellationToken cancellationToken, bool forceTag = true,
            string[]? proplistFields = null)
        {
            EnsureConnectionSet();
            BeginRun();
            try
            {
                string[] commandRows = ConstructCommandText(format,
                    proplistFields != null ? ProplistParameters(proplistFields) : extraParameters ?? new ITikCommandParameter[] { });
                var response = await Session.CallCommandCoreAsync(commandRows, cancellationToken, forceTag).ConfigureAwait(false);
                return interpret(EnsureApiSentences(response));
            }
            finally
            {
                EndRun();
            }
        }

        public override ITikReSentence ExecuteSingleRow()
        {
            EnsureConnectionSet();
            BeginRun();
            try
            {
                string[] commandRows = ConstructCommandText(TikCommandParameterFormat.Filter);
                return InterpretSingleRow(EnsureApiSentences(Session.CallCommandSync(commandRows)));
            }
            finally
            {
                EndRun();
            }
        }

        public override ITikReSentence? ExecuteSingleRowOrDefault()
        {
            return TikCommandResult.SingleRow(this, ExecuteList().ToList(), throwIfMissing: false);
        }

        public override IEnumerable<ITikReSentence> ExecuteList()
        {
            return ExecuteListInternal(null);
        }

        public override IEnumerable<ITikReSentence> ExecuteList(params string[] proplist)
        {
            Guard.ArgumentNotNull(proplist, nameof(proplist));

            return ExecuteListInternal(proplist);
        }

        private IEnumerable<ITikReSentence> ExecuteListInternal(params string[]? proplist)
        {
            EnsureConnectionSet();
            BeginRun();
            try
            {
                string[] commandRows = ConstructCommandText(TikCommandParameterFormat.Filter, ProplistParameters(proplist));
                return InterpretList(EnsureApiSentences(Session.CallCommandSync(commandRows)));
            }
            finally
            {
                EndRun();
            }
        }

        public override void ExecuteWithCallback(Action<ITikReSentence> oneResponseCallback,
            Action<ITikTrapSentence>? errorCallback = null,
            Action? onDoneCallback = null)
        {
            ExecuteAsyncCore(oneResponseCallback, errorCallback, onDoneCallback, null);
        }

        /// <summary>
        /// <see cref="ExecuteWithCallback(Action{ITikReSentence}, Action{ITikTrapSentence}, Action)"/> plus one extra
        /// hook: <paramref name="onTerminalCallback"/> is invoked for whichever sentence ENDED the command —
        /// <c>!done</c> or <c>!fatal</c> — after the running state has been cleared.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is what the bounded readers below wait on. <c>onDoneCallback</c> could not serve: it
        /// deliberately fires only for <c>!done</c>, so a connection that dies mid-stream would leave a waiter
        /// asleep. That is why the bounded readers wait on this signal and not on the callback.
        /// </para>
        /// <para>
        /// The <c>!fatal</c> reaches here carrying the reader loop's reason, so a waiter can report
        /// what the other side said rather than a generic "connection has been closed".
        /// </para>
        /// </remarks>
        private void ExecuteAsyncCore(Action<ITikReSentence> oneResponseCallback,
            Action<ITikTrapSentence>? errorCallback,
            Action? onDoneCallback,
            Action<ITikSentence>? onTerminalCallback)
        {
            EnsureConnectionSet();
            BeginRun();
            System.Diagnostics.Debug.Assert(_asyncLoadingThread == null);

            string tag = TagSequence.NextTag();
            _asynchronouslyRunningTag = tag;

            try
            {
                string[] commandRows = ConstructCommandText(TikCommandParameterFormat.NameValue);
                _asyncLoadingThread = Session.CallCommandCallbackThread(commandRows, tag,
                                        response =>
                                        {
                                            ApiReSentence? reResponse = response as ApiReSentence;
                                            if (reResponse != null)
                                            {
                                                if (oneResponseCallback != null)
                                                    oneResponseCallback(reResponse);
                                            }
                                            else
                                            {
                                                ApiTrapSentence? trapResponse = response as ApiTrapSentence;
                                                if (trapResponse != null)
                                                {
                                                    if (trapResponse.CategoryCode == "2" && trapResponse.Message == "interrupted")
                                                    {
                                                        //correct state - async operation has been Cancelled.
                                                    }
                                                    else
                                                    {
                                                        //incorrect - any error occurs
                                                        if (errorCallback != null)
                                                            errorCallback(trapResponse);
                                                    }
                                                }
                                                else if (response is ApiDoneSentence || response is ApiFatalSentence)
                                                {
                                                    //REMARKS: we are expecting !trap + !done sentences when any error occurs
                                                    EndRun();
                                                    _asynchronouslyRunningTag = null;
                                                    _asyncLoadingThread = null;

                                                    if (response is ApiDoneSentence && onDoneCallback != null)
                                                        onDoneCallback();

                                                    // A !fatal has to reach the caller by exactly ONE route.
                                                    // The bounded readers below pass an onTerminalCallback and
                                                    // take it from there; ExecuteWithCallback has no such hook,
                                                    // and without this none of its three callbacks fired at all —
                                                    // an idle /listen simply stopped at ReceiveTimeout and the
                                                    // caller went on believing it was listening. Reported as a
                                                    // trap, which is how the other ten transports report the
                                                    // same failure (Connection/PollingMonitorEngine).
                                                    if (response is ApiFatalSentence fatal && !fatal.ClientInitiated
                                                        && onTerminalCallback == null && errorCallback != null)
                                                        errorCallback(
                                                            new Connection.TikTrapSentenceResult(fatal.Message));

                                                    // Last, so a waiter woken by this signal already sees
                                                    // everything set above it.
                                                    if (onTerminalCallback != null)
                                                        onTerminalCallback(response);
                                                }
                                            }
                                        });
            }
            catch
            {
                EndRun();
                _asynchronouslyRunningTag = null;
                throw;
            }
        }

        public override IEnumerable<ITikReSentence> ExecuteListWithDuration(TimeSpan duration)
        {
            bool wasAborted;
            string? abortReason;
            var result = ExecuteListWithDuration(duration, out wasAborted, out abortReason);

            if (wasAborted)
                throw new TikCommandAbortException(this, abortReason!);   // non-null exactly when wasAborted is true
            else
                return result;
        }

        // ── Bounded reads: one harness ────────────────────────────────────────
        //
        // ExecuteListWithDuration, ExecuteListUntilDone and the IAsyncEnumerable stream run the command through
        // ExecuteAsyncCore and wait for it: in the calling thread for the first two, in the enumeration for the
        // stream. BoundedRead wires the four callbacks once and records what happened; BoundedRead.Ending decides
        // once, after the command has ended, how it ended; each method keeps its own mapping of an ending to a result
        // or an exception.
        //
        // The wait is on the command itself (the callbacks that carry its sentences), not on a clock that re-asks
        // whether it has ended: an end is seen when it happens, a lost connection is the !fatal the reader loop
        // delivers to this command with its reason, and a !trap's message stands even though its own !done follows.
        //
        // The signal is deliberately NOT disposed: the pump thread can still set it after the caller has returned
        // (a !trap is followed by its own !done), and disposing it underneath would throw inside a callback whose
        // exceptions are swallowed by design.

        private enum BoundedReadEnding
        {
            /// <summary>The command's own <c>!done</c>.</summary>
            Done,
            /// <summary>A <c>!trap</c>: the router's error.</summary>
            Trap,
            /// <summary>A <c>!fatal</c>: the router closed the session, or the reader loop lost the connection.</summary>
            ConnectionLost,
            /// <summary>The caller's duration or timeout ran out while the command was still running.</summary>
            DurationElapsed,
            /// <summary>Ended without any of the above.</summary>
            Cancelled,
        }

        /// <summary>One bounded read: where its rows go, and what ended it.</summary>
        private sealed class BoundedRead
        {
            private readonly Action<ITikReSentence> _onRow;
            private readonly Action _onEnded;
            private volatile ITikTrapSentence? _trap;
            private volatile ApiFatalSentence? _fatal;
            private volatile bool _doneReceived;

            private BoundedRead(Action<ITikReSentence> onRow, Action onEnded)
            {
                _onRow = onRow;
                _onEnded = onEnded;
            }

            /// <summary>Rows collected into a list, the end signalled on an event (the synchronous reads).</summary>
            internal static BoundedRead Collecting(ApiCommand command, out Func<List<ITikReSentence>> snapshot,
                out ManualResetEventSlim finished)
            {
                var rows = new List<ITikReSentence>();
                var rowsLock = new object();
                var signal = new ManualResetEventSlim(false);
                finished = signal;
                // The pump thread appends to the list; the caller reads it once the command has ended. A copy, so the
                // two cannot overlap on the trap path, where the pump keeps running until its !done.
                snapshot = () => { lock (rowsLock) return new List<ITikReSentence>(rows); };
                return new BoundedRead(row =>
                {
                    lock (rowsLock)
                    {
                        if (command.IsRunning)
                            rows.Add(row);
                    }
                }, signal.Set);
            }

#if NET8_0_OR_GREATER
            /// <summary>Rows written into a channel, the end signalled by completing it (the stream).</summary>
            internal static BoundedRead Streaming(System.Threading.Channels.ChannelWriter<ITikReSentence> writer)
                // The ending fields are written before TryComplete, and the consumer reads them only after observing the
                // completion, which is the hand-over.
                => new BoundedRead(row => writer.TryWrite(row), () => writer.TryComplete());
#endif

            internal ITikTrapSentence? Trap => _trap;
            internal ApiFatalSentence? Fatal => _fatal;

            /// <summary>The <c>!fatal</c>'s reason, or "Connection has been closed" when it gave none.</summary>
            internal string FatalReason
                => string.IsNullOrEmpty(_fatal?.Message) ? "Connection has been closed" : _fatal!.Message;

            /// <summary>Runs the command with the four callbacks, once.</summary>
            internal void Start(ApiCommand command)
                => command.ExecuteAsyncCore(
                    row => _onRow(row),
                    trap =>
                    {
                        _trap = trap;
                        _onEnded();   // a !trap ends this read; its !done arrives afterwards
                    },
                    onDoneCallback: () => _doneReceived = true,
                    onTerminalCallback: sentence =>
                    {
                        if (sentence is ApiFatalSentence fatal)
                            _fatal = fatal;
                        _onEnded();
                    });

            /// <summary>
            /// How the read ended, in priority order: the caller's deadline (when it ran out first), the router's trap,
            /// a lost connection, the command's own <c>!done</c>, else cancelled.
            /// </summary>
            internal BoundedReadEnding Ending(bool deadlineElapsed)
            {
                if (deadlineElapsed)
                    return BoundedReadEnding.DurationElapsed;
                if (_trap != null)
                    return BoundedReadEnding.Trap;
                if (_fatal != null)
                    return BoundedReadEnding.ConnectionLost;
                return _doneReceived ? BoundedReadEnding.Done : BoundedReadEnding.Cancelled;
            }
        }

        public override IEnumerable<ITikReSentence> ExecuteListWithDuration(TimeSpan duration, out bool wasAborted, out string? abortReason)
        {
            var read = BoundedRead.Collecting(this, out var snapshot, out var finished);
            read.Start(this);

            // Waits in the calling thread, no longer than the requested duration.
            bool elapsed = !finished.Wait(TikTimeSpans.ToDuration(duration));
            if (!elapsed)
                EndRun();

            wasAborted = false;
            abortReason = null;   // meaningful only when wasAborted is true, per the interface doc
            switch (read.Ending(elapsed))
            {
                case BoundedReadEnding.DurationElapsed:
                    // The normal end of a bounded read: the command was still streaming.
                    CancelInternal(true, -1);   // and join the loading thread
                    break;
                case BoundedReadEnding.Trap:
                    wasAborted = true;
                    abortReason = read.Trap!.Message;   // what the router said
                    break;
                case BoundedReadEnding.ConnectionLost:
                    wasAborted = true;
                    abortReason = read.FatalReason;
                    break;
                case BoundedReadEnding.Cancelled:
                    wasAborted = true;
                    abortReason = "Cancelled";
                    break;
            }
            return snapshot();
        }

        public override IEnumerable<ITikReSentence> ExecuteListUntilDone(TimeSpan? timeout = null)
        {
            var read = BoundedRead.Collecting(this, out var snapshot, out var finished);
            read.Start(this);

            bool elapsed = !(timeout.HasValue
                ? finished.Wait(TikTimeSpans.ToDuration(timeout.Value))
                : finished.Wait(Timeout.Infinite));
            if (!elapsed)
                EndRun();

            switch (read.Ending(elapsed))
            {
                case BoundedReadEnding.DurationElapsed:
                    CancelInternal(true, -1);
                    throw new TikCommandAbortException(this, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "Command did not finish within {0} second(s).", timeout!.Value.TotalSeconds));
                case BoundedReadEnding.Trap:
                    throw new TikCommandTrapException(this, read.Trap!);
                case BoundedReadEnding.ConnectionLost:
                    throw new IOException(string.IsNullOrEmpty(read.Fatal!.Message)
                        ? "Connection has been closed."
                        : "Connection has been closed: " + read.Fatal.Message);
                default:
                    return snapshot();   // !done received
            }
        }

        private bool CancelInternal(bool joinLoadingThread, int millisecondsTimeout)
        {
            if (IsRunning && _asynchronouslyRunningTag != null)
            {
                // Capture the thread reference BEFORE ExecuteNonQuery — the async thread may set
                // _asyncLoadingThread to null when it processes its own !done, which can race with
                // the /cancel response arriving and leaving _asyncLoadingThread null before we read it.
                Thread? loadingThread = _asyncLoadingThread;

                ApiCommand cancelCommand = new ApiCommand(Session, "/cancel", TikCommandParameterFormat.Default,
                    new ApiCommandParameter("tag", _asynchronouslyRunningTag, TikCommandParameterFormat.NameValue), // tag we are cancelling: REMARKS: =tag=1234 and not =.tag=1234
                    new ApiCommandParameter(TikSpecialProperties.Tag, "c_" + _asynchronouslyRunningTag, TikCommandParameterFormat.Tag) //tag of cancell command itself
                    );
                // A bounded CancelAndJoin promises to come back within millisecondsTimeout. The /cancel is a
                // command like any other, so on the connection's ReceiveTimeout it could sit for 30 s before
                // the budget was ever looked at — measured once on a loaded router, where CancelAndJoin(2000)
                // threw a receive timeout after 62 s instead of returning false after 2 s. So the cancel gets
                // the caller's budget and the join gets what is left of it.
                //
                // A cancel that does not come back still THROWS rather than degrading to false: "the router
                // never answered the cancel" and "the command did not stop in time" are different facts, and
                // only the first one says the connection is in trouble. What changes is when you hear it.
                int budgetStart = Environment.TickCount;
                cancelCommand.ExecuteNonQuery(
                    millisecondsTimeout > 0 ? millisecondsTimeout : Session.ReceiveTimeoutMs);

                if (joinLoadingThread)
                {
                    if (loadingThread != null)
                    {
                        if (millisecondsTimeout > 0)
                        {
                            int remaining = millisecondsTimeout - unchecked(Environment.TickCount - budgetStart);
                            return remaining > 0 && loadingThread.Join(remaining);
                        }
                        else
                        {
                            loadingThread.Join();
                            return true;
                        }
                    }
                }
            }
            return true;
        }

        public override void Cancel()
        {
            CancelInternal(false, 0);
        }

        public override void CancelAndJoin()
        {
            CancelInternal(true, -1);
        }

        public override bool CancelAndJoin(TimeSpan timeout)
        {
            return CancelInternal(true, TikTimeSpans.ToWaitMilliseconds(timeout, nameof(timeout)));
        }

#if NET8_0_OR_GREATER
        // ── Streaming reads as IAsyncEnumerable ─────────────────────────────
        //
        // The synchronous ExecuteListWithDuration/ExecuteListUntilDone above collect rows into a List and
        // hand it over once the read has ended, so a caller watching /tool/torch learns nothing until the
        // window closes. The rows were always arriving one at a time — they were being accumulated, not
        // waited for — so the async form needs no new protocol work: the same callbacks write into a
        // Channel, and the caller reads it as it fills.
        //
        // Ending is unchanged and deliberately shared with the synchronous methods: the requested duration
        // (which cancels the command on the router), the command's own !done, a !trap, or a !fatal. A trap or
        // fatal is thrown out of the enumeration as TikCommandAbortException, matching what
        // ExecuteListWithDuration(TimeSpan) does with the same event - a caller must not read a truncated stream
        // as a complete one.

        /// <inheritdoc/>
        public IAsyncEnumerable<ITikReSentence> ExecuteListWithDurationAsync(TimeSpan duration,
            CancellationToken cancellationToken = default)
            => StreamAsync(TikTimeSpans.ToDuration(duration), cancelOnEnd: true, cancellationToken);

        /// <inheritdoc/>
        public IAsyncEnumerable<ITikReSentence> ExecuteListUntilDoneAsync(TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
            => StreamAsync(timeout.HasValue ? TikTimeSpans.ToDuration(timeout.Value) : (TimeSpan?)null,
                cancelOnEnd: false, cancellationToken);

        private async IAsyncEnumerable<ITikReSentence> StreamAsync(TimeSpan? limit, bool cancelOnEnd,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var channel = System.Threading.Channels.Channel.CreateUnbounded<ITikReSentence>(
                new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

            var read = BoundedRead.Streaming(channel.Writer);
            read.Start(this);

            // A deadline is a second way to end the read, not a poll: the channel completes either when the
            // command does or when this fires, whichever happens first.
            using var deadline = limit.HasValue
                ? new CancellationTokenSource(limit.Value)
                : new CancellationTokenSource();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);

            try
            {
                while (true)
                {
                    ITikReSentence row;
                    try
                    {
                        row = await channel.Reader.ReadAsync(linked.Token).ConfigureAwait(false);
                    }
                    catch (System.Threading.Channels.ChannelClosedException)
                    {
                        break;      // the command ended - !done, !trap or !fatal
                    }
                    catch (OperationCanceledException)
                    {
                        break;      // the duration elapsed, or the caller cancelled
                    }
                    yield return row;
                }
            }
            finally
            {
                if (IsRunning && (cancelOnEnd || linked.IsCancellationRequested))
                    CancelInternal(true, -1);
                EndRun();
            }

            // Whatever ended the stream is reported after the rows the caller already received, so a
            // truncated read cannot be mistaken for a complete one. The deadline is not an ending of its own here:
            // for a duration it is the normal end, and a timeout counts only when nothing else ended the read.
            var ending = read.Ending(deadlineElapsed: false);
            switch (ending)
            {
                case BoundedReadEnding.Trap:
                    throw new TikCommandAbortException(this, read.Trap!.Message);
                case BoundedReadEnding.ConnectionLost:
                    throw new TikCommandAbortException(this, read.FatalReason);
            }
            if (cancellationToken.IsCancellationRequested)
                cancellationToken.ThrowIfCancellationRequested();
            if (ending != BoundedReadEnding.Done && !cancelOnEnd && limit.HasValue && deadline.IsCancellationRequested)
                throw new TikCommandAbortException(this, "Timeout");
        }
#endif

    }
}
