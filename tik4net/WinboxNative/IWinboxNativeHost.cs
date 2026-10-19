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

        /// <summary>The friendly-name → M2 id lookup of the open session.</summary>
        WinboxIdResolver IdResolver { get; }

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

        /// <summary>The session field overrides of a path (<see cref="WinboxNativeConnection.FieldOverride"/>).</summary>
        IReadOnlyDictionary<string, int> OverridesFor(string apiPath);

        /// <summary>The field resolver for a path and the handler it resolves to.</summary>
        WinboxFieldResolver MakeResolver(string apiPath, int[] handler);

        /// <summary>The command gate for one M2 operation: a no-op on a multiplexed session.</summary>
        WinboxNativeConnection.CommandGate EnterCommand();

        /// <summary>Encodes the command's name=value parameters as M2 fields, references resolved under await.</summary>
        Task<List<byte[]>> EncodeNameValueFieldsAsync(int[] handler, TikCommandDescriptor descriptor,
            WinboxFieldResolver resolver, bool skipId, CancellationToken cancellationToken, bool allowReadOnly = false,
            bool includeFilters = false, bool skipSnapshotModifier = false);

        /// <summary>Encodes the command's name=value parameters as M2 fields, references resolved by <paramref name="resolveRef"/>.</summary>
        List<byte[]> EncodeNameValueFields(int[] handler, TikCommandDescriptor descriptor, WinboxFieldResolver resolver,
            bool skipId, Func<int[], string, int?> resolveRef, bool allowReadOnly = false, bool includeFilters = false,
            bool skipSnapshotModifier = false);

        /// <summary>Takes the WinBox labels the command carries for the resolvers it builds.</summary>
        void TakeLabelHints(TikCommandDescriptor descriptor);

        /// <summary><see cref="EnterCommand"/> for an awaiting caller.</summary>
        Task<WinboxNativeConnection.CommandGate> EnterCommandAsync(CancellationToken cancellationToken);

        /// <summary>The handler and field resolver of a write path; an unmapped path throws as reads do.</summary>
        (int[] handler, WinboxFieldResolver resolver) ResolveHandlerAndFields(string apiPath);

        /// <summary>Whether the window a path resolves to is a singleton (get-singleton / set-singleton).</summary>
        bool IsSingletonWindow(string apiPath, int[] handler);

        /// <summary>Invokes a standalone action window (<c>/tool/wol</c>) and returns the API's one empty row.</summary>
        Task<IList<TikRecordSentence>> RunActionWindowAsync(string apiPath, int[] handler, TikCommandDescriptor descriptor,
            CancellationToken cancellationToken);

        /// <summary>Invokes an action verb's <c>.jg</c> doit on its parent handler (<c>/system/script/run</c>).</summary>
        Task DispatchActionVerbAsync(string verb, string apiPath, int[] handler, WinboxFieldResolver resolver,
            TikCommandDescriptor descriptor, CancellationToken cancellationToken);
    }
}
