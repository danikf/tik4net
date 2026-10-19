using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.Connection
{
    /// <summary>
    /// Transport-neutral base class for RouterOS command-style connections that expose CRUD through
    /// the three CRUD <c>Run*</c> hooks, plus the optional raw and async ones (instead of the binary API
    /// sentence protocol). On top of the plumbing it shares with the binary API (<see cref="TikConnectionBase"/>) it supplies the command factory's primitives with
    /// <see cref="TikGenericCommand"/> and offers a <see cref="SemaphoreSlim"/> to serialise commands.
    ///
    /// It deliberately does <b>not</b> implement <see cref="ITikRawSentenceConnection"/>. That interface's
    /// contract is a command in the <i>connection-specific</i> format, and this class knows only the
    /// transport-neutral one; a base-class implementation could only accept API-shaped rows and translate
    /// them, which is what the O/R mapper already does and is the opposite of a raw call. Transports with a
    /// native dialect of their own implement it themselves — see <c>CliConnectionBase</c>.
    ///
    /// Concrete subclasses provide the transport (CLI terminal, native WinBox M2, …) by implementing:
    /// <list type="bullet">
    ///   <item><see cref="TikConnectionBase.Open(string, string, string)"/> / <see cref="TikConnectionBase.Open(string, int, string, string)"/></item>
    ///   <item><see cref="TikConnectionBase.OpenAsync(string, string, string, CancellationToken)"/> /
    ///       <see cref="TikConnectionBase.OpenAsync(string, int, string, string, CancellationToken)"/></item>
    ///   <item><see cref="TikConnectionBase.Close"/></item>
    ///   <item>the three CRUD hooks <see cref="RunPrint"/>, <see cref="RunAdd"/>, <see cref="RunNonQuery"/>.</item>
    /// </list>
    /// <para>
    /// <b>This is a real extension point, not just the shape the in-tree transports happen to share.</b> The
    /// hooks are <c>protected</c>, so a transport can be written outside this assembly: implement the three,
    /// declare what it can do in <see cref="Capabilities"/>, create it with <c>new</c> and configure it with
    /// <see cref="TikConnectionSetup.ApplyTo"/> before opening it. (Not through
    /// <see cref="ConnectionFactory.RegisterConnectionFactory"/>: that is keyed by the closed
    /// <see cref="TikConnectionType"/> enum and refuses every value but <c>Ssh</c>, the slot the
    /// <c>tik4net.ssh</c> package fills.) The <c>Run*Async</c> siblings are optional: their defaults throw rather
    /// than wrapping the synchronous hook in a <c>Task.Run</c> façade, so a transport that cannot genuinely
    /// await its I/O simply does not declare
    /// <see cref="TikConnectionCapability.AsyncCommands"/>. Only <see cref="ITikRawSentenceConnection"/> is
    /// left out on purpose — see below.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <b>Thread safety is the subclass's call, and it has to be made deliberately.</b> The
    /// <see cref="CommandLock"/> offered here is a default, not something this class enforces: the
    /// <c>Run*</c> hooks are invoked without it held, so each transport decides whether to serialize.
    /// The CLI family takes it around every command because a terminal carries one conversation;
    /// <see cref="Rest.RestConnection"/> never touches it, since each command is an independent HTTP
    /// request; <see cref="WinboxNative.WinboxNativeConnection"/> takes it only until its multiplexer is
    /// running and then stops. A transport written outside this assembly has to make the same choice and
    /// document which one it made — a channel that needs serializing and does not get it fails by handing
    /// a caller someone else's answer, not by throwing.
    /// </remarks>
    public abstract class TikCommandConnectionBase : TikConnectionBase
    {
        /// <summary>The constructor a transport written outside tik4net calls: it has no <see cref="TikConnectionType"/>.</summary>
        protected TikCommandConnectionBase() : base(null) { }

        /// <summary>The constructor of a built-in transport, which names its <see cref="TikConnectionType"/>.</summary>
        private protected TikCommandConnectionBase(TikConnectionType? connectionType) : base(connectionType) { }

        /// <summary>
        /// A lock to serialise commands with, offered and not enforced: the <c>Run*</c> hooks run without it held, and each
        /// transport decides whether its channel needs it (see the class remarks).
        /// </summary>
        protected SemaphoreSlim CommandLock { get; } = new SemaphoreSlim(1, 1);

        /// <summary>
        /// The exception for "the connection was closed while this command was still running".
        /// </summary>
        /// <remarks>
        /// <c>Close</c> does not wait for an in-flight command — it must stay prompt, and a caller closing a
        /// connection to escape a stuck one would be defeated by a Close that blocked for
        /// <see cref="TikConnectionBase.ReceiveTimeout"/>. So the command loses its socket mid-flight, and the question is only
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

        // ── Capabilities ──────────────────────────────────────────────────────

        /// <inheritdoc/>
        public override TikConnectionCapability Capabilities => TikConnectionCapability.Crud;

        // ── CRUD hooks — subclass contract ────────────────────────────────────

        /// <summary>
        /// Executes a read (<c>print</c>) command and returns the matching records.
        /// </summary>
        protected abstract IList<TikRecordSentence> RunPrint(TikCommandDescriptor descriptor);

        /// <summary>
        /// Executes an <c>add</c> command and returns the new record's <c>.id</c>. When the router's answer has none,
        /// throw <see cref="TikAddIdNotReadException"/>: the row has very likely been created, and a caller left without
        /// its id cannot clean it up. A null or empty id returned here is reported the same way.
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

        private protected sealed override ITikCommand NewCommand(string? commandText, TikCommandParameterFormat format,
            ITikCommandParameter[] parameters)
            => commandText == null
                ? new TikGenericCommand(this, format)
                : new TikGenericCommand(this, commandText, format, parameters);

        private protected sealed override ITikCommandParameter NewParameter(string name, string? value, TikCommandParameterFormat format)
            => new TikCommandParameter(name, value, format);

        // ── Internal dispatch ─────────────────────────────────────────────────
        //
        // The hooks above are protected: they are the extension point a transport implements, and a
        // transport is the only thing that should be able to define them. But TikGenericCommand — a
        // different class — is what CALLS them, and protected does not reach across classes. These shims
        // are that one bridge, internal so they stay inside the assembly, and nothing more than a
        // forwarding call. Adding a hook means adding its shim beside it.

        internal IList<TikRecordSentence> InvokeRunPrint(TikCommandDescriptor descriptor)
        {
            descriptor = Canonical(descriptor);
            CheckFilters(descriptor);
            return RunPrint(descriptor);
        }

        // Every read over this base goes through the two InvokeRunPrint*: the one place a filter on a field the menu
        // does not have is refused (TikFilterValidation).
        private void CheckFilters(TikCommandDescriptor descriptor)
            => TikFilterValidation.Check(this, null, descriptor.CommandText,
                descriptor.Parameters.Where(p => p.ParameterFormat == TikCommandParameterFormat.Filter).Select(p => p.Name),
                descriptor.Parameters.FirstOrDefault(p => p.Name == TikSpecialProperties.WinboxLabels)?.Value);

        internal string InvokeRunAdd(TikCommandDescriptor descriptor)
        {
            descriptor = Canonical(descriptor);
            return AddedId(RunAdd(descriptor), descriptor);
        }

        internal void InvokeRunNonQuery(TikCommandDescriptor descriptor) => RunNonQuery(Canonical(descriptor));

        internal string InvokeRunRawText(TikCommandDescriptor descriptor) => RunRawText(descriptor);

        internal Task<IList<TikRecordSentence>> InvokeRunPrintAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            descriptor = Canonical(descriptor);
            CheckFilters(descriptor);
            return RunPrintAsync(descriptor, cancellationToken);
        }

        internal async Task<string> InvokeRunAddAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            descriptor = Canonical(descriptor);
            return AddedId(await RunAddAsync(descriptor, cancellationToken).ConfigureAwait(false), descriptor);
        }

        // The new .id RunAdd promises. A transport written outside tik4net that hands back null or "" gets the exception
        // every built-in transport throws for an add the router answered without one.
        private string AddedId(string? id, TikCommandDescriptor descriptor)
            => id == null || id.Length == 0 ? throw TikAddIdNotRead.Create(CommandFor(descriptor), null) : id;

        internal Task InvokeRunNonQueryAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
            => RunNonQueryAsync(Canonical(descriptor), cancellationToken);

        /// <summary>
        /// The command in the form the rest of the pipeline expects, before anything — the filter check included
        /// — reads it. The identity here; WinBox native turns a menu-label path into its API path and, with GUI
        /// names on, a filter on a WinBox caption into the API field it labels. Not applied to a raw command.
        /// </summary>
        /// <remarks>Internal virtual rather than protected: only this assembly's transports may rewrite a command.</remarks>
        internal virtual TikCommandDescriptor Canonical(TikCommandDescriptor descriptor) => descriptor;

        internal Task<string> InvokeRunRawTextAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
            => RunRawTextAsync(descriptor, cancellationToken);

        /// <summary>The command a caller sees in an exception raised on a path that has a descriptor, not a command object.</summary>
        internal ITikCommand CommandFor(TikCommandDescriptor descriptor)
            => CommandFor(descriptor.CommandText, descriptor.Parameters);

        /// <summary>As <see cref="CommandFor(TikCommandDescriptor)"/>, for a bare command line.</summary>
        internal ITikCommand CommandFor(string commandText, IEnumerable<ITikCommandParameter>? parameters = null)
        {
            var cmd = new TikGenericCommand(this, commandText);
            if (parameters != null)
                foreach (var p in parameters)
                    cmd.AddParameter(p.Name, p.Value, p.ParameterFormat);
            return cmd;
        }
    }
}
