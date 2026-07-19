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
        /// <summary>
        /// Live <c>/path/listen</c> change notifications (native on the API; emulated by poll+diff on CLI / WinBox M2).
        /// Also covers the async monitor pattern (<c>LoadAsync</c>/<c>ExecuteAsync</c>) for streaming-monitor commands
        /// (e.g. <c>/tool/torch</c>): native on the API; on CLI transports (Telnet/SSH/MACTelnet/WinBox CLI) most
        /// monitors are emulated by re-polling a one-shot <c>once</c>/<c>as-value</c> snapshot, but <c>/tool/torch</c>
        /// specifically needs a dedicated <c>freeze-frame-interval</c> + <c>proplist</c> builder instead — its
        /// <c>as-value</c> form prints nothing (see <see cref="tik4net.Cli.CliMonitorVerbs"/>); and on WinBox native
        /// (WinboxNative/WinboxNativeMac) via the <c>.jg</c> <c>type:'query'</c> monitor window, which returns typed
        /// M2 fields rather than text. <c>/tool/torch</c> is confirmed working live on every transport that reports
        /// this capability — API, all four CLI transports, and both WinBox native transports.
        /// </summary>
        Listen       = 2,
        /// <summary>Blocking, synchronous streaming reads on a single command execution (<c>ExecuteList*</c> /
        /// <c>ExecuteListWithDuration</c>) that push successive snapshots (e.g. <c>/interface/monitor-traffic</c>,
        /// <c>/tool/torch</c>). Binary API only — CLI/WinBox transports have no persistent multi-row read within
        /// one command exchange; use the async monitor pattern (<see cref="Listen"/>) there instead.</summary>
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
        /// <summary>
        /// Transport can run a <b>raw command pass-through</b> (<see cref="ITikConnection"/>.<c>CreateRawCommand</c>):
        /// a payload in the transport's own dialect sent verbatim, bypassing the structured command builder/mapper.
        /// The dialect is transport-specific — an API sentence (<c>\n</c>-separated words, lossless <c>!re</c> rows)
        /// on the binary API, a verbatim CLI line on the CLI transports. WinBox native does NOT report it (its raw
        /// form would be a numeric M2 message, not a string; use a CLI transport for raw over that channel).
        /// Distinct from <see cref="RawSentences"/> (read access to raw response sentences below the O/R mapper).
        /// </summary>
        RawCommand   = 64,
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
