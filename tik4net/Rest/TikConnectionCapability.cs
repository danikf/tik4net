using System;

namespace tik4net
{
    /// <summary>
    /// Flags declaring which capabilities a transport supports.
    /// REST supports only <see cref="Crud"/>.
    /// </summary>
    [Flags]
    public enum TikConnectionCapability
    {
        /// <summary>No capabilities (placeholder / cleared flags).</summary>
        None         = 0,
        /// <summary>Create/read/update/delete of RouterOS records (load, save, delete). Supported by every transport.</summary>
        Crud         = 1,
        /// <summary>Live <c>/path/listen</c> change notifications (native on the API; emulated by poll+diff on CLI / WinBox M2).</summary>
        Listen       = 2,
        /// <summary>Streaming monitor windows (e.g. <c>/interface/monitor-traffic</c>, <c>/tool/torch</c>) that push successive snapshots.</summary>
        Streaming    = 4,
        /// <summary>Raw sentence access below the O/R mapper (direct <c>!re</c>/<c>!done</c>/<c>!trap</c> words).</summary>
        RawSentences = 8,
        /// <summary>Per-command tagging for multiplexed concurrent commands on a single channel (binary API <c>.tag</c>).</summary>
        Tagging      = 16,
        /// <summary>
        /// Transport can enter/commit/roll back RouterOS Safe Mode bound to this connection
        /// (<see cref="ITikConnection.SafeModeTake"/> / <see cref="ITikConnection.SafeModeRelease"/> /
        /// <see cref="ITikConnection.SafeModeUnroll"/> / <see cref="ITikConnection.SafeModeGet"/>).
        /// Requires a persistent, session-bound channel: binary API, a CLI terminal (via <c>Ctrl+X</c>, works on
        /// any RouterOS) or native WinBox M2 (RouterOS 7.18+). Stateless REST does not report it.
        /// </summary>
        SafeMode     = 32,
    }

    /// <summary>
    /// Optional interface implemented by connections that do not support all capabilities.
    /// Connections that do not implement this interface are assumed to support everything (backwards-compatible for ApiConnection).
    /// </summary>
    public interface ITikConnectionCapabilities
    {
        /// <summary>The set of capabilities this transport supports.</summary>
        TikConnectionCapability Capabilities { get; }
    }

    /// <summary>
    /// Extensions for capability checking.
    /// </summary>
    public static class TikConnectionCapabilityExtensions
    {
        /// <summary>
        /// Returns true if the connection supports the given capability.
        /// Connections that do not implement <see cref="ITikConnectionCapabilities"/> are assumed to support everything.
        /// </summary>
        public static bool Supports(this ITikConnection connection, TikConnectionCapability cap)
        {
            var caps = connection as ITikConnectionCapabilities;
            return caps == null || caps.Capabilities.HasFlag(cap);
        }
    }
}
