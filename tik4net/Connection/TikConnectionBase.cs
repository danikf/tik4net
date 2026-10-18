using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.Connection
{
    /// <summary>
    /// The plumbing every tik4net connection shares, whatever its protocol: the open state, the timeouts and
    /// encoding, the row-trace events, the safe-mode flag and the menu-schema cache. It knows nothing about how a
    /// command reaches the router.
    /// </summary>
    /// <remarks>
    /// Two families derive from it: <see cref="TikCommandConnectionBase"/>, the request→reply transports (REST, the
    /// CLI terminals, WinBox native), and the binary API, whose tagged sentence protocol is its own. A transport
    /// written outside this assembly derives from <see cref="TikCommandConnectionBase"/>; this class cannot be
    /// derived from directly.
    /// </remarks>
    public abstract class TikConnectionBase : ITikConnection, ITikConnectionCapabilities
    {
        // Volatile: the binary API's reader thread clears it when the socket fails, while callers read it.
        private volatile bool _isOpened;

        private protected TikConnectionBase()
        {
        }

        // ── ITikConnection properties ─────────────────────────────────────────

        /// <inheritdoc/>
        public bool DebugEnabled { get; set; }

        /// <inheritdoc/>
        public bool IsOpened => _isOpened;

        /// <inheritdoc/>
        public Encoding Encoding { get; set; } = Encoding.UTF8;

        /// <inheritdoc/>
        public TimeSpan SendTimeout
        {
            get => TimeSpan.FromMilliseconds(SendTimeoutMs);
            set => SendTimeoutMs = TikTimeouts.ToMilliseconds(value);
        }

        /// <inheritdoc/>
        public TimeSpan ReceiveTimeout
        {
            get => TimeSpan.FromMilliseconds(ReceiveTimeoutMs);
            set => ReceiveTimeoutMs = TikTimeouts.ToMilliseconds(value);
        }

        /// <inheritdoc/>
        public TimeSpan ConnectTimeout
        {
            get => TimeSpan.FromMilliseconds(ConnectTimeoutMs);
            set => ConnectTimeoutMs = TikTimeouts.ToMilliseconds(value);
        }

        // The transports' sockets and read loops count in milliseconds; these are the public timeouts in that unit.
        // Zero means "leave the socket's own timeout alone", for a caller who asks for it deliberately.
        internal int SendTimeoutMs { get; set; } = 30000;
        internal int ReceiveTimeoutMs { get; set; } = 30000;
        internal int ConnectTimeoutMs { get; set; } = 15000;

        /// <inheritdoc/>
        public event EventHandler<TikConnectionCommCallbackEventArgs>? OnReadRow;

        /// <inheritdoc/>
        public event EventHandler<TikConnectionCommCallbackEventArgs>? OnWriteRow;

        // ── Capabilities ──────────────────────────────────────────────────────

        /// <inheritdoc/>
        public abstract TikConnectionCapability Capabilities { get; }

        // ── Transport ─────────────────────────────────────────────────────────

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

        // ── Command factory ───────────────────────────────────────────────────

        // The two things a transport decides; the eight factory overloads below are the same for every transport.
        // A null commandText is a command whose text the caller sets later.
        private protected abstract ITikCommand NewCommand(string? commandText, TikCommandParameterFormat format,
            ITikCommandParameter[] parameters);
        private protected abstract ITikCommandParameter NewParameter(string name, string? value, TikCommandParameterFormat format);

        /// <inheritdoc/>
        public ITikCommand CreateCommand()
            => NewCommand(null, TikCommandParameterFormat.Default, Array.Empty<ITikCommandParameter>());

        /// <inheritdoc/>
        public ITikCommand CreateCommand(TikCommandParameterFormat defaultParameterFormat)
            => NewCommand(null, defaultParameterFormat, Array.Empty<ITikCommandParameter>());

        /// <inheritdoc/>
        public ITikCommand CreateCommand(string commandText, params ITikCommandParameter[] parameters)
            => NewCommand(commandText, TikCommandParameterFormat.Default, parameters);

        /// <inheritdoc/>
        public ITikCommand CreateCommand(string commandText, TikCommandParameterFormat defaultParameterFormat,
            params ITikCommandParameter[] parameters)
            => NewCommand(commandText, defaultParameterFormat, parameters);

        /// <inheritdoc/>
        public ITikCommand CreateCommandAndParameters(string commandText, params string[] parameterNamesAndValues)
            => CreateCommandAndParameters(commandText, TikCommandParameterFormat.Default, parameterNamesAndValues);

        /// <inheritdoc/>
        public ITikCommand CreateCommandAndParameters(string commandText, TikCommandParameterFormat defaultParameterFormat,
            params string[] parameterNamesAndValues)
        {
            // The parameters are created with TikCommandParameterFormat.Default and resolved at execution, so it
            // does not matter that the command's default format is set before they are added.
            var cmd = NewCommand(commandText, defaultParameterFormat, Array.Empty<ITikCommandParameter>());
            cmd.AddParameterAndValues(parameterNamesAndValues);
            return cmd;
        }

        /// <inheritdoc/>
        public ITikCommandParameter CreateParameter(string name, string? value)
            => NewParameter(name, value, TikCommandParameterFormat.Default);

        /// <inheritdoc/>
        public ITikCommandParameter CreateParameter(string name, string? value, TikCommandParameterFormat parameterFormat)
            => NewParameter(name, value, parameterFormat);

        // ── Open state ────────────────────────────────────────────────────────

        /// <summary>
        /// Tracks whether this connection currently holds Safe Mode. Maintained and reported by the
        /// connections that implement <see cref="ITikSafeModeConnection"/>; it lives here because the bookkeeping
        /// is identical wherever safe mode exists, while the interface deliberately does not — REST cannot bind a
        /// rollback to a connection it does not keep, so it implements neither the interface nor a set of methods
        /// that only throw.
        /// </summary>
        protected bool SafeModeHeld { get; set; }

        /// <summary>
        /// Backs <see cref="ITikMenuSchemaConnection.ValidateWrites"/> on every transport that implements it.
        /// </summary>
        internal bool ValidateWrites { get; set; }

        /// <summary>
        /// Subclasses must call this after a successful login to mark the connection as open.
        /// </summary>
        protected void SetOpened()
        {
            // What the previous open learnt about its router's menus is not true of this one.
            MenuSchemas = new TikMenuSchemaCache();
            _isOpened = true;
        }

        /// <summary>The menus described on this open (<see cref="TikMenuSchemaExtensions.DescribeMenu(ITikConnection, string)"/>).</summary>
        internal TikMenuSchemaCache MenuSchemas { get; private set; } = new TikMenuSchemaCache();

        /// <summary>
        /// Subclasses must call this when closing or on a fatal error to mark the connection as closed.
        /// </summary>
        protected void SetClosed() => _isOpened = false;

        /// <summary>
        /// Throws <see cref="TikConnectionNotOpenException"/> when the connection has not been opened. A transport
        /// that can also tell a session which stopped answering overrides it and adds that check.
        /// </summary>
        protected virtual void EnsureOpened()
        {
            if (!_isOpened)
                throw NotOpen();
        }

        /// <summary>The transport's name in diagnostics such as "API connection is not open." — the type's name unless a transport names itself.</summary>
        private protected virtual string TransportName => GetType().Name;

        /// <summary>The one "connection is not open" exception every transport throws.</summary>
        private protected TikConnectionNotOpenException NotOpen() => new TikConnectionNotOpenException(TransportName + " connection is not open.");

        // ── IDisposable ────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public virtual void Dispose() => Close();

        // ── Diagnostics ────────────────────────────────────────────────────────

        /// <summary>
        /// True when row-level tracing would be observed by someone — either <see cref="DebugEnabled"/>
        /// is set, or a <see cref="OnReadRow"/>/<see cref="OnWriteRow"/> handler is attached. Subclasses
        /// can gate the (potentially costly) rendering of a trace word behind this so it is only built
        /// when something is actually listening.
        /// </summary>
        protected bool RowTracingEnabled => DebugEnabled || OnReadRow != null || OnWriteRow != null;

        /// <summary>Short tag prefixing <see cref="DebugEnabled"/> trace lines (e.g. <c>CLI&gt;&gt;</c>), naming the
        /// channel (API / CLI / REST / …).</summary>
        protected abstract string DiagnosticPrefix { get; }

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
    }
}
