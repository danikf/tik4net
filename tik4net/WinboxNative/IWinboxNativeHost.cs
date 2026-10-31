using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Connection;
using tik4net.Winbox;

namespace tik4net.WinboxNative
{
    /// <summary>
    /// What the native connection's collaborators (<see cref="WinboxNativeMonitor"/>, <see cref="WinboxNativeWriter"/>)
    /// need from the connection that runs them, the polling engine's host (<see cref="IPollingMonitorHost"/>) among it.
    /// <see cref="WinboxNativeConnection"/> implements it explicitly, so none of it shows on the connection.
    /// </summary>
    internal interface IWinboxNativeHost : IPollingMonitorHost
    {
        /// <summary>The M2 operations of the open session; <c>null</c> while closed.</summary>
        WinboxNativeM2Operations Ops { get; }

        /// <summary>The M2 record → API field decoder of the open session.</summary>
        WinboxRecordCodec Codec { get; }

        /// <summary>The name ↔ id service of the referenced tables, shared with <see cref="Codec"/>.</summary>
        WinboxReferenceTables References { get; }

        /// <summary>The router's <c>.jg</c> catalog.</summary>
        WinboxJgCatalog Catalog { get; }

        /// <summary>API path → M2 handler.</summary>
        WinboxHandlerMap HandlerMap { get; }

        /// <summary>The WinBox labels the running command carries (<see cref="TikSpecialProperties.WinboxLabels"/>).</summary>
        IReadOnlyDictionary<string, string>? LabelHints { get; }

        /// <summary>Whether WinBox GUI names resolve (<see cref="WinboxNativeConnection.UseGuiNames"/>).</summary>
        bool UseGuiNames { get; }

        /// <summary>Whether the connection is open.</summary>
        bool IsOpened { get; }

        /// <summary>The receive timeout (ms), which bounds every M2 operation.</summary>
        int ReceiveTimeoutMs { get; }

        /// <summary>Throws the not-open exception when the session is not usable.</summary>
        void EnsureNativeOpen();

        /// <summary>The command in its canonical form (menu-label path → API path, captions → API names).</summary>
        TikCommandDescriptor Canonical(TikCommandDescriptor descriptor);

        /// <summary>The command an exception carries.</summary>
        ITikCommand CommandFor(string commandText);

        /// <summary>As <see cref="CommandFor(string)"/>, with the descriptor's parameters.</summary>
        ITikCommand CommandFor(TikCommandDescriptor descriptor);

        /// <summary>A native M2 operation error as the public exception the other transports raise.</summary>
        TikCommandException TranslateM2Error(WinboxM2OperationException ex, string commandText);

        /// <summary>
        /// The field resolver for a path and the handler it resolves to; an action's arguments laid over it when
        /// <paramref name="actionLabel"/> names one, the window and overrides of <paramref name="windowPath"/> when the
        /// values live in another path's window (a snapshot monitor reads its parent's record).
        /// </summary>
        WinboxFieldResolver MakeResolver(string apiPath, WinboxHandler handler, string? actionLabel = null, string? windowPath = null);

        /// <summary>
        /// A refusal the client makes before sending anything, as the trap the router would raise over the API. The one
        /// place such a refusal is classified (review D7). <paramref name="innerException"/> is the client-side reason, when
        /// there is one to hand on.
        /// </summary>
        TikCommandException TrapFor(string commandText, string message, Exception? innerException = null);

        /// <summary>As <see cref="TrapFor(string, string, Exception)"/>, with the descriptor's parameters on the command.</summary>
        TikCommandException TrapFor(TikCommandDescriptor descriptor, string message);

        /// <summary>Encodes the command's name=value parameters as M2 fields, references resolved under await.</summary>
        Task<List<byte[]>> EncodeNameValueFieldsAsync(WinboxHandler handler, TikCommandDescriptor descriptor,
            WinboxFieldResolver resolver, bool skipId, CancellationToken cancellationToken, bool allowReadOnly = false,
            bool includeFilters = false, bool skipSnapshotModifier = false);

        /// <summary>Takes the WinBox labels the command carries for the resolvers it builds.</summary>
        void TakeLabelHints(TikCommandDescriptor descriptor);

        /// <summary>The handler and field resolver of a write path; an unmapped path throws as reads do.</summary>
        (WinboxHandler handler, WinboxFieldResolver resolver) ResolveHandlerAndFields(string apiPath);

        /// <summary>Whether the window a path resolves to is a singleton (get-singleton / set-singleton).</summary>
        bool IsSingletonWindow(string apiPath, WinboxHandler handler);

        /// <summary>Invokes a standalone action window (<c>/tool/wol</c>) and returns the API's one empty row.</summary>
        Task<IList<TikRecordSentence>> RunActionWindowAsync(string apiPath, WinboxHandler handler, TikCommandDescriptor descriptor,
            CancellationToken cancellationToken);

        /// <summary>Invokes an action verb's <c>.jg</c> doit on its parent handler (<c>/system/script/run</c>).</summary>
        Task DispatchActionVerbAsync(string verb, string apiPath, WinboxHandler handler, WinboxFieldResolver resolver,
            TikCommandDescriptor descriptor, CancellationToken cancellationToken);
    }
}
