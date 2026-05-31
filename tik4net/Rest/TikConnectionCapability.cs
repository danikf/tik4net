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
        None         = 0,
        Crud         = 1,
        Listen       = 2,
        Streaming    = 4,
        RawSentences = 8,
        Tagging      = 16,
    }

    /// <summary>
    /// Optional interface implemented by connections that do not support all capabilities.
    /// Connections that do not implement this interface are assumed to support everything (backwards-compatible for ApiConnection).
    /// </summary>
    public interface ITikConnectionCapabilities
    {
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
