using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.Api
{
    internal class ApiCommand: ITikCommand, ITikCommandAsync
#if NET8_0_OR_GREATER
        , ITikStreamingCommand
#endif
    {
        private volatile bool _isRuning;
        private volatile int _asynchronouslyRunningTag;
        private volatile Thread? _asyncLoadingThread; // only set while an ExecuteWithCallback-family command is running; null otherwise
        private readonly List<ITikCommandParameter> _parameters = new List<ITikCommandParameter>();
        private ApiConnection _connection = null!; // set via Connection property before EnsureConnectionSet() is called
        private string _commandText = null!; // set via CommandText property before EnsureCommandTextSet() is called
        private TikCommandParameterFormat _defaultParameterFormat;

        public ITikConnection Connection
        {
            get { return _connection; }
            set
            {
                Guard.ArgumentOfType<ApiConnection>(value, "Session");
                EnsureNotRunning();

                _connection = (ApiConnection)value;
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

        public bool IsRunning
        {
            get { return _isRuning; }
        }

        public IList<ITikCommandParameter> Parameters
        {
            get { return _parameters; }
        }

        public TikCommandParameterFormat DefaultParameterFormat
        {
            get { return _defaultParameterFormat; }
            set { _defaultParameterFormat = value; }
        }

        public ApiCommand()
        {
            _defaultParameterFormat = TikCommandParameterFormat.Default;
        }

        public ApiCommand(TikCommandParameterFormat defaultParameterFormat)
        {
            _defaultParameterFormat = defaultParameterFormat;
        }

        public ApiCommand(ITikConnection connection)
            : this()
        {
            Connection = connection;
        }

        public ApiCommand(ITikConnection connection, TikCommandParameterFormat defaultParameterFormat)
            : this(defaultParameterFormat)
        {
            Connection = connection;
        }

        public ApiCommand(ITikConnection connection, string commandText)
            :this(connection)
        {
            CommandText = commandText;
        }

        public ApiCommand(ITikConnection connection, string commandText, TikCommandParameterFormat defaultParameterFormat)
            : this(connection, defaultParameterFormat)
        {
            CommandText = commandText;
        }


        public ApiCommand(ITikConnection connection, string commandText, params ITikCommandParameter[] parameters)
            : this(connection, commandText)
        {
            _parameters.AddRange(parameters);
        }

        public ApiCommand(ITikConnection connection, string commandText, TikCommandParameterFormat defaultParameterFormat, params ITikCommandParameter[] parameters)
            : this(connection, commandText, defaultParameterFormat)
        {
            _parameters.AddRange(parameters);
        }

        private void EnsureNotRunning()
        {
            if (_isRuning)
                throw new InvalidOperationException("Command is already running.");
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

        private TikCommandParameterFormat ResolveParameterFormat(TikCommandParameterFormat usecaseDefaultFormat, TikCommandParameterFormat commandDefaultFormat, ITikCommandParameter parameter)
        {
            if (parameter.ParameterFormat != TikCommandParameterFormat.Default)
                return parameter.ParameterFormat;
            else if (parameter.Name == TikSpecialProperties.Tag)
                return TikCommandParameterFormat.Tag; //.tag=1231
            else if (commandDefaultFormat != TikCommandParameterFormat.Default)
                return commandDefaultFormat;
            else if (usecaseDefaultFormat != TikCommandParameterFormat.Default)
                return usecaseDefaultFormat;
            else
                return TikCommandParameterFormat.NameValue;
        }

        /// <summary>
        /// Client-side marker parameters that must NEVER be written to the binary API wire.
        /// (Mirror of <see cref="tik4net.Rest.RestRequestBuilder"/>.IsSpecialParam / CliCommandBuilder.IsSpecialParam;
        /// the membership differs per transport on purpose.) Unlike REST/CLI, the binary API understands
        /// <c>.proplist</c> and <c>.tag</c> natively (they ARE valid wire words), so the only thing stripped
        /// here is the CLI-only pair: the <c>.cli-stats</c> stats marker and the <c>.cli-json</c>
        /// <c>:serialize</c> marker (the binary API frames values, so it never needs either).
        /// </summary>
        private static bool IsSpecialParam(string name)
            => name == TikSpecialProperties.CliStats
            || name == TikSpecialProperties.CliJson;

        private string[] ConstructCommandText(TikCommandParameterFormat defaultParameterFormat, params ITikCommandParameter[] additionalParamemeters)
        {
            EnsureCommandTextSet();
            foreach (var additionalParameter in additionalParamemeters)
            {
                if (_parameters.Any(p => p.Name == additionalParameter.Name))
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
            result.AddRange(_parameters.Concat(additionalParamemeters)
                .Where(p => !IsSpecialParam(p.Name))
                .Select(p =>
            {
                if (p.Name.StartsWith("=")) //NameValue format in parameter name
                    return string.Format("{0}={1}", p.Name, p.Value);
                else if (p.Name.StartsWith("?")) //Filter format in parameter name
                    return string.Format("{0}={1}", p.Name, p.Value);
                else
                {
                    switch (ResolveParameterFormat(defaultParameterFormat, _defaultParameterFormat, p))
                    {
                        case TikCommandParameterFormat.Filter:
                            return string.Format("?{0}={1}", p.Name, p.Value);
                        case TikCommandParameterFormat.NameValue:
                            return string.Format("={0}={1}", p.Name, p.Value);
                        case TikCommandParameterFormat.Tag:
                            return string.Format("{0}={1}", p.Name, p.Value);
                        //case TikCommandParameterFormat.NameOnly:
                        //      return string.Format("={0}", p.Name);
                        default:
                            // Not a tik4net exception on purpose: an undefined enum value here is a bug in
                            // the caller's argument, nothing the router said.
                            throw new ArgumentOutOfRangeException(nameof(defaultParameterFormat),
                                ResolveParameterFormat(defaultParameterFormat, _defaultParameterFormat, p),
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

        /// <summary>
        /// True for the read verbs, where a bare !done (no !re row, no =ret=) means "nothing matched" and
        /// <see cref="TikNoSuchItemException"/> is the right answer. Every other verb answers a bare !done
        /// on <b>success</b>, so the same shape there means "this command has no return value" — see
        /// <see cref="TikCommandEmptyResponseException"/>. The two are indistinguishable in the response
        /// alone; only the verb tells them apart.
        /// </summary>
        private bool IsReadVerb()
        {
            switch (tik4net.Connection.TikPath.Verb(_commandText))
            {
                case "print":
                case "get":
                case "getall":
                    return true;
                default:
                    return false;
            }
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
            EnsureReReponse(response.First());
            EnsureDoneResponse(response.Last());
        }

        private void ThrowPossibleResponseError(params ApiSentence[] responseSentences)
        {
            foreach (ApiSentence responseSentence in responseSentences)
            {
                ApiTrapSentence? trapSentence = responseSentence as ApiTrapSentence;
                if (trapSentence != null)
                { //detect well known error responses and convert them to special exceptions
                    switch (TikTrapClassifier.Classify(trapSentence.Message))
                    {
                        case TikTrapKind.NoSuchCommand:
                            throw new TikNoSuchCommandException(this, trapSentence);
                        case TikTrapKind.NoSuchItem:
                            throw new TikNoSuchItemException(this, trapSentence);
                        case TikTrapKind.AlreadyHaveSuchItem:
                            throw new TikAlreadyHaveSuchItemException(this, trapSentence);
                        default:
                            throw new TikCommandTrapException(this, trapSentence);
                    }
                }
                ApiFatalSentence? fatalSentence = responseSentence as ApiFatalSentence;
                if (fatalSentence != null)
                    throw new TikCommandFatalException(this, fatalSentence.Message);
            }
        }

        private ApiDoneSentence EnsureDoneResponse(ApiSentence responseSentence)
        {
            ApiDoneSentence? doneSentence = responseSentence as ApiDoneSentence;
            if (doneSentence == null)
                throw new TikCommandUnexpectedResponseException("!done sentence expected as result.", this, responseSentence);

            return doneSentence;
        }

        private void EnsureReReponse(params ApiSentence[] responseSentences)
        {
            foreach (ApiSentence responseSentence in responseSentences)
            {
                ApiReSentence? reSentence = responseSentence as ApiReSentence;
                if (reSentence == null)
                    throw new TikCommandUnexpectedResponseException("!re sentence expected as result.", this, responseSentence);
            }
        }


        public void ExecuteNonQuery() => ExecuteNonQuery(_connection?.ReceiveTimeout ?? 0);

        /// <summary>
        /// <see cref="ExecuteNonQuery()"/> with an explicit reply deadline. Used by
        /// <see cref="CancelInternal"/> so a bounded <see cref="CancelAndJoin(int)"/> cannot spend the
        /// connection's <c>ReceiveTimeout</c> before its own budget is even consulted.
        /// </summary>
        private void ExecuteNonQuery(int receiveTimeoutMs)
        {
            EnsureConnectionSet();
            EnsureNotRunning();

            _isRuning = true;
            try
            {
                string[] commandRows = ConstructCommandText(TikCommandParameterFormat.NameValue);
                InterpretNonQuery(EnsureApiSentences(_connection.CallCommandSync(commandRows, receiveTimeoutMs)).ToArray());
            }
            finally
            {
                _isRuning = false;
            }
        }

        public string ExecuteScalar()
        {
            // allowReturnDefault:false - ExecuteScalarInternal either returns a real value or throws, never null.
            return ExecuteScalarInternal(null, false)!;
        }

        public string ExecuteScalar(string target)
        {
            // allowReturnDefault:false - ExecuteScalarInternal either returns a real value or throws, never null.
            return ExecuteScalarInternal(target, false)!;
        }

        public string? ExecuteScalarOrDefault()
        {
            // Returns null (or the caller's defaultValue, which may be null) when nothing matched —
            // the interface says so and now types it that way.
            return ExecuteScalarInternal(null, true, null);
        }

        public string? ExecuteScalarOrDefault(string? defaultValue)
        {
            return ExecuteScalarInternal(null, true, defaultValue);
        }

        public string? ExecuteScalarOrDefault(string? defaultValue, string target)
        {
            return ExecuteScalarInternal(target, true, defaultValue);
        }

        private string? ExecuteScalarInternal(string? target, bool allowReturnDefault, string? defaultValue = null)
        {
            EnsureConnectionSet();
            EnsureNotRunning();

            _isRuning = true;
            try
            {
                var targetParameterInArray = target != null ? new ITikCommandParameter[] { new ApiCommandParameter(TikSpecialProperties.Proplist, target, TikCommandParameterFormat.NameValue) } : new ITikCommandParameter[] { };
                string[] commandRows = ConstructCommandText(TikCommandParameterFormat.NameValue, targetParameterInArray);
                IEnumerable<ApiSentence> response = EnsureApiSentences(_connection.CallCommandSync(commandRows));
                return InterpretScalar(response, allowReturnDefault, defaultValue);
            }
            finally
            {
                _isRuning = false;
            }
        }

        // ── Response interpretation, shared by the sync and async surfaces ────
        //
        // Extracted rather than duplicated: which answer means "not found", which means "succeeded with
        // nothing to return", and which is a protocol violation must not be allowed to drift between the two
        // paths (the same rule P2.2 applied to the CLI/REST transports).

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
                    else if (allowReturnDefault)
                        return defaultValue;
                    else if (IsReadVerb())
                        // A read that produced no !re row genuinely found nothing.
                        throw new TikNoSuchItemException(this);
                    else
                        // Anything else answering a bare !done with no =ret= succeeded and simply has no
                        // return value — that is how the API answers every write. Reporting that as "no such
                        // item" invented a router error for a command that had worked, and pointed the caller
                        // at a record that was never missing. The CLI transports hit the same case (empty
                        // output) and now answer identically.
                        throw new TikCommandEmptyResponseException(this,
                            "The router answered !done without a =ret= value, so there is no scalar value to return. "
                            + "Commands that return nothing (set/unset/remove/enable/…) succeed this way — "
                            + "run them with ExecuteNonQuery(), or use ExecuteScalarOrDefault() when the value is optional.");
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

            if (response.OfType<ApiReSentence>().Count() > 1)
                throw new TikCommandAmbiguousResultException(this);
            EnsureOneReAndDone(response);
            return (ApiReSentence)response.First();
        }

        private IList<ITikReSentence> InterpretList(IEnumerable<ApiSentence> response)
        {
            ThrowPossibleResponseError(response.ToArray());

            EnsureReReponse(response.Take(response.Count() - 1).ToArray());   //!re  - reapeating
            EnsureDoneResponse(response.Last()); //!done

            return response.Take(response.Count() - 1).Cast<ITikReSentence>().ToList();
        }

        // ── ITikCommandAsync (P2.3 / job B) ───────────────────────────────────
        //
        // Mirrors the synchronous methods rather than either side wrapping the other — the same rule P2.2
        // applied to the CLI and REST transports. Everything after the call is the shared Interpret* helpers
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
            if (rows.Count > 1)
                throw new TikCommandAmbiguousResultException(this);
            return rows.SingleOrDefault();
        }

        Task<IList<ITikReSentence>> ITikCommandAsync.ExecuteListAsync(CancellationToken cancellationToken)
            => RunAsync(TikCommandParameterFormat.Filter, null, InterpretList, cancellationToken);

        Task<IList<ITikReSentence>> ITikCommandAsync.ExecuteListAsync(string[] proplistFields, CancellationToken cancellationToken)
        {
            Guard.ArgumentNotNull(proplistFields, nameof(proplistFields));
            return RunAsync(TikCommandParameterFormat.Filter, ProplistParameters(proplistFields), InterpretList, cancellationToken);
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

        private ITikCommandParameter[] ProplistParameters(string[] proplist)
            => proplist == null
                ? new ITikCommandParameter[] { }
                : proplist.Select(p => (ITikCommandParameter)new ApiCommandParameter(TikSpecialProperties.Proplist, p, TikCommandParameterFormat.NameValue)).ToArray();

        // The one place the async surface talks to the connection: build the sentence, await the answer,
        // hand it to the shared interpreter. _isRuning is set and cleared here exactly as the sync methods
        // do it, so the two surfaces share the "one command at a time per ITikCommand" rule too.
        private async Task<T> RunAsync<T>(TikCommandParameterFormat format, ITikCommandParameter[]? extraParameters,
            Func<IEnumerable<ApiSentence>, T> interpret, CancellationToken cancellationToken, bool forceTag = true)
        {
            EnsureConnectionSet();
            EnsureNotRunning();

            var apiConnection = _connection as ApiConnection;
            if (apiConnection == null)
                throw new InvalidOperationException("ApiCommand requires an ApiConnection.");

            _isRuning = true;
            try
            {
                string[] commandRows = ConstructCommandText(format, extraParameters ?? new ITikCommandParameter[] { });
                var response = await apiConnection.CallCommandCoreAsync(commandRows, cancellationToken, forceTag).ConfigureAwait(false);
                return interpret(EnsureApiSentences(response));
            }
            finally
            {
                _isRuning = false;
            }
        }

        public ITikReSentence ExecuteSingleRow()
        {
            EnsureConnectionSet();
            EnsureNotRunning();

            _isRuning = true;
            try
            {
                string[] commandRows = ConstructCommandText(TikCommandParameterFormat.Filter);
                return InterpretSingleRow(EnsureApiSentences(_connection.CallCommandSync(commandRows)));
            }
            finally
            {
                _isRuning = false;
            }
        }

        public ITikReSentence? ExecuteSingleRowOrDefault()
        {
            var sentences = ExecuteList();

            if (sentences.Count() > 1)
                throw new TikCommandAmbiguousResultException(this);
            return sentences.SingleOrDefault();
        }

        public IEnumerable<ITikReSentence> ExecuteList()
        {
            return ExecuteListInternal(null);
        }

        public IEnumerable<ITikReSentence> ExecuteList(params string[] proplist)
        {
            Guard.ArgumentNotNull(proplist, nameof(proplist));

            return ExecuteListInternal(proplist);
        }

        private IEnumerable<ITikReSentence> ExecuteListInternal(params string[]? proplist)
        {
            EnsureConnectionSet();
            EnsureNotRunning();

            _isRuning = true;
            try
            {
                var proplistParameters = proplist == null ? new ITikCommandParameter[] { } : proplist.Select(p => new ApiCommandParameter(TikSpecialProperties.Proplist, p, TikCommandParameterFormat.NameValue)).ToArray();
                string[] commandRows = ConstructCommandText(TikCommandParameterFormat.Filter, proplistParameters);
                return InterpretList(EnsureApiSentences(_connection.CallCommandSync(commandRows)));
            }
            finally
            {
                _isRuning = false;
            }
        }

        public void ExecuteWithCallback(Action<ITikReSentence> oneResponseCallback,
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
            EnsureNotRunning();
            System.Diagnostics.Debug.Assert(_asyncLoadingThread == null);

            int tag = TagSequence.Next();
            _isRuning = true;
            _asynchronouslyRunningTag = tag;

            try
            {
                string[] commandRows = ConstructCommandText(TikCommandParameterFormat.NameValue);
                _asyncLoadingThread = _connection.CallCommandCallbackThread(commandRows, tag.ToString(),
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
                                                    _isRuning = false;
                                                    _asynchronouslyRunningTag = -1;
                                                    _asyncLoadingThread = null;

                                                    if (response is ApiDoneSentence && onDoneCallback != null)
                                                        onDoneCallback();

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
                _isRuning = false;
                _asynchronouslyRunningTag = -1;
                throw;
            }
            finally
            {
                //still running
            }
        }

        public IEnumerable<ITikReSentence> ExecuteListWithDuration(int durationSec)
        {
            bool wasAborted;
            string abortReason;
            var result = ExecuteListWithDuration(durationSec, out wasAborted, out abortReason);

            if (wasAborted)
                throw new TikCommandAbortException(this, abortReason);
            else
                return result;
        }

        // ── Bounded streaming reads (P2.4) ────────────────────────────────────
        //
        // Both methods below run the command asynchronously and wait for it in the calling thread. The wait
        // is on the command itself — the same callbacks that carry the sentences — not on a 100 ms clock that
        // re-asks "are we there yet?". What that polling loop cost was not CPU but ACCURACY:
        //
        //  * an end was noticed up to a full tick after it happened, so every such read paid up to 100 ms;
        //  * connection loss was read off `_connection.IsOpened` instead of the `!fatal` the reader loop had
        //    already delivered to this very command — and that `!fatal` carries the reason (P2.14), which the
        //    flag does not, so the caller got "Connection has been closed" and never learned what happened;
        //  * the tick could also land between a `!trap` and the `!done` that follows it, and in that ordering
        //    `ExecuteListWithDuration` overwrote the router's message with the literal "Cancelled". Narrow,
        //    and gone here: the reason is now decided once, after the command has ended, in priority order.
        //
        // The signal is deliberately NOT disposed: the pump thread can still set it after we have returned
        // (a `!trap` is followed by its own `!done`), and disposing it underneath would throw inside a
        // callback whose exceptions are swallowed by design.

        public IEnumerable<ITikReSentence> ExecuteListWithDuration(int durationSec, out bool wasAborted, out string abortReason)
        {
            ITikTrapSentence? asyncTrap = null;
            string? fatalMessage = null;
            bool doneReceived = false;
            List<ITikReSentence> result = new List<ITikReSentence>();
            object resultLock = new object();
            ManualResetEventSlim finished = new ManualResetEventSlim(false);
            wasAborted = false;
            abortReason = null!; // meaningful only when wasAborted is true, per the interface doc

            //Async execute, responses are stored in result list
            ExecuteAsyncCore(
                reSentence =>
                {
                    lock (resultLock)
                    {
                        if (_isRuning)
                            result.Add(reSentence);
                    }
                },
                error =>
                {
                    asyncTrap = error;
                    finished.Set(); //a !trap ends this read; its !done arrives afterwards
                },
                onDoneCallback: () =>
                {
                    doneReceived = true;
                },
                onTerminalCallback: sentence =>
                {
                    ApiFatalSentence? fatal = sentence as ApiFatalSentence;
                    if (fatal != null)
                        fatalMessage = string.IsNullOrEmpty(fatal.Message) ? "Connection has been closed" : fatal.Message;
                    finished.Set();
                });

            //wait for the command (in calling =UI? thread), no longer than the requested duration
            if (!finished.Wait(TimeSpan.FromSeconds(Math.Max(0, durationSec))))
            {
                //duration elapsed while the command was still streaming - the normal end of a bounded read
                CancelInternal(true, -1); //Join loading thread
                return SnapshotResult(result, resultLock);
            }

            _isRuning = false;

            if (asyncTrap != null) //ended with an error - report what the router said
            {
                wasAborted = true;
                abortReason = asyncTrap.Message;
            }
            else if (fatalMessage != null)
            {
                wasAborted = true;
                abortReason = fatalMessage;
            }
            else if (!doneReceived)
            {
                wasAborted = true;
                abortReason = "Cancelled";
            }

            return SnapshotResult(result, resultLock);
        }

        public IEnumerable<ITikReSentence> ExecuteListUntilDone(int? timeoutSec = null)
        {
            ITikTrapSentence? asyncTrap = null;
            string? fatalMessage = null;
            List<ITikReSentence> result = new List<ITikReSentence>();
            object resultLock = new object();
            ManualResetEventSlim finished = new ManualResetEventSlim(false);

            ExecuteAsyncCore(
                reSentence =>
                {
                    lock (resultLock)
                    {
                        if (_isRuning)
                            result.Add(reSentence);
                    }
                },
                error =>
                {
                    asyncTrap = error;
                    finished.Set();
                },
                onDoneCallback: null,
                onTerminalCallback: sentence =>
                {
                    ApiFatalSentence? fatal = sentence as ApiFatalSentence;
                    if (fatal != null)
                        fatalMessage = string.IsNullOrEmpty(fatal.Message)
                            ? "Connection has been closed."
                            : "Connection has been closed: " + fatal.Message;
                    finished.Set();
                });

            int timeoutMs = timeoutSec.HasValue
                ? (int)Math.Min(Math.Max(0L, timeoutSec.Value * 1000L), int.MaxValue)
                : Timeout.Infinite;
            if (!finished.Wait(timeoutMs))
            {
                // timeout elapsed — cancel and report
                CancelInternal(true, -1);
                throw new TikCommandAbortException(this, string.Format("Command did not finish within {0} second(s).", timeoutSec));
            }

            _isRuning = false;

            if (asyncTrap != null)
                throw new TikCommandTrapException(this, asyncTrap);
            if (fatalMessage != null)
                throw new IOException(fatalMessage);

            return SnapshotResult(result, resultLock); //!done received
        }

        // The pump thread appends to the list; the caller reads it once the command has ended. Handing back
        // the live list let those two overlap on the trap path, where the pump keeps running until its !done.
        private static List<ITikReSentence> SnapshotResult(List<ITikReSentence> result, object resultLock)
        {
            lock (resultLock)
                return new List<ITikReSentence>(result);
        }

        private bool CancelInternal(bool joinLoadingThread, int milisecondsTimeout)
        {
            if (_isRuning && _asynchronouslyRunningTag >= 0)
            {
                // Capture the thread reference BEFORE ExecuteNonQuery — the async thread may set
                // _asyncLoadingThread to null when it processes its own !done, which can race with
                // the /cancel response arriving and leaving _asyncLoadingThread null before we read it.
                Thread? loadingThread = _asyncLoadingThread;

                ApiCommand cancellCommand = new ApiCommand(_connection, "/cancel",
                    new ApiCommandParameter("tag", _asynchronouslyRunningTag.ToString(), TikCommandParameterFormat.NameValue), // tag we are cancelling: REMARKS: =tag=1234 and not =.tag=1234
                    new ApiCommandParameter(TikSpecialProperties.Tag, "c_"+_asynchronouslyRunningTag.ToString(), TikCommandParameterFormat.Tag) //tag of cancell command itself
                    );
                // A bounded CancelAndJoin promises to come back within milisecondsTimeout. The /cancel is a
                // command like any other, so on the connection's ReceiveTimeout it could sit for 30 s before
                // the budget was ever looked at — measured once on a loaded router, where CancelAndJoin(2000)
                // threw a receive timeout after 62 s instead of returning false after 2 s. So the cancel gets
                // the caller's budget and the join gets what is left of it.
                //
                // A cancel that does not come back still THROWS rather than degrading to false: "the router
                // never answered the cancel" and "the command did not stop in time" are different facts, and
                // only the first one says the connection is in trouble. What changes is when you hear it.
                int budgetStart = Environment.TickCount;
                cancellCommand.ExecuteNonQuery(
                    milisecondsTimeout > 0 ? milisecondsTimeout : _connection.ReceiveTimeout);

                if (joinLoadingThread)
                {
                    if (loadingThread != null)
                    {
                        if (milisecondsTimeout > 0)
                        {
                            int remaining = milisecondsTimeout - unchecked(Environment.TickCount - budgetStart);
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

        public void Cancel()
        {
            CancelInternal(false, 0);
        }

        public void CancelAndJoin()
        {
            CancelInternal(true, -1);
        }

        public bool CancelAndJoin(int milisecondsTimeout)
        {
            return CancelInternal(true, milisecondsTimeout);
        }

        public ITikCommandParameter AddParameter(string name, string value)
        {
            ApiCommandParameter result = new ApiCommandParameter(name, value);
            _parameters.Add(result);

            return result;
        }

        public ITikCommandParameter AddParameter(string name, string value, TikCommandParameterFormat parameterFormat)
        {
            ITikCommandParameter result = AddParameter(name, value);
            result.ParameterFormat = parameterFormat;

            return result;
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

        public override string ToString()
        {
            return CommandText + " PARAMS: " + string.Join("; ", Parameters.Select(p => $"{p.Name}:{p.Value}").ToArray());
        }

        private IEnumerable<ITikCommandParameter> CreateParameters(string[] parameterNamesAndValues)
        {
            List<ApiCommandParameter> parameters = new List<ApiCommandParameter>();
            for (int idx = 0; idx < parameterNamesAndValues.Length / 2; idx++)   // name, value, name, value, ... sequence
            {
                parameters.Add(new ApiCommandParameter(parameterNamesAndValues[idx * 2], parameterNamesAndValues[idx * 2 + 1]));
            }

            return parameters.Cast<ITikCommandParameter>();
        }

        public IEnumerable<ITikCommandParameter> AddParameterAndValues(params string[] parameterNamesAndValues)
        {
            var parameters = CreateParameters(parameterNamesAndValues);
            _parameters.AddRange(parameters);

            return parameters;
        }

#if NET8_0_OR_GREATER
        // ── Streaming reads as IAsyncEnumerable (P2.4's deferred half, D4) ────
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
        // ExecuteListWithDuration(int) does with the same event - a caller must not read a truncated stream
        // as a complete one.

        /// <inheritdoc/>
        public IAsyncEnumerable<ITikReSentence> ExecuteListWithDurationAsync(int durationSec,
            CancellationToken cancellationToken = default)
            => StreamAsync(TimeSpan.FromSeconds(Math.Max(0, durationSec)), cancelOnEnd: true, cancellationToken);

        /// <inheritdoc/>
        public IAsyncEnumerable<ITikReSentence> ExecuteListUntilDoneAsync(int? timeoutSec = null,
            CancellationToken cancellationToken = default)
            => StreamAsync(timeoutSec.HasValue ? TimeSpan.FromSeconds(Math.Max(0, timeoutSec.Value)) : (TimeSpan?)null,
                cancelOnEnd: false, cancellationToken);

        private async IAsyncEnumerable<ITikReSentence> StreamAsync(TimeSpan? limit, bool cancelOnEnd,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var channel = System.Threading.Channels.Channel.CreateUnbounded<ITikReSentence>(
                new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

            ITikTrapSentence? asyncTrap = null;
            string? fatalMessage = null;
            bool doneReceived = false;

            ExecuteAsyncCore(
                reSentence => channel.Writer.TryWrite(reSentence),
                error => { asyncTrap = error; channel.Writer.TryComplete(); },
                onDoneCallback: () => { doneReceived = true; },
                onTerminalCallback: _ =>
                {
                    // The !fatal case carries the reader loop's reason (P2.14); !done needs nothing but the
                    // completion. Reading the field the callbacks set is safe here: the pump thread has
                    // already run them by the time it reaches this one.
                    channel.Writer.TryComplete();
                });

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
                if (_isRuning && (cancelOnEnd || linked.IsCancellationRequested))
                    CancelInternal(true, -1);
                _isRuning = false;
            }

            // Whatever ended the stream is reported after the rows the caller already received, so a
            // truncated read cannot be mistaken for a complete one.
            if (asyncTrap != null)
                throw new TikCommandAbortException(this, asyncTrap.Message);
            if (fatalMessage != null)
                throw new TikCommandAbortException(this, fatalMessage);
            if (cancellationToken.IsCancellationRequested)
                cancellationToken.ThrowIfCancellationRequested();
            if (!doneReceived && !cancelOnEnd && limit.HasValue && deadline.IsCancellationRequested)
                throw new TikCommandAbortException(this, "Timeout");
        }
#endif

    }
}
