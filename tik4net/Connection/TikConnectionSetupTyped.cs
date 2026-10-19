using System;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.Connection
{
    /// <summary>The typed create behind every per-transport <c>Create…Connection</c> setup extension.</summary>
    internal static class TikConnectionSetupTyped
    {
        internal static T CreateTyped<T>(this TikConnectionSetup setup, TikConnectionType type,
            Action<ITikConnection>? configure = null) where T : class, ITikConnection
            => (T)setup.Create(type, configure);

        // async on purpose, like the methods it replaces: an exception from CreateUnopened faults the Task.
        internal static async Task<T> CreateTypedAsync<T>(this TikConnectionSetup setup, TikConnectionType type,
            Action<ITikConnection>? configure, CancellationToken ct) where T : class, ITikConnection
            => (T)await setup.CreateAsync(type, configure, ct).ConfigureAwait(false);
    }
}
