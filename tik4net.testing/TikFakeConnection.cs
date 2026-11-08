using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Objects;

namespace tik4net.Testing
{
    /// <summary>
    /// Fake <see cref="ITikConnection"/> for unit testing — no real router required.
    /// </summary>
    /// <remarks>
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
    /// </remarks>
    /// <example>
    /// <code>
    /// var conn = new TikFakeConnection()
    ///     .WithEntities(new IpAddress { Address = "10.0.0.1/24", Interface = "ether1" }.WithId("*1"))
    ///     .WithScalarResponse(rows => rows.First() == "/ip/address/add", "*2")
    ///     .WithNonQuery(rows => rows.First() == "/ip/address/set");
    ///
    /// var list = conn.LoadAll&lt;IpAddress&gt;();
    /// </code>
    /// </example>
    public sealed class TikFakeConnection : ITikConnection, ITikConnectionCapabilities,
        ITikRawSentenceConnection, ITikSafeModeConnection, ITikTaggedConnection, ITikTlsConnection,
        ITikCancellationModeConnection, ITikMacLayerConnection, tik4net.Cli.ITikCliCompletion,
        ITikApiConnection, ITikRestConnection, ITikCliConnection, ITikMacCliConnection,
        ITikWinboxNativeConnection, ITikWinboxNativeMacConnection
    {
        private readonly List<(Func<IEnumerable<string>, bool> Predicate, Func<IEnumerable<string>, IEnumerable<ITikSentence>> Response)> _handlers
            = new List<(Func<IEnumerable<string>, bool>, Func<IEnumerable<string>, IEnumerable<ITikSentence>>)>();

        private readonly List<string[]> _sentCommands = new List<string[]>();
        private readonly HashSet<string> _cancelledTags = new HashSet<string>();
        private readonly HashSet<string> _runningTags = new HashSet<string>();   // guarded by _cancelledTags
        private volatile bool _isOpened = true;
        private TikConnectionCapability _capabilities =
            TikConnectionCapability.Crud | TikConnectionCapability.Listen
            | TikConnectionCapability.AsyncCommands | TikConnectionCapability.SafeMode
            | TikConnectionCapability.Tagging | TikConnectionCapability.RawCommand;

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
        /// <remarks>See <see cref="WithEntities{TEntity}(Func{IEnumerable{TEntity}})"/> for how the read is evaluated.</remarks>
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
        /// <remarks>
        /// <para>The read is answered as a router answers it: a filter selects the rows (<c>?name=value</c> by the field's
        /// wire text, <c>?name</c> when the field is printed, <c>?-name</c> when it is not; several filters must all hold),
        /// and <c>=.proplist=</c> keeps only the fields it names. A property left unassigned is not printed.</para>
        /// <para>A read this cannot evaluate — the stack operators (<c>?#</c>), <c>?&gt;</c>, <c>?&lt;</c>, or a print
        /// argument that changes the rows (<c>count-only</c>, <c>from</c>, <c>where</c>, …) — throws
        /// <see cref="InvalidOperationException"/> rather than answer it wrongly; script such a read with
        /// <see cref="WithResponse(Func{IEnumerable{string},bool},Func{IEnumerable{string},IEnumerable{ITikSentence}})"/>.</para>
        /// </remarks>
        public TikFakeConnection WithEntities<TEntity>(Func<IEnumerable<TEntity>> entityFactory)
            where TEntity : new()
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            string commandPath = metadata.EntityPath + metadata.LoadCommand;

            return WithResponse(
                predicate: rows => rows.First() == commandPath,
                response: rows =>
                {
                    var read = TikFakeRead.Parse(rows);
                    var sentences = new List<ITikSentence>();
                    foreach (var entity in entityFactory())
                    {
                        // entity comes from the caller's factory for TEntity : new() — always a real
                        // instance here, but the unconstrained generic parameter reads as possibly-null.
                        var words = metadata.Properties
                            // A null value is a field the router does not print, so the fake leaves the word out
                            // rather than printing it empty — that is what an entity reading it back must see.
                            .Select(p => new { p.FieldName, Value = p.GetEntityValue(entity!) })
                            .Where(p => p.Value != null)
                            .ToDictionary(p => p.FieldName, p => p.Value!);
                        if (read.Matches(words))
                            sentences.Add(new TikFakeReSentence(read.Project(words)));
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
        /// <remarks>
        /// The <c>Execute*</c> methods throw what a transport throws for the same <paramref name="message"/>:
        /// <see cref="TikNoSuchItemException"/>, <see cref="TikNoSuchCommandException"/>,
        /// <see cref="TikAlreadyHaveSuchItemException"/>, or the base <see cref="TikCommandTrapException"/> for any other text.
        /// <c>ExecuteWithCallback</c> / <c>LoadWithCallback</c> report it to the error callback and, as on every transport,
        /// do not call <c>onDone</c> after it.
        /// </remarks>
        /// <param name="predicate">Matches the command rows to trap.</param>
        /// <param name="message">Error message (e.g. "no such item", "already have such item").</param>
        /// <param name="categoryCode">Optional MikroTik error category code.</param>
        public TikFakeConnection WithTrap(
            Func<IEnumerable<string>, bool> predicate,
            string message,
            string? categoryCode = null)
        {
            return WithResponse(predicate, _ => new ITikSentence[]
            {
                new TikFakeTrapSentence(message, categoryCode),
                new TikFakeDoneSentence()
            });
        }

        /// <summary>
        /// Registers a handler that answers with a lost connection (<c>!fatal</c>), as a router does after
        /// <c>/system/reboot</c> or when the link drops. See <see cref="TikFakeFatalSentence"/> for what each command makes of it.
        /// </summary>
        /// <param name="predicate">Matches the command rows to answer so.</param>
        /// <param name="message">Why the session ended.</param>
        public TikFakeConnection WithFatal(Func<IEnumerable<string>, bool> predicate, string message)
            => WithResponse(predicate, _ => new ITikSentence[] { new TikFakeFatalSentence(message) });

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
            {
                _cancelledTags.Add(tag);
                Monitor.PulseAll(_cancelledTags);
            }
        }

        // ── Capabilities ───────────────────────────────────────────────────────

        /// <summary>
        /// What this fake claims to support; by default what it actually implements (<see cref="TikConnectionCapability.Crud"/>,
        /// <see cref="TikConnectionCapability.Listen"/>, <see cref="TikConnectionCapability.AsyncCommands"/>,
        /// <see cref="TikConnectionCapability.SafeMode"/>, <see cref="TikConnectionCapability.Tagging"/> and
        /// <see cref="TikConnectionCapability.RawCommand"/>).
        /// </summary>
        /// <remarks>
        /// <para>
        /// In the default set, <see cref="TikConnectionCapability.Listen"/> is the callback
        /// <c>ExecuteWithCallback</c>/<c>LoadWithCallback</c> pattern, <see cref="TikConnectionCapability.AsyncCommands"/>
        /// the <c>Execute*Async</c> surface, completing synchronously (see <see cref="TikFakeCommand"/>), and
        /// <see cref="TikConnectionCapability.SafeMode"/> is recorded, not performed (the counters below).
        /// </para>
        /// <para>
        /// Set it to test how your code behaves against a transport that lacks something — clearing
        /// <see cref="TikConnectionCapability.AsyncCommands"/>, for instance, makes <c>ExecuteListAsync</c> throw
        /// <see cref="TikConnectionCapabilityNotSupportedException"/> here exactly as it would over a CLI
        /// transport. It is deliberately <b>not</b> the full flag set: a fake that claims everything cannot be
        /// used to test the fail-closed branches.
        /// </para>
        /// <para>
        /// <see cref="TikConnectionCapability.SafeMode"/>, <see cref="TikConnectionCapability.Tagging"/> and
        /// <see cref="TikConnectionCapability.RawCommand"/> are in the default set because this fake implements
        /// <see cref="ITikSafeModeConnection"/>, <see cref="ITikTaggedConnection"/> and
        /// <see cref="ITikRawSentenceConnection"/> functionally (its <c>CallCommandSync</c> and <c>CreateRawCommand</c>
        /// answer from the registered handlers like any other command), and a connection whose flags disagreed with
        /// the interfaces it implements would answer the same question two ways. Clear a flag to test the branch
        /// where a transport lacks that feature.
        /// </para>
        /// <para>
        /// <see cref="TikConnectionCapability.Streaming"/> is not in the default set. Add it on the net8.0 build, where the
        /// fake's commands implement <c>ITikStreamingCommand</c>: the stream yields the scripted rows, then ends as the binary
        /// API's does.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentException">The value holds <see cref="TikConnectionCapability.MenuSchema"/>: the fake
        /// cannot describe a menu, and a flag it could not back would fail later with an exception no transport throws.</exception>
        public TikConnectionCapability Capabilities
        {
            get => _capabilities;
            set
            {
                if ((value & TikConnectionCapability.MenuSchema) != 0)
                    throw new ArgumentException("TikFakeConnection cannot describe a menu, so it cannot declare "
                        + "TikConnectionCapability.MenuSchema.", nameof(value));
                _capabilities = value;
            }
        }

        // ── ITikConnection ─────────────────────────────────────────────────────

        /// <inheritdoc/>
        public bool DebugEnabled { get; set; }

        /// <summary>True from creation, after <c>Open</c>, and until <see cref="Close"/> or <see cref="Dispose"/>.</summary>
        /// <remarks>A command on a closed fake throws <see cref="TikConnectionNotOpenException"/>, as on every transport.</remarks>
        public bool IsOpened => _isOpened;

        /// <inheritdoc/>
        /// <remarks>Recorded, not used: the fake has no bytes to encode. UTF-8 by default, as on every transport.</remarks>
        public Encoding Encoding { get; set; } = Encoding.UTF8;

        /// <inheritdoc/>
        public TimeSpan SendTimeout { get; set; }

        /// <inheritdoc/>
        public TimeSpan ReceiveTimeout { get; set; }

        /// <inheritdoc/>
        /// <remarks>Accepted and ignored — the fake connection reaches no router to be slow.</remarks>
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>Fires for each word of an answer, as the binary API reads it: the sentence type (<c>!re</c>, <c>!done</c>,
        /// …), then each field as <c>=name=value</c>.</summary>
        public event EventHandler<TikConnectionCommCallbackEventArgs>? OnReadRow;

        /// <summary>Fires for each row a command sends.</summary>
        public event EventHandler<TikConnectionCommCallbackEventArgs>? OnWriteRow;

        /// <summary>Opens the fake again after <see cref="Close"/>; it reaches no router.</summary>
        public void Open(string host, string user, string password) => _isOpened = true;

        /// <summary>Opens the fake again after <see cref="Close"/>; it reaches no router.</summary>
        public void Open(string host, int port, string user, string password) => _isOpened = true;

        /// <summary>Opens the fake again after <see cref="Close"/> — and an already-cancelled token throws.</summary>
        /// <remarks>
        /// The cancellation check is not ceremony: a double that ignored the token would let a test of
        /// "cancel the open" pass while the real connection throws, which is the one thing a test double must
        /// never do.
        /// </remarks>
        public System.Threading.Tasks.Task OpenAsync(string host, string user, string password,
            System.Threading.CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _isOpened = true;
            return System.Threading.Tasks.Task.CompletedTask;
        }

        /// <inheritdoc cref="OpenAsync(string, string, string, System.Threading.CancellationToken)"/>
        public System.Threading.Tasks.Task OpenAsync(string host, int port, string user, string password,
            System.Threading.CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _isOpened = true;
            return System.Threading.Tasks.Task.CompletedTask;
        }

        /// <summary>
        /// Closes the fake: later commands throw <see cref="TikConnectionNotOpenException"/>, and a running
        /// <c>ExecuteWithCallback</c> ends in its <c>onDone</c>, as the caller's own close ends it on every transport.
        /// </summary>
        public void Close()
        {
            _isOpened = false;
            string[] running;
            lock (_cancelledTags)
                running = _runningTags.ToArray();
            foreach (var tag in running)
                CancelTag(tag);
        }

        /// <summary>Number of times <see cref="SafeModeTake"/> was called (for test assertions).</summary>
        public int SafeModeTakeCount { get; private set; }

        /// <summary>Number of times <see cref="SafeModeRelease"/> was called (for test assertions).</summary>
        public int SafeModeReleaseCount { get; private set; }

        /// <summary>Number of times <see cref="SafeModeUnroll"/> was called (for test assertions).</summary>
        public int SafeModeUnrollCount { get; private set; }

        /// <summary>True between a <see cref="SafeModeTake"/> and its matching release/unroll.</summary>
        public bool SafeModeActive { get; private set; }

        /// <summary>Records a safe-mode take. No router involved — just bookkeeping for tests.</summary>
        public void SafeModeTake() { SafeModeTakeCount++; SafeModeActive = true; }

        /// <summary>Records a safe-mode release (commit).</summary>
        public void SafeModeRelease() { SafeModeReleaseCount++; SafeModeActive = false; }

        /// <summary>Records a safe-mode unroll (rollback).</summary>
        public void SafeModeUnroll() { SafeModeUnrollCount++; SafeModeActive = false; }

        /// <summary>Returns whether safe mode is currently held (client-side bookkeeping).</summary>
        public bool SafeModeGet() => SafeModeActive;

        /// <summary>Closes the fake, as <see cref="Close"/>.</summary>
        public void Dispose() => Close();

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
            => CreateCommandAndParameters(commandText, TikCommandParameterFormat.Default, parameterNamesAndValues);

        /// <inheritdoc/>
        public ITikCommand CreateCommandAndParameters(string commandText, TikCommandParameterFormat defaultParameterFormat, params string[] parameterNamesAndValues)
        {
            Guard.NamesAndValuesArePaired(parameterNamesAndValues, nameof(parameterNamesAndValues));
            // The parameters are created unformatted and resolved when the command runs, as on every transport.
            var cmd = new TikFakeCommand(this) { CommandText = commandText, DefaultParameterFormat = defaultParameterFormat };
            for (int i = 0; i + 1 < parameterNamesAndValues.Length; i += 2)
                cmd.Parameters.Add(CreateParameter(parameterNamesAndValues[i], parameterNamesAndValues[i + 1]));
            return cmd;
        }

        /// <inheritdoc/>
        public ITikCommandParameter CreateParameter(string name, string? value)
            => new TikFakeParameter(name, value, TikCommandParameterFormat.Default);

        /// <inheritdoc/>
        public ITikCommandParameter CreateParameter(string name, string? value, TikCommandParameterFormat parameterFormat)
            => new TikFakeParameter(name, value, parameterFormat);

        /// <inheritdoc/>
        public IEnumerable<ITikSentence> CallCommandSync(params string[] commandRows)
            => CallCommandSync((IEnumerable<string>)commandRows);

        /// <summary>
        /// Core intercept point. Looks up the first matching registered handler and returns its sentences.
        /// Throws <see cref="InvalidOperationException"/> if no handler matches — which helps catch
        /// unregistered commands early in tests.
        /// </summary>
        /// <exception cref="TikConnectionNotOpenException">The fake was closed.</exception>
        public IEnumerable<ITikSentence> CallCommandSync(IEnumerable<string> commandRows)
            => Dispatch(commandRows.ToArray());

        // Every command goes through here, the commands' own and the raw ones: the open check, the record, the row events,
        // and the first handler that matches.
        internal IList<ITikSentence> Dispatch(string[] rows)
        {
            if (!_isOpened)
                throw new TikConnectionNotOpenException("Fake connection is not open.");
            lock (_sentCommands)
                _sentCommands.Add(rows);
            var write = OnWriteRow;
            if (write != null)
                foreach (var row in rows)
                    write(this, new TikConnectionCommCallbackEventArgs(row));

            var handler = _handlers.FirstOrDefault(h => h.Predicate(rows));
            if (handler.Response == null)
                throw new InvalidOperationException(
                    $"TikFakeConnection: no handler registered for command '{rows.FirstOrDefault()}'. " +
                    "Register one via WithResponse / WithEntities / WithScalarResponse / WithNonQuery / WithTrap / WithFatal.");
            var answer = handler.Response(rows).ToList();

            var read = OnReadRow;
            if (read != null)
                foreach (var sentence in answer)
                    foreach (var word in WordsOf(sentence))
                        read(this, new TikConnectionCommCallbackEventArgs(word));
            return answer;
        }

        private static IEnumerable<string> WordsOf(ITikSentence sentence)
        {
            yield return sentence is ITikReSentence ? "!re"
                : sentence is ITikDoneSentence ? "!done"
                : sentence is ITikTrapSentence ? "!trap"
                : sentence is TikFakeFatalSentence ? "!fatal"
                : "!" + sentence.GetType().Name;
            foreach (var word in sentence.Words)
                yield return "=" + word.Key + "=" + word.Value;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The fake answers from memory, so there is nothing to await — but it still has to OFFER the
        /// awaitable shape, or a consumer's <c>await conn.CallCommandAsync(...)</c> could not be tested
        /// against it, which is the whole point of this class. The token is honoured before the answer is
        /// produced, matching the one guarantee every real transport makes.
        /// </remarks>
        public Task<IList<ITikSentence>> CallCommandAsync(string[] commandRows,
            CancellationToken cancellationToken = default(CancellationToken))
            => CallCommandAsync((IEnumerable<string>)commandRows, cancellationToken);

        /// <inheritdoc/>
        public Task<IList<ITikSentence>> CallCommandAsync(IEnumerable<string> commandRows,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IList<ITikSentence>>(CallCommandSync(commandRows).ToList());
        }

        // The callback form: the answer is delivered on a background thread, one sentence at a time. A command that has not
        // ended (no !done or !fatal delivered: cancelled part-way, or a script that stops short of one, as a running monitor
        // does) runs until it is cancelled or the connection closed, and then ends in a !done, as the binary API ends a
        // cancelled command. TikFakeCommand.ExecuteWithCallback is the one caller.
        internal Thread CallCommandAsync(IEnumerable<string> commandRows, string tag, Action<ITikSentence> oneResponseCallback)
        {
            var sentences = Dispatch(commandRows.ToArray());
            lock (_cancelledTags)
                _runningTags.Add(tag);

            var thread = new Thread(() =>
            {
                try
                {
                    foreach (var sentence in sentences)
                    {
                        bool cancelled;
                        lock (_cancelledTags)
                            cancelled = _cancelledTags.Contains(tag);
                        if (cancelled) break;
                        oneResponseCallback(sentence);
                        if (sentence is ITikDoneSentence || sentence is TikFakeFatalSentence)
                            return;
                    }

                    lock (_cancelledTags)
                        while (!_cancelledTags.Contains(tag))
                            Monitor.Wait(_cancelledTags);
                    oneResponseCallback(new TikFakeDoneSentence());
                }
                finally
                {
                    lock (_cancelledTags)
                    {
                        _runningTags.Remove(tag);
                        _cancelledTags.Remove(tag);
                    }
                }
            });
            thread.IsBackground = true;
            thread.Start();
            return thread;
        }

        // ── The option facets, so the fake satisfies the composite connection interfaces ──────
        //
        // The wiki tells people to prefer a typed connection - `ITikCliConnection cli =
        // setup.CreateTelnetConnection()` - over a runtime Supports() check, because it turns a missing
        // feature into a compile error. A method taking ITikApiConnection or ITikCliConnection needs a double
        // it can be given, so the fake implements every facet and every composite. It is not claiming to BE an
        // API or a CLI connection - a double stands in for whatever the code under test asked for, and the
        // capability set (Capabilities, settable per test) is still the thing that answers "can it".

        /// <summary>Per-command tagging. Settable; the fake does not act on it.</summary>
        public bool SendTagWithSyncCommand { get; set; } = true;

        /// <summary>TLS option. Settable so a test can assert configuration; no TLS is involved.</summary>
        public bool AllowInvalidCertificate { get; set; }

        /// <summary>TLS option. Settable so a test can assert configuration; no TLS is involved.</summary>
        public RemoteCertificateValidationCallback? CertificateValidationCallback { get; set; }

        /// <summary>What a mid-command cancel costs on a terminal. Settable; the fake has no terminal.</summary>
        public TikCancellationMode CancellationMode { get; set; } = TikCancellationMode.Cooperative;

        /// <summary>Router MAC for the MAC-layer transports. Settable; the fake reaches no router.</summary>
        public string? RouterMac { get; set; }

        /// <summary>
        /// Tab-completion answers this fake will give, keyed by the partial input. A test that exercises
        /// completion scripts it here; anything unscripted completes to nothing rather than throwing,
        /// because "the router offered no completion" is a real answer and not an error.
        /// </summary>
        public readonly Dictionary<string, IReadOnlyList<string>> Completions
            = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        /// <inheritdoc/>
        public IReadOnlyList<string> CompleteCli(string partialInput)
            => Completions.TryGetValue(partialInput ?? string.Empty, out var tokens)
                ? tokens
                : new string[0];

        /// <inheritdoc/>
        public string CompleteCliRaw(string partialInput)
            => string.Join(" ", CompleteCli(partialInput));

        /// <summary>Address WinBox-native fields by their GUI label. Settable; the fake maps nothing.</summary>
        public bool UseGuiNames { get; set; }

        /// <summary>
        /// How many handlers the WinBox `.jg` catalog loaded. Zero on a real connection means silent
        /// degradation to a seed table, so a test asserting on that path can script it here.
        /// </summary>
        public int CatalogHandlerCount { get; set; }

        /// <summary>The WinBox-native path aliases set on this fake (<c>apiPath → winboxMenuPath</c>); the fake maps nothing with them.</summary>
        public IDictionary<string, string> PathAliases { get; } = new Dictionary<string, string>();

        /// <summary>The WinBox-native path overrides set on this fake (<c>apiPath → handler</c>); the fake maps nothing with them.</summary>
        public IDictionary<string, int[]> PathOverrides { get; } = new Dictionary<string, int[]>();

        /// <summary>The WinBox-native field overrides set on this fake (<c>(apiPath, apiName) → key</c>); the fake maps nothing with them.</summary>
        public IDictionary<(string ApiPath, string ApiName), int> FieldOverrides { get; } = new Dictionary<(string, string), int>();

        /// <summary>Records the alias in <see cref="PathAliases"/>, so a test can assert what code under test configured.</summary>
        public void PathAlias(string apiPath, string winboxMenuPath) => PathAliases[apiPath] = winboxMenuPath;

        /// <summary>Records the override in <see cref="PathOverrides"/>.</summary>
        public void PathOverride(string apiPath, int[] handler) => PathOverrides[apiPath] = handler;

        /// <summary>Records the override in <see cref="FieldOverrides"/>.</summary>
        public void FieldOverride(string apiPath, string apiName, int key) => FieldOverrides[(apiPath, apiName)] = key;
    }
}
