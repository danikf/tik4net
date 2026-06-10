using System;
using System.Collections.Generic;

namespace tik4net.Winbox
{
    /// <summary>
    /// Maps a RouterOS API path (e.g. <c>/interface</c>) to a WinBox M2 handler array
    /// (e.g. <c>[20,0]</c>). Resolution order (highest priority first):
    /// <list type="number">
    ///   <item>session overrides (<c>WinboxNativeConnection.PathOverride</c>);</item>
    ///   <item>the live <c>.jg</c>-derived menu map (<see cref="SetDerivedPaths"/>) — every
    ///         <c>type:'map'/'query'</c> window node's <c>path:[…]</c> handler keyed by the normalized
    ///         <c>group</c> + node <c>name</c>;</item>
    ///   <item>a small shipped override tail for the irregular paths whose menu label does not
    ///         normalize to the API path (e.g. <c>/ip/firewall/filter</c>).</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Major handler 20 is the generic nv/config handler; the minor selects the table
    /// (<c>[20,0]</c>=Interface, <c>[20,1]</c>=IP Address, …). The volatile handler numbers come
    /// live from the <c>.jg</c>; only the irregular tail (where text normalization cannot reach the
    /// API path) is shipped — mirroring the field-resolver design (live id + stable text + override).
    /// </remarks>
    internal sealed class WinboxHandlerMap
    {
        // Shipped override tail: irregular paths whose .jg menu label does not normalize to the API
        // path, plus aliases for API paths that share a WinBox handler (no dedicated window node).
        private static readonly Dictionary<string, int[]> ShippedOverride = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
        {
            // Firewall: node name 'Firewall Rule' / title 'Filter Rules' under group 'IP' → won't normalize
            // to /ip/firewall/filter. (Handler [20,3] confirmed in roteros.jg 7.21.4.)
            ["/ip/firewall/filter"] = new[] { 20, 3 },
            // /interface/ethernet has no dedicated WinBox window — WinBox shows ethernet config as a tab of
            // the generic Interface window ([20,0]); the ethernet field subset lives under that handler.
            ["/interface/ethernet"] = new[] { 20, 0 },
        };

        private IReadOnlyDictionary<string, int[]> _derivedPaths;
        private readonly Dictionary<string, int[]> _overrides = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Supplies the <c>.jg</c>-derived <c>apiPath → handler</c> map (from
        /// <see cref="WinboxJgCatalog.GetDerivedPaths"/>). Consulted after session overrides and before
        /// the shipped tail.
        /// </summary>
        internal void SetDerivedPaths(IReadOnlyDictionary<string, int[]> derived)
        {
            _derivedPaths = derived;
        }

        /// <summary>Registers a session override <c>apiPath → handler</c> (highest priority).</summary>
        internal void AddOverride(string apiPath, int[] handler)
        {
            _overrides[Normalize(apiPath)] = handler;
        }

        /// <summary>
        /// Resolves an API path to its handler array, or <c>null</c> when no session override, .jg-derived
        /// entry, or shipped override matches.
        /// </summary>
        internal int[] Resolve(string apiPath)
        {
            string key = Normalize(apiPath);
            if (_overrides.TryGetValue(key, out var ov)) return ov;
            if (_derivedPaths != null && _derivedPaths.TryGetValue(key, out var dyn)) return dyn;
            if (ShippedOverride.TryGetValue(key, out var seed)) return seed;
            return null;
        }

        // Strip a trailing verb segment (/interface/print → /interface) and the leading/trailing slash noise.
        internal static string Normalize(string apiPath)
        {
            if (string.IsNullOrWhiteSpace(apiPath)) return "";
            string p = apiPath.Trim();
            if (!p.StartsWith("/")) p = "/" + p;
            return p.TrimEnd('/');
        }
    }
}
