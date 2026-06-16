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
    /// <see cref="ITikConnection"/> surface — command factory, low-level <see cref="CallCommandSync(string[])"/>
    /// dispatch, diagnostics and lifecycle — and serialises commands through a <see cref="SemaphoreSlim"/>.
    ///
    /// Concrete subclasses provide the transport (CLI terminal, native WinBox M2, …) by implementing:
    /// <list type="bullet">
    ///   <item><see cref="Open(string, string, string)"/> / <see cref="Open(string, int, string, string)"/></item>
    ///   <item><see cref="OpenAsync(string, string, string)"/> / <see cref="OpenAsync(string, int, string, string)"/></item>
    ///   <item><see cref="Close"/></item>
    ///   <item>the three CRUD hooks <see cref="RunPrint"/>, <see cref="RunAdd"/>, <see cref="RunNonQuery"/>.</item>
    /// </list>
    /// </summary>
    public abstract class TikCommandConnectionBase : ITikConnection, ITikConnectionCapabilities
    {
        /// <summary>Serialises command execution — the underlying transports are inherently sequential.</summary>
        protected readonly SemaphoreSlim _cmdLock = new SemaphoreSlim(1, 1);
        private bool _isOpened;

        // ── ITikConnection properties ─────────────────────────────────────────

        /// <inheritdoc/>
        public bool DebugEnabled { get; set; }

        /// <inheritdoc/>
        public bool IsOpened => _isOpened;

        /// <inheritdoc/>
        public Encoding Encoding { get; set; } = Encoding.UTF8;

        /// <summary>No-op for command transports (no tag protocol).</summary>
        public bool SendTagWithSyncCommand { get; set; }

        /// <inheritdoc/>
        public int SendTimeout { get; set; } = 30000;

        /// <inheritdoc/>
        public int ReceiveTimeout { get; set; } = 30000;

        /// <inheritdoc/>
        public event EventHandler<TikConnectionCommCallbackEventArgs> OnReadRow;

        /// <inheritdoc/>
        public event EventHandler<TikConnectionCommCallbackEventArgs> OnWriteRow;

        /// <summary>
        /// Optional callback for low-level transport diagnostics (raw packets, protocol events).
        /// Intended for test instrumentation and debugging — not for production use.
        /// The string format is transport-specific (e.g. "[pkt] type=1 paylen=42").
        /// Set to <c>null</c> (default) to disable.
        /// </summary>
        public Action<string> TransportDiagnostic { get; set; }

        // ── Capabilities ──────────────────────────────────────────────────────

        /// <inheritdoc/>
        public virtual TikConnectionCapability Capabilities => TikConnectionCapability.Crud;

        // ── Transport — subclass contract ─────────────────────────────────────

        /// <inheritdoc/>
        public abstract void Open(string host, string user, string password);

        /// <inheritdoc/>
        public abstract void Open(string host, int port, string user, string password);

        /// <inheritdoc/>
        public abstract Task OpenAsync(string host, string user, string password);

        /// <inheritdoc/>
        public abstract Task OpenAsync(string host, int port, string user, string password);

        /// <inheritdoc/>
        public abstract void Close();

        /// <summary>
        /// Tracks whether this connection currently holds Safe Mode. Maintained by the transport-specific
        /// <see cref="SafeModeTake"/>/<see cref="SafeModeRelease"/>/<see cref="SafeModeUnroll"/> overrides and
        /// reported by <see cref="SafeModeGet"/>.
        /// </summary>
        protected bool SafeModeHeld { get; set; }

        private const string SafeModeUnsupported =
            "This transport does not support Safe Mode. Use the binary API, a CLI transport " +
            "(Telnet / MAC-Telnet / WinBox CLI) or native WinBox, which can bind safe mode to a session.";

        /// <summary>
        /// Default: safe mode is not supported. Transports that bind safe mode to a persistent session
        /// (CLI terminals, native WinBox M2) override these.
        /// </summary>
        /// <inheritdoc/>
        public virtual void SafeModeTake() => throw new TikConnectionCapabilityNotSupportedException(TikConnectionCapability.SafeMode, SafeModeUnsupported);

        /// <inheritdoc/>
        public virtual void SafeModeRelease() => throw new TikConnectionCapabilityNotSupportedException(TikConnectionCapability.SafeMode, SafeModeUnsupported);

        /// <inheritdoc/>
        public virtual void SafeModeUnroll() => throw new TikConnectionCapabilityNotSupportedException(TikConnectionCapability.SafeMode, SafeModeUnsupported);

        /// <inheritdoc/>
        public virtual bool SafeModeGet() => SafeModeHeld;

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
        internal abstract IList<TikRecordSentence> RunPrint(TikCommandDescriptor descriptor);

        /// <summary>
        /// Executes an <c>add</c> command and returns the new record's <c>.id</c>.
        /// </summary>
        internal abstract string RunAdd(TikCommandDescriptor descriptor);

        /// <summary>
        /// Executes a non-query command (set, remove, enable, disable, move, unset, reboot, …).
        /// </summary>
        internal abstract void RunNonQuery(TikCommandDescriptor descriptor);

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

        // ── CallCommandSync (low-level) ────────────────────────────────────────

        /// <inheritdoc/>
        public IEnumerable<ITikSentence> CallCommandSync(params string[] commandRows)
            => CallCommandSync((IEnumerable<string>)commandRows);

        /// <inheritdoc/>
        public IEnumerable<ITikSentence> CallCommandSync(IEnumerable<string> commandRows)
        {
            var rows = new List<string>(commandRows);
            if (rows.Count == 0)
                throw new ArgumentException("commandRows must not be empty.");

            string commandText = rows[0];

            // Parse remaining rows as parameters
            var parameters = new List<ITikCommandParameter>();
            for (int i = 1; i < rows.Count; i++)
            {
                string row = rows[i];
                if (row.StartsWith(".tag=") || row.StartsWith(".tag ="))
                    continue;  // tags are no-op for command transports

                if (row.StartsWith("?"))
                {
                    string kv = row.TrimStart('?');
                    if (kv.StartsWith("="))
                        kv = kv.Substring(1);
                    int eq = kv.IndexOf('=');
                    if (eq >= 0)
                        parameters.Add(new TikCommandParameter(kv.Substring(0, eq), kv.Substring(eq + 1), TikCommandParameterFormat.Filter));
                }
                else if (row.StartsWith("="))
                {
                    string kv = row.Substring(1);
                    int eq = kv.IndexOf('=');
                    if (eq >= 0)
                        parameters.Add(new TikCommandParameter(kv.Substring(0, eq), kv.Substring(eq + 1), TikCommandParameterFormat.NameValue));
                }
            }

            string verb = TikPath.Verb(commandText);
            var descriptor = new TikCommandDescriptor(commandText, parameters);

            if (verb == "add")
            {
                string id = RunAdd(descriptor);
                return new List<ITikSentence> { new TikDoneSentenceResult(id) };
            }

            if (verb == "remove" || verb == "set" || verb == "unset" || verb == "move"
                || verb == "enable" || verb == "disable")
            {
                RunNonQuery(descriptor);
                return new List<ITikSentence> { new TikDoneSentenceResult() };
            }

            // Read
            var result = new List<ITikSentence>();
            result.AddRange(RunPrint(descriptor));
            result.Add(new TikDoneSentenceResult());
            return result;
        }

        // ── CallCommandAsync (not supported) ──────────────────────────────────

        /// <inheritdoc/>
        public Thread CallCommandAsync(IEnumerable<string> commandRows, string tag, Action<ITikSentence> oneResponseCallback)
        {
            // Raw tagged/multiplexed async is a binary-API feature (Tagging). CLI/native do report Listen, but
            // they emulate it via polling (ITikMonitorTransport / ExecuteAsync), not this low-level entry point.
            throw new TikConnectionCapabilityNotSupportedException(TikConnectionCapability.Tagging,
                "This transport does not support tagged asynchronous commands (CallCommandAsync). Use the binary API.");
        }

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
