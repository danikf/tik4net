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
    /// Interface declaring which capabilities a transport supports. Every in-tree connection implements it
    /// with a positive declaration — including <see cref="tik4net.Api.ApiConnection"/>, which declares the
    /// full flag set. A connection that does <i>not</i> implement this interface is treated as supporting
    /// <b>nothing</b> (fail-closed): a transport must declare a capability to be allowed to use it.
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
        /// Returns true if the connection supports the given capability. Fail-closed: a connection that does
        /// not implement <see cref="ITikConnectionCapabilities"/> is treated as supporting nothing.
        /// </summary>
        public static bool Supports(this ITikConnection connection, TikConnectionCapability cap)
        {
            var caps = connection as ITikConnectionCapabilities;
            return caps != null && caps.Capabilities.HasFlag(cap);
        }

        /// <summary>
        /// Throws <see cref="TikConnectionCapabilityNotSupportedException"/> when the connection does not
        /// support <paramref name="cap"/>. Use to guard a feature entry point before attempting it.
        /// </summary>
        /// <param name="connection">The connection to check.</param>
        /// <param name="cap">The required capability.</param>
        /// <param name="feature">Optional short feature name shown in the exception message.</param>
        public static void Require(this ITikConnection connection, TikConnectionCapability cap, string feature = null)
        {
            if (!connection.Supports(cap))
                throw new TikConnectionCapabilityNotSupportedException(cap,
                    $"This transport does not support the '{cap}' capability"
                    + (feature != null ? $" ({feature})" : "") + ". "
                    + $"Use a transport that reports '{cap}' — see the capability matrix in the wiki.");
        }
    }
}
