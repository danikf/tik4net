using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Connection;

namespace tik4net.Cli
{
    /// <summary>
    /// Transport-agnostic base class for RouterOS CLI-based connections (Telnet, SSH, MACTelnet, WinBox CLI).
    /// Builds the CLI command text, sends it through the transport, and parses the textual response back
    /// into the shared command/sentence model. All the transport-neutral plumbing
    /// (<see cref="ITikConnection"/> surface, command factory, low-level dispatch, diagnostics) lives in
    /// <see cref="TikCommandConnectionBase"/>.
    ///
    /// Concrete transport subclasses must implement:
    /// <list type="bullet">
    ///   <item><see cref="TikCommandConnectionBase.Open(string, string, string)"/> / <see cref="TikCommandConnectionBase.Open(string, int, string, string)"/></item>
    ///   <item><see cref="TikCommandConnectionBase.OpenAsync(string, string, string)"/> / <see cref="TikCommandConnectionBase.OpenAsync(string, int, string, string)"/></item>
    ///   <item><see cref="TikCommandConnectionBase.Close"/></item>
    ///   <item><see cref="ExecuteCliCommandCoreAsync"/> — send one CLI string, return the response text.</item>
    /// </list>
    ///
    /// The response text passed to <see cref="ExecuteCliCommandCoreAsync"/> must already have ANSI
    /// escape sequences stripped (<see cref="VtStripper.StripAnsi"/>) and any terminal echo /
    /// prompt trimmed by the transport before being returned — the core layer only sees data lines.
    /// </summary>
    public abstract class CliConnectionBase : TikCommandConnectionBase
    {
        // ── Transport — subclass contract ─────────────────────────────────────

        /// <summary>
        /// Sends <paramref name="cliText"/> to the router and returns the cleaned response text
        /// (ANSI stripped, echo and prompt removed). The implementation is responsible for all
        /// transport-specific concerns: framing, echo removal, paging (without-paging or equivalent).
        /// </summary>
        protected abstract Task<string> ExecuteCliCommandCoreAsync(string cliText, CancellationToken ct);

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

        // ── CRUD hooks — CLI text build + parse ────────────────────────────────

        /// <summary>
        /// Executes a <c>print as-value</c> command and returns parsed sentences.
        /// When the <c>.cli-stats</c> marker is present in the descriptor's parameters,
        /// performs two queries (detail + stats) and merges the results by <c>.id</c>
        /// so that config fields and live counter fields are combined in each record.
        /// </summary>
        internal override IList<TikRecordSentence> RunPrint(TikCommandDescriptor descriptor)
        {
            EnsureOpened();

            // Action verbs invoked via ExecuteList (e.g. /system/script/run) are not reads — over a
            // terminal they execute fire-and-forget and yield no per-record !re output (unlike the
            // binary API). Run as an action and return an empty result set.
            if (GetVerb(descriptor.CommandText) == "run")
            {
                string runCli = CliCommandBuilder.BuildSimpleVerb(descriptor.CommandText, "run", descriptor.Parameters);
                string runOut = ExecuteCliCommand(runCli);
                CliErrorParser.ThrowIfError(runOut, CreateDummyCommand(descriptor));
                return new List<TikRecordSentence>();
            }

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
            IList<TikRecordSentence> configRecords = CliOutputParser.ParseAsValue(detailOutput);

            string statsText = CliCommandBuilder.BuildPrintStats(descriptor.CommandText, descriptor.Parameters);
            string statsOutput = ExecuteCliCommand(statsText);
            CliErrorParser.ThrowIfError(statsOutput, CreateDummyCommand(descriptor));
            IList<TikRecordSentence> statsRecords = CliOutputParser.ParseAsValue(statsOutput);

            // Build index of stats records by .id for O(1) lookup.
            var statsById = new Dictionary<string, TikRecordSentence>(StringComparer.OrdinalIgnoreCase);
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
            var merged = new List<TikRecordSentence>(configRecords.Count);
            foreach (var cfg in configRecords)
            {
                string id = cfg.GetResponseFieldOrDefault(TikSpecialProperties.Id, null);
                if (id == null || !statsById.TryGetValue(id, out TikRecordSentence sr))
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
                merged.Add(new TikRecordSentence(mergedFields));
            }

            return merged;
        }

        /// <summary>
        /// Executes an <c>add</c> command and returns the new record's .id.
        /// </summary>
        internal override string RunAdd(TikCommandDescriptor descriptor)
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
        internal override void RunNonQuery(TikCommandDescriptor descriptor)
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
        /// Executes a scalar <c>get</c> command (already fully built by the command) and returns the raw value.
        /// </summary>
        internal override string RunScalarGet(string cliText)
        {
            EnsureOpened();
            string output = ExecuteCliCommand(cliText);
            // Error-check so RouterOS error text (e.g. "input does not match any value of value-name",
            // "syntax error …") is surfaced as the correct exception instead of leaking as a value.
            CliErrorParser.ThrowIfError(output, new TikGenericCommand(this, cliText));
            if (string.IsNullOrWhiteSpace(output))
                return null;
            return output.Trim();
        }
    }
}
