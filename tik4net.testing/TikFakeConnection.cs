using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using tik4net.Objects;

namespace tik4net.Testing
{
    /// <summary>
    /// Fake <see cref="ITikConnection"/> for unit testing — no real router required.
    /// <para>
    /// Intercepts at the lowest level (<see cref="CallCommandSync(System.Collections.Generic.IEnumerable{string})"/>) so that all higher-level
    /// code — <see cref="ITikCommand"/> Execute* methods and the O/R mapper extension methods
    /// (<c>LoadAll</c>, <c>Save</c>, <c>Delete</c>, …) — runs through the real parsing and
    /// mapping logic unchanged.
    /// </para>
    /// <para>
    /// Register fake responses with the fluent builder methods before running the code under test:
    /// <list type="bullet">
    ///   <item><see cref="WithResponse(Func{IEnumerable{string},bool},Func{IEnumerable{string},IEnumerable{ITikSentence}})"/> — low-level: match command rows, return raw sentences.</item>
    ///   <item><see cref="WithEntities{TEntity}(TEntity[])"/> — high-level: match the entity load command, auto-serialize entities.</item>
    ///   <item><see cref="WithScalarResponse(Func{IEnumerable{string},bool},string)"/> — match any command that should return a scalar (e.g. /add returning =ret=).</item>
    ///   <item><see cref="WithNonQuery"/> — match any command that should succeed with just !done.</item>
    ///   <item><see cref="WithTrap"/> — simulate router errors.</item>
    /// </list>
    /// </para>
    /// <para>
    /// After the call, inspect <see cref="SentCommands"/> or use <see cref="AssertWasSent(string)"/>
    /// to verify what the code sent.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// var conn = new TikFakeConnection()
    ///     .WithEntities(new IpAddress { Id = "*1", Address = "10.0.0.1/24", Interface = "ether1" })
    ///     .WithScalarResponse(rows => rows.First() == "/ip/address/add", "*2")
    ///     .WithNonQuery(rows => rows.First() == "/ip/address/set");
    ///
    /// var list = conn.LoadAll&lt;IpAddress&gt;();
    /// </code>
    /// </example>
    public sealed class TikFakeConnection : ITikConnection
    {
        private readonly List<(Func<IEnumerable<string>, bool> Predicate, Func<IEnumerable<string>, IEnumerable<ITikSentence>> Response)> _handlers
            = new List<(Func<IEnumerable<string>, bool>, Func<IEnumerable<string>, IEnumerable<ITikSentence>>)>();

        private readonly List<string[]> _sentCommands = new List<string[]>();
        private readonly HashSet<string> _cancelledTags = new HashSet<string>();

        // ── Builder methods ────────────────────────────────────────────────────

        /// <summary>
        /// Registers a low-level response handler. The first matching handler (in registration order) wins.
        /// </summary>
        /// <param name="predicate">Returns true when these command rows should trigger this response.</param>
        /// <param name="response">Factory that produces the fake sentences for the given command rows.</param>
        public TikFakeConnection WithResponse(
            Func<IEnumerable<string>, bool> predicate,
            Func<IEnumerable<string>, IEnumerable<ITikSentence>> response)
        {
            _handlers.Add((predicate, response));
            return this;
        }

        /// <summary>
        /// Registers a low-level response handler that always returns the same sentence list.
        /// </summary>
        public TikFakeConnection WithResponse(
            Func<IEnumerable<string>, bool> predicate,
            IEnumerable<ITikSentence> sentences)
        {
            var list = sentences.ToList();
            return WithResponse(predicate, _ => list);
        }

        /// <summary>
        /// Registers a handler that matches the load command for <typeparamref name="TEntity"/> and
        /// returns the supplied entities serialized to !re sentences via the entity's metadata.
        /// </summary>
        /// <param name="entities">Entities to return. Supports dynamic factories via the overload with <see cref="Func{T}"/>.</param>
        public TikFakeConnection WithEntities<TEntity>(params TEntity[] entities)
            where TEntity : new()
        {
            return WithEntities<TEntity>(() => entities);
        }

        /// <summary>
        /// Registers a handler that matches the load command for <typeparamref name="TEntity"/> and
        /// evaluates <paramref name="entityFactory"/> each time the command is called.
        /// Useful for stateful tests where the entity list changes between calls.
        /// </summary>
        public TikFakeConnection WithEntities<TEntity>(Func<IEnumerable<TEntity>> entityFactory)
            where TEntity : new()
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            string commandPath = metadata.EntityPath + metadata.LoadCommand;

            return WithResponse(
                predicate: rows => rows.First() == commandPath,
                response: _ =>
                {
                    var sentences = new List<ITikSentence>();
                    foreach (var entity in entityFactory())
                    {
                        var words = metadata.Properties
                            .ToDictionary(p => p.FieldName, p => p.GetEntityValue(entity) ?? "");
                        sentences.Add(new TikFakeReSentence(words));
                    }
                    sentences.Add(new TikFakeDoneSentence());
                    return sentences;
                });
        }

        /// <summary>
        /// Registers a handler that returns a scalar value (=ret=) in a !done sentence.
        /// Covers commands such as /add that return the new entity id.
        /// </summary>
        public TikFakeConnection WithScalarResponse(
            Func<IEnumerable<string>, bool> predicate,
            string value)
        {
            return WithScalarResponse(predicate, _ => value);
        }

        /// <summary>
        /// Registers a handler that returns a scalar value produced by <paramref name="valueFactory"/>
        /// so the response can depend on the actual command rows (e.g. to extract a field and echo it back).
        /// </summary>
        public TikFakeConnection WithScalarResponse(
            Func<IEnumerable<string>, bool> predicate,
            Func<IEnumerable<string>, string> valueFactory)
        {
            return WithResponse(predicate, rows => new ITikSentence[]
            {
                new TikFakeDoneSentence(new Dictionary<string, string>
                {
                    { TikSpecialProperties.Ret, valueFactory(rows) }
                })
            });
        }

        /// <summary>
        /// Registers a handler that returns just !done (success with no data).
        /// Covers /set, /remove, /move, /reboot, etc.
        /// </summary>
        public TikFakeConnection WithNonQuery(Func<IEnumerable<string>, bool> predicate)
        {
            return WithResponse(predicate, _ => new ITikSentence[] { new TikFakeDoneSentence() });
        }

        /// <summary>
        /// Registers a handler that returns a !trap sentence followed by !done, simulating a router error.
        /// </summary>
        /// <param name="predicate">Matches the command rows to trap.</param>
        /// <param name="message">Error message (e.g. "no such item", "already have such item").</param>
        /// <param name="categoryCode">Optional MikroTik error category code.</param>
        public TikFakeConnection WithTrap(
            Func<IEnumerable<string>, bool> predicate,
            string message,
            string categoryCode = null)
        {
            return WithResponse(predicate, _ => new ITikSentence[]
            {
                new TikFakeTrapSentence(message, categoryCode),
                new TikFakeDoneSentence()
            });
        }

        // ── Assertion helpers ──────────────────────────────────────────────────

        /// <summary>All command row arrays that were sent through this connection, in order.</summary>
        public IReadOnlyList<string[]> SentCommands
        {
            get { lock (_sentCommands) return _sentCommands.ToList().AsReadOnly(); }
        }

        /// <summary>Throws if no sent command had <paramref name="commandText"/> as its first row.</summary>
        public void AssertWasSent(string commandText)
        {
            lock (_sentCommands)
            {
                if (!_sentCommands.Any(rows => rows.Length > 0 && rows[0] == commandText))
                    throw new InvalidOperationException(
                        $"Expected command '{commandText}' was not sent. " +
                        $"Sent commands: {string.Join(", ", _sentCommands.Select(r => r.FirstOrDefault() ?? "(empty)"))}");
            }
        }

        /// <summary>Throws if no sent command satisfies <paramref name="predicate"/>.</summary>
        public void AssertWasSent(Func<string[], bool> predicate)
        {
            lock (_sentCommands)
            {
                if (!_sentCommands.Any(predicate))
                    throw new InvalidOperationException("Expected command matching predicate was not sent.");
            }
        }

        /// <summary>Returns how many times a command with the given first row was sent.</summary>
        public int GetCallCount(string commandText)
        {
            lock (_sentCommands)
                return _sentCommands.Count(rows => rows.Length > 0 && rows[0] == commandText);
        }

        // ── Internal helpers ───────────────────────────────────────────────────

        internal void CancelTag(string tag)
        {
            lock (_cancelledTags)
                _cancelledTags.Add(tag);
        }

        // ── ITikConnection ─────────────────────────────────────────────────────

        /// <inheritdoc/>
        public bool DebugEnabled { get; set; }

        /// <inheritdoc/>
        public bool IsOpened => true;

        /// <inheritdoc/>
        public Encoding Encoding { get; set; } = Encoding.ASCII;

        /// <inheritdoc/>
        public bool SendTagWithSyncCommand { get; set; }

        /// <inheritdoc/>
        public int SendTimeout { get; set; }

        /// <inheritdoc/>
        public int ReceiveTimeout { get; set; }

        /// <inheritdoc/>
        public event EventHandler<TikConnectionCommCallbackEventArgs> OnReadRow { add { } remove { } }

        /// <inheritdoc/>
        public event EventHandler<TikConnectionCommCallbackEventArgs> OnWriteRow { add { } remove { } }

        /// <summary>No-op — fake connection is always "open".</summary>
        public void Open(string host, string user, string password) { }

        /// <summary>No-op — fake connection is always "open".</summary>
        public void Open(string host, int port, string user, string password) { }

        /// <summary>No-op — fake connection is always "open".</summary>
        public System.Threading.Tasks.Task OpenAsync(string host, string user, string password)
            => System.Threading.Tasks.Task.CompletedTask;

        /// <summary>No-op — fake connection is always "open".</summary>
        public System.Threading.Tasks.Task OpenAsync(string host, int port, string user, string password)
            => System.Threading.Tasks.Task.CompletedTask;

        /// <summary>No-op.</summary>
        public void Close() { }

        /// <summary>No-op.</summary>
        public void Dispose() { }

        /// <inheritdoc/>
        public ITikCommand CreateCommand()
            => new TikFakeCommand(this);

        /// <inheritdoc/>
        public ITikCommand CreateCommand(TikCommandParameterFormat defaultParameterFormat)
            => new TikFakeCommand(this) { DefaultParameterFormat = defaultParameterFormat };

        /// <inheritdoc/>
        public ITikCommand CreateCommand(string commandText, params ITikCommandParameter[] parameters)
        {
            var cmd = new TikFakeCommand(this) { CommandText = commandText };
            foreach (var p in parameters) cmd.Parameters.Add(p);
            return cmd;
        }

        /// <inheritdoc/>
        public ITikCommand CreateCommand(string commandText, TikCommandParameterFormat defaultParameterFormat, params ITikCommandParameter[] parameters)
        {
            var cmd = new TikFakeCommand(this) { CommandText = commandText, DefaultParameterFormat = defaultParameterFormat };
            foreach (var p in parameters) cmd.Parameters.Add(p);
            return cmd;
        }

        /// <inheritdoc/>
        public ITikCommand CreateCommandAndParameters(string commandText, params string[] parameterNamesAndValues)
            => CreateCommandAndParameters(commandText, TikCommandParameterFormat.NameValue, parameterNamesAndValues);

        /// <inheritdoc/>
        public ITikCommand CreateCommandAndParameters(string commandText, TikCommandParameterFormat defaultParameterFormat, params string[] parameterNamesAndValues)
        {
            var cmd = new TikFakeCommand(this) { CommandText = commandText, DefaultParameterFormat = defaultParameterFormat };
            for (int i = 0; i + 1 < parameterNamesAndValues.Length; i += 2)
                cmd.Parameters.Add(CreateParameter(parameterNamesAndValues[i], parameterNamesAndValues[i + 1], defaultParameterFormat));
            return cmd;
        }

        /// <inheritdoc/>
        public ITikCommandParameter CreateParameter(string name, string value)
            => new TikFakeParameter(name, value, TikCommandParameterFormat.Default);

        /// <inheritdoc/>
        public ITikCommandParameter CreateParameter(string name, string value, TikCommandParameterFormat parameterFormat)
            => new TikFakeParameter(name, value, parameterFormat);

        /// <inheritdoc/>
        public IEnumerable<ITikSentence> CallCommandSync(params string[] commandRows)
            => CallCommandSync((IEnumerable<string>)commandRows);

        /// <summary>
        /// Core intercept point. Looks up the first matching registered handler and returns its sentences.
        /// Throws <see cref="InvalidOperationException"/> if no handler matches — which helps catch
        /// unregistered commands early in tests.
        /// </summary>
        public IEnumerable<ITikSentence> CallCommandSync(IEnumerable<string> commandRows)
        {
            var rows = commandRows.ToArray();
            lock (_sentCommands)
                _sentCommands.Add(rows);

            var handler = _handlers.FirstOrDefault(h => h.Predicate(rows));
            if (handler.Response == null)
                throw new InvalidOperationException(
                    $"TikFakeConnection: no handler registered for command '{rows.FirstOrDefault()}'. " +
                    "Register one via WithResponse / WithEntities / WithScalarResponse / WithNonQuery / WithTrap.");

            return handler.Response(rows).ToList();
        }

        /// <summary>
        /// Async variant — runs the fake sentences on a background thread, calling
        /// <paramref name="oneResponseCallback"/> for each sentence until the thread is cancelled.
        /// </summary>
        public Thread CallCommandAsync(IEnumerable<string> commandRows, string tag, Action<ITikSentence> oneResponseCallback)
        {
            var rows = commandRows.ToArray();
            lock (_sentCommands)
                _sentCommands.Add(rows);

            var handler = _handlers.FirstOrDefault(h => h.Predicate(rows));
            if (handler.Response == null)
                throw new InvalidOperationException(
                    $"TikFakeConnection: no handler registered for async command '{rows.FirstOrDefault()}'. " +
                    "Register one via WithResponse / WithEntities / WithScalarResponse / WithNonQuery / WithTrap.");

            var sentences = handler.Response(rows).ToList();

            var thread = new Thread(() =>
            {
                foreach (var sentence in sentences)
                {
                    bool cancelled;
                    lock (_cancelledTags)
                        cancelled = _cancelledTags.Contains(tag);
                    if (cancelled) break;
                    oneResponseCallback(sentence);
                }
            });
            thread.IsBackground = true;
            thread.Start();
            return thread;
        }
    }
}
