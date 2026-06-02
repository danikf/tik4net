using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.Cli
{
    /// <summary>
    /// Transport-agnostic base class for RouterOS CLI-based connections (Telnet, SSH, MACTelnet).
    /// Implements the full <see cref="ITikConnection"/> surface and serialises commands through
    /// a <see cref="SemaphoreSlim"/> (terminal is inherently sequential).
    ///
    /// Concrete transport subclasses must implement:
    /// <list type="bullet">
    ///   <item><see cref="Open(string, string, string)"/> / <see cref="Open(string, int, string, string)"/></item>
    ///   <item><see cref="OpenAsync(string, string, string)"/> / <see cref="OpenAsync(string, int, string, string)"/></item>
    ///   <item><see cref="Close"/></item>
    ///   <item><see cref="ExecuteCliCommandCoreAsync"/> — send one CLI string, return the response text.</item>
    /// </list>
    ///
    /// The response text passed to <see cref="ExecuteCliCommandCoreAsync"/> must already have ANSI
    /// escape sequences stripped (<see cref="VtStripper.StripAnsi"/>) and any terminal echo /
    /// prompt trimmed by the transport before being returned — the core layer only sees data lines.
    /// </summary>
    public abstract class CliConnectionBase : ITikConnection, ITikConnectionCapabilities
    {
        private readonly SemaphoreSlim _cmdLock = new SemaphoreSlim(1, 1);
        private bool _isOpened;

        // ── ITikConnection properties ─────────────────────────────────────────

        /// <inheritdoc/>
        public bool DebugEnabled { get; set; }

        /// <inheritdoc/>
        public bool IsOpened => _isOpened;

        /// <inheritdoc/>
        public Encoding Encoding { get; set; } = Encoding.UTF8;

        /// <summary>No-op for CLI transports (no tag protocol).</summary>
        public bool SendTagWithSyncCommand { get; set; }

        /// <inheritdoc/>
        public int SendTimeout { get; set; } = 30000;

        /// <inheritdoc/>
        public int ReceiveTimeout { get; set; } = 30000;

        /// <inheritdoc/>
        public event EventHandler<TikConnectionCommCallbackEventArgs> OnReadRow;

        /// <inheritdoc/>
        public event EventHandler<TikConnectionCommCallbackEventArgs> OnWriteRow;

        // ── Capabilities ──────────────────────────────────────────────────────

        /// <inheritdoc/>
        public virtual TikConnectionCapability Capabilities => TikConnectionCapability.Crud;

        // ── Transport — subclass contract ─────────────────────────────────────

        /// <summary>
        /// Sends <paramref name="cliText"/> to the router and returns the cleaned response text
        /// (ANSI stripped, echo and prompt removed). The implementation is responsible for all
        /// transport-specific concerns: framing, echo removal, paging (without-paging or equivalent).
        /// </summary>
        protected abstract Task<string> ExecuteCliCommandCoreAsync(string cliText, CancellationToken ct);

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

        // ── Open/Close helpers for subclasses ─────────────────────────────────

        /// <summary>
        /// Subclasses must call this after a successful login to mark the connection as open.
        /// </summary>
        protected void SetOpened() => _isOpened = true;

        /// <summary>
        /// Subclasses must call this when closing or on a fatal error to mark the connection as closed.
        /// </summary>
        protected void SetClosed() => _isOpened = false;

        // ── Semaphore-serialised execution ────────────────────────────────────

        /// <summary>
        /// Serialises access, fires diagnostics events, then delegates to
        /// <see cref="ExecuteCliCommandCoreAsync"/>.
        /// </summary>
        protected async Task<string> ExecuteCliCommandAsync(string cliText, CancellationToken ct)
        {
            await _cmdLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                FireWriteRow(cliText);
                string result = await ExecuteCliCommandCoreAsync(cliText, ct).ConfigureAwait(false);
                FireReadRow(result);
                return result;
            }
            finally
            {
                _cmdLock.Release();
            }
        }

        /// <summary>Synchronous wrapper around <see cref="ExecuteCliCommandAsync"/>.</summary>
        protected string ExecuteCliCommand(string cliText)
            => ExecuteCliCommandAsync(cliText, CancellationToken.None).GetAwaiter().GetResult();

        // ── Internal hooks used by CliCommand ──────────────────────────────────

        /// <summary>
        /// Executes a <c>print as-value</c> command and returns parsed sentences.
        /// When the <c>.cli-stats</c> marker is present in the descriptor's parameters,
        /// performs two queries (detail + stats) and merges the results by <c>.id</c>
        /// so that config fields and live counter fields are combined in each record.
        /// </summary>
        internal IList<CliReSentence> RunPrint(CliCommandDescriptor descriptor)
        {
            EnsureOpened();

            bool needStats = descriptor.Parameters.Any(p => p.Name == TikSpecialProperties.CliStats);

            if (!needStats)
            {
                // Normal single-query path.
                string cliText = CliCommandBuilder.BuildPrint(descriptor.CommandText, descriptor.Parameters);
                string output = ExecuteCliCommand(cliText);
                CliErrorParser.ThrowIfError(output, CreateDummyCommand(descriptor));
                return CliOutputParser.ParseAsValue(output);
            }

            // Two-query path: detail (config) + stats (counters), merged by .id.
            string detailText = CliCommandBuilder.BuildPrint(descriptor.CommandText, descriptor.Parameters);
            string detailOutput = ExecuteCliCommand(detailText);
            CliErrorParser.ThrowIfError(detailOutput, CreateDummyCommand(descriptor));
            IList<CliReSentence> configRecords = CliOutputParser.ParseAsValue(detailOutput);

            string statsText = CliCommandBuilder.BuildPrintStats(descriptor.CommandText, descriptor.Parameters);
            string statsOutput = ExecuteCliCommand(statsText);
            CliErrorParser.ThrowIfError(statsOutput, CreateDummyCommand(descriptor));
            IList<CliReSentence> statsRecords = CliOutputParser.ParseAsValue(statsOutput);

            // Build index of stats records by .id for O(1) lookup.
            var statsById = new Dictionary<string, CliReSentence>(StringComparer.OrdinalIgnoreCase);
            foreach (var sr in statsRecords)
            {
                string id = sr.GetResponseFieldOrDefault(TikSpecialProperties.Id, null);
                if (id != null)
                    statsById[id] = sr;
            }

            if (statsById.Count == 0)
            {
                // Stats returned nothing or no .id — fall back gracefully to config-only.
                return configRecords;
            }

            // Merge: overlay stats fields onto each config record.
            var merged = new List<CliReSentence>(configRecords.Count);
            foreach (var cfg in configRecords)
            {
                string id = cfg.GetResponseFieldOrDefault(TikSpecialProperties.Id, null);
                if (id == null || !statsById.TryGetValue(id, out CliReSentence sr))
                {
                    // No matching stats record — keep config as-is.
                    merged.Add(cfg);
                    continue;
                }

                // Clone config fields and add missing stats fields.
                var mergedFields = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                // Start with config (includes all config fields + .id).
                foreach (var kv in cfg.Words)
                    mergedFields[kv.Key] = kv.Value;
                // Overlay stats: add fields not already present in config.
                foreach (var kv in sr.Words)
                {
                    if (!mergedFields.ContainsKey(kv.Key))
                        mergedFields[kv.Key] = kv.Value;
                }
                merged.Add(new CliReSentence(mergedFields));
            }

            return merged;
        }

        /// <summary>
        /// Executes an <c>add</c> command and returns the new record's .id.
        /// </summary>
        internal string RunAdd(CliCommandDescriptor descriptor)
        {
            EnsureOpened();
            string cliText = CliCommandBuilder.BuildAdd(descriptor.CommandText, descriptor.Parameters);
            string output = ExecuteCliCommand(cliText);
            CliErrorParser.ThrowIfError(output, CreateDummyCommand(descriptor));
            return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
        }

        /// <summary>
        /// Executes a non-query command (set, remove, enable, disable, move, reboot, …).
        /// </summary>
        internal void RunNonQuery(CliCommandDescriptor descriptor)
        {
            EnsureOpened();
            string verb = GetVerb(descriptor.CommandText);

            string cliText;
            switch (verb)
            {
                case "set":
                    cliText = CliCommandBuilder.BuildSet(descriptor.CommandText, descriptor.Parameters);
                    break;
                case "remove":
                    cliText = CliCommandBuilder.BuildRemove(descriptor.CommandText, descriptor.Parameters);
                    break;
                case "enable":
                case "disable":
                case "move":
                case "unset":
                    cliText = CliCommandBuilder.BuildSimpleVerb(descriptor.CommandText, verb, descriptor.Parameters);
                    break;
                default:
                    cliText = CliCommandBuilder.BuildNonQuery(descriptor.CommandText, descriptor.Parameters);
                    break;
            }

            string output = ExecuteCliCommand(cliText);
            CliErrorParser.ThrowIfError(output, CreateDummyCommand(descriptor));
        }

        /// <summary>
        /// Executes a scalar <c>get</c> command (already fully built by CliCommand) and returns the raw value.
        /// </summary>
        internal string RunScalarGet(string cliText)
        {
            EnsureOpened();
            string output = ExecuteCliCommand(cliText);
            // Error-check so RouterOS error text (e.g. "input does not match any value of value-name",
            // "syntax error …") is surfaced as the correct exception instead of leaking as a value.
            CliErrorParser.ThrowIfError(output, new CliCommand(this, cliText));
            if (string.IsNullOrWhiteSpace(output))
                return null;
            return output.Trim();
        }

        // ── ITikConnection — Command factory ──────────────────────────────────

        /// <inheritdoc/>
        public ITikCommand CreateCommand()
            => new CliCommand(this);

        /// <inheritdoc/>
        public ITikCommand CreateCommand(TikCommandParameterFormat defaultParameterFormat)
            => new CliCommand(this, defaultParameterFormat);

        /// <inheritdoc/>
        public ITikCommand CreateCommand(string commandText, params ITikCommandParameter[] parameters)
            => new CliCommand(this, commandText, parameters);

        /// <inheritdoc/>
        public ITikCommand CreateCommand(string commandText, TikCommandParameterFormat defaultParameterFormat, params ITikCommandParameter[] parameters)
            => new CliCommand(this, commandText, defaultParameterFormat, parameters);

        /// <inheritdoc/>
        public ITikCommand CreateCommandAndParameters(string commandText, params string[] parameterNamesAndValues)
        {
            var cmd = new CliCommand(this, commandText);
            cmd.AddParameterAndValues(parameterNamesAndValues);
            return cmd;
        }

        /// <inheritdoc/>
        public ITikCommand CreateCommandAndParameters(string commandText, TikCommandParameterFormat defaultParameterFormat, params string[] parameterNamesAndValues)
        {
            var cmd = new CliCommand(this, commandText, defaultParameterFormat);
            cmd.AddParameterAndValues(parameterNamesAndValues);
            return cmd;
        }

        /// <inheritdoc/>
        public ITikCommandParameter CreateParameter(string name, string value)
            => new CliCommandParameter(name, value);

        /// <inheritdoc/>
        public ITikCommandParameter CreateParameter(string name, string value, TikCommandParameterFormat parameterFormat)
            => new CliCommandParameter(name, value, parameterFormat);

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
                    continue;  // tags are no-op for CLI

                if (row.StartsWith("?"))
                {
                    string kv = row.TrimStart('?');
                    if (kv.StartsWith("="))
                        kv = kv.Substring(1);
                    int eq = kv.IndexOf('=');
                    if (eq >= 0)
                        parameters.Add(new CliCommandParameter(kv.Substring(0, eq), kv.Substring(eq + 1), TikCommandParameterFormat.Filter));
                }
                else if (row.StartsWith("="))
                {
                    string kv = row.Substring(1);
                    int eq = kv.IndexOf('=');
                    if (eq >= 0)
                        parameters.Add(new CliCommandParameter(kv.Substring(0, eq), kv.Substring(eq + 1), TikCommandParameterFormat.NameValue));
                }
            }

            string verb = GetVerb(commandText);
            var descriptor = new CliCommandDescriptor(commandText, parameters);

            if (verb == "add")
            {
                string id = RunAdd(descriptor);
                return new List<ITikSentence> { new CliDoneSentence(id) };
            }

            if (verb == "remove" || verb == "set" || verb == "unset" || verb == "move"
                || verb == "enable" || verb == "disable")
            {
                RunNonQuery(descriptor);
                return new List<ITikSentence> { new CliDoneSentence() };
            }

            // Read
            var result = new List<ITikSentence>();
            result.AddRange(RunPrint(descriptor));
            result.Add(new CliDoneSentence());
            return result;
        }

        // ── CallCommandAsync (not supported) ──────────────────────────────────

        /// <inheritdoc/>
        public Thread CallCommandAsync(IEnumerable<string> commandRows, string tag, Action<ITikSentence> oneResponseCallback)
        {
            throw new NotSupportedException("CLI transport does not support asynchronous commands. Use a transport that reports Listen capability.");
        }

        // ── IDisposable ────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public void Dispose() => Close();

        // ── Diagnostics ────────────────────────────────────────────────────────

        private void FireWriteRow(string word)
        {
            OnWriteRow?.Invoke(this, new TikConnectionCommCallbackEventArgs(word));
            if (DebugEnabled)
                System.Diagnostics.Debug.WriteLine("CLI>> " + word);
        }

        private void FireReadRow(string word)
        {
            OnReadRow?.Invoke(this, new TikConnectionCommCallbackEventArgs(word));
            if (DebugEnabled)
                System.Diagnostics.Debug.WriteLine("CLI<< " + (word != null && word.Length > 200 ? word.Substring(0, 200) + "..." : word));
        }

        // ── Private helpers ────────────────────────────────────────────────────

        private void EnsureOpened()
        {
            if (!_isOpened)
                throw new TikConnectionNotOpenException("CLI connection is not open.");
        }

        private static string GetVerb(string commandText)
        {
            if (string.IsNullOrWhiteSpace(commandText))
                return "print";
            string trimmed = commandText.TrimStart('/');
            var segments = trimmed.Split('/');
            return segments[segments.Length - 1].ToLowerInvariant();
        }

        /// <summary>
        /// Creates a minimal command object for use in exception constructors when the original
        /// CliCommand is not available (e.g. in CallCommandSync / RunNonQuery paths).
        /// </summary>
        private ITikCommand CreateDummyCommand(CliCommandDescriptor descriptor)
        {
            var cmd = new CliCommand(this, descriptor.CommandText);
            foreach (var p in descriptor.Parameters)
                cmd.AddParameter(p.Name, p.Value, p.ParameterFormat);
            return cmd;
        }
    }
}
