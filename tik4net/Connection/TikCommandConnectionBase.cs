using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.Connection
{
    /// <summary>
    /// Transport-neutral base class for RouterOS command-style connections that expose CRUD through
    /// the four <c>Run*</c> hooks (instead of the binary API sentence protocol). It implements the full
    /// <see cref="ITikConnection"/> surface — command factory, diagnostics and lifecycle — and serialises
    /// commands through a <see cref="SemaphoreSlim"/>.
    ///
    /// It deliberately does <b>not</b> implement <see cref="ITikRawSentenceConnection"/>. That interface's
    /// contract is a command in the <i>connection-specific</i> format, and this class knows only the
    /// transport-neutral one; a base-class implementation could only accept API-shaped rows and translate
    /// them, which is what the O/R mapper already does and is the opposite of a raw call. Transports with a
    /// native dialect of their own implement it themselves — see <c>CliConnectionBase</c>.
    ///
    /// Concrete subclasses provide the transport (CLI terminal, native WinBox M2, …) by implementing:
    /// <list type="bullet">
    ///   <item><see cref="Open(string, string, string)"/> / <see cref="Open(string, int, string, string)"/></item>
    ///   <item><see cref="OpenAsync(string, string, string, CancellationToken)"/> /
    ///       <see cref="OpenAsync(string, int, string, string, CancellationToken)"/></item>
    ///   <item><see cref="Close"/></item>
    ///   <item>the three CRUD hooks <see cref="RunPrint"/>, <see cref="RunAdd"/>, <see cref="RunNonQuery"/>.</item>
    /// </list>
    /// <para>
    /// <b>This is a real extension point, not just the shape the in-tree transports happen to share.</b> The
    /// hooks are <c>protected</c>, so a transport can be written outside this assembly: implement the three,
    /// declare what it can do in <see cref="Capabilities"/>, and register it through
    /// <c>ConnectionFactory.RegisterConnectionFactory</c> — which is exactly how the <c>tik4net.ssh</c>
    /// satellite package plugs in. The <c>Run*Async</c> siblings are optional: their defaults throw rather
    /// than wrapping the synchronous hook in a <c>Task.Run</c> façade, so a transport that cannot genuinely
    /// await its I/O simply does not declare
    /// <see cref="TikConnectionCapability.AsyncCommands"/>. Only <see cref="ITikRawSentenceConnection"/> is
    /// left out on purpose — see below.
    /// </para>
    /// </summary>
    public abstract class TikCommandConnectionBase : ITikConnection, ITikConnectionCapabilities
    {
        /// <summary>Serialises command execution — the underlying transports are inherently sequential.</summary>
        protected readonly SemaphoreSlim _cmdLock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// The exception for "the connection was closed while this command was still running".
        /// </summary>
        /// <remarks>
        /// <c>Close</c> does not wait for an in-flight command — it must stay prompt, and a caller closing a
        /// connection to escape a stuck one would be defeated by a Close that blocked for
        /// <see cref="ReceiveTimeout"/>. So the command loses its socket mid-flight, and the question is only
        /// what it is told. Left alone it surfaces whatever the framework threw — an
        /// <see cref="ObjectDisposedException"/> or a raw <see cref="System.IO.IOException"/> — which is
        /// outside the tik4net hierarchy and reads like a bug in the library rather than a race the caller
        /// started.
        /// <para>
        /// The message deliberately does <b>not</b> say the command did not run. Nobody here knows: the
        /// bytes may have reached the router before the socket went. Claiming otherwise is the kind of
        /// confident wrong answer that costs somebody a duplicated write.
        /// </para>
        /// </remarks>
        /// <param name="inner">Whatever the torn-down transport threw.</param>
        protected TikConnectionNotOpenException ClosedWhileRunning(Exception inner)
            => new TikConnectionNotOpenException(
                "The connection was closed while this command was in flight. Whether the router received "
                + "and executed it is not known — Close does not wait for a running command. Close from the "
                + "thread that owns the connection, or let the command finish first.", inner);
        private bool _isOpened;

        // ── ITikConnection properties ─────────────────────────────────────────

        /// <inheritdoc/>
        public bool DebugEnabled { get; set; }

        /// <inheritdoc/>
        public bool IsOpened => _isOpened;

        /// <inheritdoc/>
        public Encoding Encoding { get; set; } = Encoding.UTF8;

        /// <inheritdoc/>
        public int SendTimeout { get; set; } = 30000;

        /// <inheritdoc/>
        public int ReceiveTimeout { get; set; } = 30000;

        /// <inheritdoc/>
        public int ConnectTimeout { get; set; } = 15000;

        /// <inheritdoc/>
        public event EventHandler<TikConnectionCommCallbackEventArgs>? OnReadRow;

        /// <inheritdoc/>
        public event EventHandler<TikConnectionCommCallbackEventArgs>? OnWriteRow;

        /// <summary>
        /// Optional callback for low-level transport diagnostics (raw packets, protocol events).
        /// Intended for test instrumentation and debugging — not for production use.
        /// The string format is transport-specific (e.g. "[pkt] type=1 paylen=42").
        /// Set to <c>null</c> (default) to disable.
        /// </summary>
        public Action<string>? TransportDiagnostic { get; set; }

        // ── Capabilities ──────────────────────────────────────────────────────

        /// <inheritdoc/>
        public virtual TikConnectionCapability Capabilities => TikConnectionCapability.Crud;

        // ── Transport — subclass contract ─────────────────────────────────────

        /// <inheritdoc/>
        public abstract void Open(string host, string user, string password);

        /// <inheritdoc/>
        public abstract void Open(string host, int port, string user, string password);

        /// <inheritdoc/>
        public abstract Task OpenAsync(string host, string user, string password,
            CancellationToken cancellationToken = default);

        /// <inheritdoc/>
        public abstract Task OpenAsync(string host, int port, string user, string password,
            CancellationToken cancellationToken = default);

        /// <inheritdoc/>
        public abstract void Close();

        /// <summary>
        /// Tracks whether this connection currently holds Safe Mode. Maintained and reported by the
        /// subclasses that implement <see cref="ITikSafeModeConnection"/> (the CLI terminals and native
        /// WinBox M2); it lives here because the bookkeeping is identical wherever safe mode exists, while
        /// the interface deliberately does not — REST cannot bind a rollback to a connection it does not
        /// keep, so it implements neither the interface nor a set of methods that only throw.
        /// </summary>
        protected bool SafeModeHeld { get; set; }

        // ── Open/Close helpers for subclasses ─────────────────────────────────

        /// <summary>
        /// Subclasses must call this after a successful login to mark the connection as open.
        /// </summary>
        protected void SetOpened() => _isOpened = true;

        /// <summary>
        /// Subclasses must call this when closing or on a fatal error to mark the connection as closed.
        /// </summary>
        protected void SetClosed() => _isOpened = false;

        // ── CRUD hooks — subclass contract ────────────────────────────────────

        /// <summary>
        /// Executes a read (<c>print</c>) command and returns the matching records.
        /// </summary>
        protected abstract IList<TikRecordSentence> RunPrint(TikCommandDescriptor descriptor);

        /// <summary>
        /// Executes an <c>add</c> command and returns the new record's <c>.id</c>.
        /// </summary>
        protected abstract string RunAdd(TikCommandDescriptor descriptor);

        /// <summary>
        /// Executes a non-query command (set, remove, enable, disable, move, unset, reboot, …).
        /// </summary>
        protected abstract void RunNonQuery(TikCommandDescriptor descriptor);

        /// <summary>
        /// Sends a <b>raw</b> pass-through payload (<see cref="TikCommandDescriptor.CommandText"/>) verbatim in the
        /// transport's dialect and returns the cleaned response text — used by <c>ExecuteScalar</c>/
        /// <c>ExecuteNonQuery</c> on a raw command (see <c>CreateRawCommand</c>). The default throws; transports
        /// that declare <see cref="TikConnectionCapability.RawCommand"/> override it. (For raw <c>ExecuteList</c>,
        /// <see cref="RunPrint"/> handles the <see cref="TikCommandDescriptor.IsRaw"/> descriptor itself.)
        /// </summary>
        protected virtual string RunRawText(TikCommandDescriptor descriptor)
            => throw new TikConnectionCapabilityNotSupportedException(TikConnectionCapability.RawCommand,
                "This transport does not support raw command pass-through (CreateRawCommand).");

        // ── Async CRUD hooks — subclass contract ──────────────────────────────
        //
        // The Task-based siblings of the four hooks above, driving ITikCommandAsync on TikGenericCommand.
        // The defaults throw rather than wrapping the synchronous hook in a Task: a Task.Run façade would report
        // "async" for a transport that still blocks a thread per command, which is exactly what the capability
        // model exists to prevent. A transport implements these by awaiting its own I/O and then declares
        // TikConnectionCapability.AsyncCommands — the two go together, and neither is useful alone.

        private const string AsyncUnsupported =
            "This transport has no Task-based command implementation yet. Use the synchronous Execute* methods, or "
            + "a transport that reports the 'AsyncCommands' capability.";

        /// <summary>Async <see cref="RunPrint"/>. Default: not supported (see the note on the async hooks).</summary>
        protected virtual Task<IList<TikRecordSentence>> RunPrintAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
            => throw new TikConnectionCapabilityNotSupportedException(TikConnectionCapability.AsyncCommands, AsyncUnsupported);

        /// <summary>Async <see cref="RunAdd"/>. Default: not supported (see the note on the async hooks).</summary>
        protected virtual Task<string> RunAddAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
            => throw new TikConnectionCapabilityNotSupportedException(TikConnectionCapability.AsyncCommands, AsyncUnsupported);

        /// <summary>Async <see cref="RunNonQuery"/>. Default: not supported (see the note on the async hooks).</summary>
        protected virtual Task RunNonQueryAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
            => throw new TikConnectionCapabilityNotSupportedException(TikConnectionCapability.AsyncCommands, AsyncUnsupported);

        /// <summary>Async <see cref="RunRawText"/>. Default: not supported (see the note on the async hooks).</summary>
        protected virtual Task<string> RunRawTextAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
            => throw new TikConnectionCapabilityNotSupportedException(TikConnectionCapability.AsyncCommands, AsyncUnsupported);

        // ── ITikConnection — Command factory ──────────────────────────────────

        /// <inheritdoc/>
        public ITikCommand CreateCommand()
            => new TikGenericCommand(this);

        /// <inheritdoc/>
        public ITikCommand CreateCommand(TikCommandParameterFormat defaultParameterFormat)
            => new TikGenericCommand(this, defaultParameterFormat);

        /// <inheritdoc/>
        public ITikCommand CreateCommand(string commandText, params ITikCommandParameter[] parameters)
            => new TikGenericCommand(this, commandText, parameters);

        /// <inheritdoc/>
        public ITikCommand CreateCommand(string commandText, TikCommandParameterFormat defaultParameterFormat, params ITikCommandParameter[] parameters)
            => new TikGenericCommand(this, commandText, defaultParameterFormat, parameters);

        /// <inheritdoc/>
        public ITikCommand CreateCommandAndParameters(string commandText, params string[] parameterNamesAndValues)
        {
            var cmd = new TikGenericCommand(this, commandText);
            cmd.AddParameterAndValues(parameterNamesAndValues);
            return cmd;
        }

        /// <inheritdoc/>
        public ITikCommand CreateCommandAndParameters(string commandText, TikCommandParameterFormat defaultParameterFormat, params string[] parameterNamesAndValues)
        {
            var cmd = new TikGenericCommand(this, commandText, defaultParameterFormat);
            cmd.AddParameterAndValues(parameterNamesAndValues);
            return cmd;
        }

        /// <inheritdoc/>
        public ITikCommandParameter CreateParameter(string name, string value)
            => new TikCommandParameter(name, value);

        /// <inheritdoc/>
        public ITikCommandParameter CreateParameter(string name, string value, TikCommandParameterFormat parameterFormat)
            => new TikCommandParameter(name, value, parameterFormat);

        // ── Internal dispatch ─────────────────────────────────────────────────
        //
        // The hooks above are protected: they are the extension point a transport implements, and a
        // transport is the only thing that should be able to define them. But TikGenericCommand — a
        // different class — is what CALLS them, and protected does not reach across classes. These shims
        // are that one bridge, internal so they stay inside the assembly, and nothing more than a
        // forwarding call. Adding a hook means adding its shim beside it.

        internal IList<TikRecordSentence> InvokeRunPrint(TikCommandDescriptor descriptor) => RunPrint(descriptor);

        internal string InvokeRunAdd(TikCommandDescriptor descriptor) => RunAdd(descriptor);

        internal void InvokeRunNonQuery(TikCommandDescriptor descriptor) => RunNonQuery(descriptor);

        internal string InvokeRunRawText(TikCommandDescriptor descriptor) => RunRawText(descriptor);

        internal Task<IList<TikRecordSentence>> InvokeRunPrintAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
            => RunPrintAsync(descriptor, cancellationToken);

        internal Task<string> InvokeRunAddAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
            => RunAddAsync(descriptor, cancellationToken);

        internal Task InvokeRunNonQueryAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
            => RunNonQueryAsync(descriptor, cancellationToken);

        internal Task<string> InvokeRunRawTextAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
            => RunRawTextAsync(descriptor, cancellationToken);

        // ── IDisposable ────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public void Dispose() => Close();

        // ── Diagnostics ────────────────────────────────────────────────────────

        /// <summary>
        /// True when row-level tracing would be observed by someone — either <see cref="DebugEnabled"/>
        /// is set, or a <see cref="OnReadRow"/>/<see cref="OnWriteRow"/> handler is attached. Subclasses
        /// can gate the (potentially costly) rendering of a trace word behind this so it is only built
        /// when something is actually listening.
        /// </summary>
        protected bool RowTracingEnabled => DebugEnabled || OnReadRow != null || OnWriteRow != null;

        /// <summary>Short tag prefixing <see cref="DebugEnabled"/> trace lines (e.g. <c>CLI&gt;&gt;</c>).
        /// Transports override it so the debug output names the right channel (CLI / REST / …).</summary>
        protected virtual string DiagnosticPrefix => "CLI";

        /// <summary>Fires <see cref="OnWriteRow"/> and writes a debug line when <see cref="DebugEnabled"/>.</summary>
        protected void FireWriteRow(string word)
        {
            OnWriteRow?.Invoke(this, new TikConnectionCommCallbackEventArgs(word));
            if (DebugEnabled)
                System.Diagnostics.Debug.WriteLine(DiagnosticPrefix + ">> " + word);
        }

        /// <summary>Fires <see cref="OnReadRow"/> and writes a (truncated) debug line when <see cref="DebugEnabled"/>.</summary>
        protected void FireReadRow(string word)
        {
            OnReadRow?.Invoke(this, new TikConnectionCommCallbackEventArgs(word));
            if (DebugEnabled)
                System.Diagnostics.Debug.WriteLine(DiagnosticPrefix + "<< " + (word != null && word.Length > 200 ? word.Substring(0, 200) + "..." : word));
        }

        // ── Private helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Throws <see cref="TikConnectionNotOpenException"/> when the connection has not been opened.
        /// </summary>
        protected void EnsureOpened()
        {
            if (!_isOpened)
                throw new TikConnectionNotOpenException("Connection is not open.");
        }


        /// <summary>
        /// Creates a minimal command object for use in exception constructors when the original
        /// command is not available (e.g. in CallCommandSync / RunNonQuery paths).
        /// </summary>
        internal ITikCommand CreateDummyCommand(TikCommandDescriptor descriptor)
        {
            var cmd = new TikGenericCommand(this, descriptor.CommandText);
            foreach (var p in descriptor.Parameters)
                cmd.AddParameter(p.Name, p.Value, p.ParameterFormat);
            return cmd;
        }
    }
}
