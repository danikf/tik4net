using System;
using System.Collections.Generic;

namespace tik4net.Winbox
{
    /// <summary>
    /// Maps a RouterOS API path (e.g. <c>/ip/firewall/filter</c>) to a WinBox M2 handler array
    /// (e.g. <c>[20,3]</c>). Resolution order (highest priority first):
    /// <list type="number">
    ///   <item>session overrides (<c>WinboxNativeConnection.PathOverride</c>) — direct apiPath→handler;</item>
    ///   <item>the live <c>.jg</c>-derived menu map (<see cref="SetDerivedPaths"/>) under the exact apiPath
    ///         (the clean cases whose menu label equals the API leaf, e.g. <c>/ip/firewall/connection</c>);</item>
    ///   <item>a shipped <b>text alias</b> <c>apiPath → menu-label path</c>, resolved against the same live
    ///         derived map — for the irregular cases where the WinBox menu label differs from the API leaf
    ///         (<c>/ip/dns/static</c> → menu <c>/ip/dns/dns-static-entry</c>, <c>/system/resource</c> → menu
    ///         <c>/system/resources/resources</c>, …).</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <para>The design split follows the field resolver: the <b>volatile handler number</b> always comes
    /// live from the version-matched <c>.jg</c>; only the <b>stable text bridge</b> apiPath↔menu-label is
    /// shipped (and session-overridable). WinBox menu labels do not carry the RouterOS API leaf, so a fully
    /// alias-free map is not recoverable from the catalog — the alias tail below is the minimal,
    /// version-portable bridge for the irregular leaves.</para>
    /// <para>Major handler 20 is the generic nv/config handler; the minor selects the table
    /// (<c>[20,0]</c>=Interface, <c>[20,1]</c>=IP Address, …).</para>
    /// </remarks>
    internal sealed class WinboxHandlerMap
    {
        // Shipped text alias: apiPath → menu-label path (a key of the live .jg-derived map). Used when the
        // WinBox menu label does not normalize to the RouterOS API leaf. Keyed on stable English text, so it
        // carries across versions; the handler number itself is still read live from the .jg. Extend via
        // session PathOverride (direct apiPath→handler) for paths not covered here.
        private static readonly Dictionary<string, string> ShippedAlias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // ── Interfaces ──
            ["/interface"]                   = "/interfaces/interface",
            ["/interface/ethernet"]          = "/interfaces/interface", // no own window; ethernet is a tab of [20,0]
            ["/interface/list"]              = "/interfaces/interface-list",
            ["/interface/list/member"]       = "/interfaces/interface-list-member",

            // ── Interface bridge (bridge.jg-style menu rooted at top-level "Bridge", not "Interface") ──
            // The bridge LIST is an interface subtype: the "Bridge" menu's window inherits the generic
            // interface handler ([20,0]) and filters to type==12 (derived from the .jg, see WinboxJgCatalog
            // subtype filters). Its derived menu-label path is /bridge/bridge (menu 'Bridge' + window title 'Bridge').
            ["/interface/bridge"]            = "/bridge/bridge",
            ["/interface/bridge/port"]       = "/bridge/bridge-port",
            ["/interface/bridge/vlan"]       = "/bridge/bridge-vlan",
            ["/interface/bridge/host"]       = "/bridge/host",
            ["/interface/bridge/filter"]     = "/bridge/bridge-filter-rule",
            ["/interface/bridge/nat"]        = "/bridge/bridge-nat-rule",

            // ── IP ──
            ["/ip/address"]                  = "/ip/addresses/address",
            ["/ip/arp"]                      = "/ip/arp/arp",
            ["/ip/pool"]                     = "/ip/pool/ip-pool",
            ["/ip/route"]                    = "/ip/routes/route",
            ["/ip/service"]                  = "/ip/services/ip-service",
            ["/ip/dns"]                      = "/ip/dns/dns-settings",      // singleton
            ["/ip/dns/static"]               = "/ip/dns/dns-static-entry",

            // ── IP firewall (menu label "… Rule") ──
            ["/ip/firewall/filter"]          = "/ip/firewall/firewall-rule",
            ["/ip/firewall/nat"]             = "/ip/firewall/nat-rule",
            ["/ip/firewall/mangle"]          = "/ip/firewall/mangle-rule",
            ["/ip/firewall/raw"]             = "/ip/firewall/raw-rule",
            ["/ip/firewall/address-list"]    = "/ip/firewall/firewall-address-list",
            ["/ip/firewall/service-port"]    = "/ip/firewall/firewalling-service",

            // ── IP DHCP ──
            ["/ip/dhcp-server"]              = "/ip/dhcp-server/dhcp-server",
            ["/ip/dhcp-server/lease"]        = "/ip/dhcp-server/dhcp-lease",
            ["/ip/dhcp-server/network"]      = "/ip/dhcp-server/dhcp-network",
            ["/ip/dhcp-client"]              = "/ip/dhcp-client/dhcp-client",

            // ── IP hotspot (hotspot.jg, menu label "Hotspot …") ──
            ["/ip/hotspot/user"]             = "/ip/hotspot/hotspot-user",
            ["/ip/hotspot/user/profile"]     = "/ip/hotspot/hotspot-user-profile",
            ["/ip/hotspot/ip-binding"]       = "/ip/hotspot/hotspot-ip-binding",
            ["/ip/hotspot/active"]           = "/ip/hotspot/hotspot-active-user",
            ["/ip/hotspot/host"]             = "/ip/hotspot/hotspot-host",

            // ── PPP (ppp.jg, menu label "PPP …") ──
            ["/ppp/profile"]                 = "/ppp/ppp-profile",
            ["/ppp/secret"]                  = "/ppp/ppp-secret",
            ["/ppp/active"]                  = "/ppp/ppp-active-user",
            ["/ppp/aaa"]                     = "/ppp/ppp-authentication&accounting", // singleton

            // ── System ──
            ["/system/identity"]             = "/system/identity/identity",          // singleton
            ["/system/resource"]             = "/system/resources/resources",        // singleton
            ["/system/health"]               = "/system/health/health",
            ["/system/script"]               = "/system/scripts/script",
            ["/system/scheduler"]            = "/system/scheduler/schedule",

            // ── Log (the live System Log viewer: top-level "Log" menu → "Log Entry" window, handler [3,4]) ──
            ["/log"]                         = "/log/log-entry",

            // ── Routing ──
            ["/routing/bgp/connection"]      = "/routing/bgp/bgp-connection",
            ["/routing/bgp/template"]        = "/routing/bgp/bgp-template",

            // ── Tools (streaming monitors: type:'query' windows under the WinBox "Tools" menu, whose
            //    derived menu-label path doubles the leaf, e.g. menu 'Torch' + window title 'Torch') ──
            ["/tool/torch"]                  = "/tools/torch/torch",
            ["/tool/profile"]                = "/tools/profile/profile",
            // ToolPing's entity path is the top-level /ping (not /tool/ping); alias both forms.
            ["/ping"]                        = "/tools/ping/ping",
            ["/tool/ping"]                   = "/tools/ping/ping",
            ["/tool/traceroute"]             = "/tools/traceroute/traceroute",
            ["/tool/bandwidth-test"]         = "/tools/bandwidth-test/bandwidth-test",
            ["/tool/ip-scan"]                = "/tools/ip-scan/ip-scan",
            ["/tool/flood-ping"]             = "/tools/flood-ping/flood-ping",
        };

        private IReadOnlyDictionary<string, int[]> _derivedPaths;
        private IReadOnlyDictionary<string, Tuple<int, int>> _subtypeFilters;
        private readonly Dictionary<string, int[]> _overrides = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Supplies the <c>.jg</c>-derived <c>menu-label path → handler</c> map (from
        /// <see cref="WinboxJgCatalog.GetDerivedPaths"/>). Consulted after session overrides.
        /// </summary>
        internal void SetDerivedPaths(IReadOnlyDictionary<string, int[]> derived)
        {
            _derivedPaths = derived;
        }

        /// <summary>
        /// Supplies the <c>.jg</c>-derived interface subtype filters (<c>derived menu-label path →
        /// (typeKey, typeValue)</c>, from <see cref="WinboxJgCatalog.GetSubtypeFilters"/>). Resolved against the
        /// same apiPath→derived-key bridge as the handler.
        /// </summary>
        internal void SetSubtypeFilters(IReadOnlyDictionary<string, Tuple<int, int>> filters)
        {
            _subtypeFilters = filters;
        }

        /// <summary>
        /// Resolves the interface subtype filter for <paramref name="apiPath"/>, when the path maps (directly or
        /// via the shipped alias) to a subtype window. Returns <c>true</c> and the <c>typeKey</c>/<c>typeValue</c>
        /// the caller must match against each getall row; <c>false</c> for plain (non-subtype) paths. Session
        /// overrides bypass subtype filtering (an explicit handler is taken at face value).
        /// </summary>
        internal bool TryResolveSubtypeFilter(string apiPath, out int typeKey, out int typeValue)
        {
            typeKey = 0; typeValue = 0;
            if (_subtypeFilters == null || _subtypeFilters.Count == 0) return false;
            string key = Normalize(apiPath);
            if (_overrides.ContainsKey(key)) return false;

            string derivedKey = (_derivedPaths != null && _derivedPaths.ContainsKey(key)) ? key
                : (ShippedAlias.TryGetValue(key, out var menuPath) ? menuPath : null);
            if (derivedKey != null && _subtypeFilters.TryGetValue(derivedKey, out var f))
            {
                typeKey = f.Item1; typeValue = f.Item2; return true;
            }
            return false;
        }

        /// <summary>Registers a session override <c>apiPath → handler</c> (highest priority).</summary>
        internal void AddOverride(string apiPath, int[] handler)
        {
            _overrides[Normalize(apiPath)] = handler;
        }

        /// <summary>
        /// Resolves an API path to its handler array, or <c>null</c> when no session override, direct
        /// .jg-derived entry, or shipped alias matches.
        /// </summary>
        internal int[] Resolve(string apiPath)
        {
            string key = Normalize(apiPath);
            if (_overrides.TryGetValue(key, out var ov)) return ov;
            if (_derivedPaths != null)
            {
                // clean case: the menu label equals the API leaf (e.g. /ip/firewall/connection).
                if (_derivedPaths.TryGetValue(key, out var direct)) return direct;
                // irregular case: bridge apiPath → menu-label path, handler still live from the .jg.
                if (ShippedAlias.TryGetValue(key, out var menuPath)
                    && _derivedPaths.TryGetValue(menuPath, out var aliased)) return aliased;
            }
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
