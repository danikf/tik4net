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
    ///   <item>a session <b>text alias</b> (<c>WinboxNativeConnection.PathAlias</c>) <c>apiPath → menu-label
    ///         path</c>, resolved against the same live derived map — the preferred, version-portable way to
    ///         teach the mapper a path, since it is written in the labels the WinBox GUI shows;</item>
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
            // Ethernet IS its own subtype window (type==1), not a tab of the generic list: pointing the alias
            // at /interfaces/interface returned every interface on the router — 6 rows where the API returns
            // 1 — and decoded them with the generic field map, so the ethernet-only fields (auto-negotiation,
            // advertise, cable-settings, loop-protect*, …) were simply absent. Measured live on 7.23.2.
            ["/interface/ethernet"]          = "/interfaces/ethernet",
            ["/interface/list"]              = "/interfaces/interface-list",
            ["/interface/list/member"]       = "/interfaces/interface-list-member",

            // Interface SUBTYPES. None of these is a handler of its own: they all read the generic interface
            // table [20,0] and are told apart by the numeric `type` field, which the .jg declares per window
            // (see WinboxJgCatalog's inherit/typevalue harvest). The alias only has to name the window; the
            // handler AND the type filter both come live from the catalog. The API leaf and the WinBox window
            // title disagree often enough ("eoip" ↔ "EoIP Tunnel", "ipip" ↔ "IP Tunnel", "l2tp-client" ↔
            // "L2TP Client") that no normalizer recovers them — see the class remarks.
            ["/interface/bonding"]           = "/interfaces/bonding",
            ["/interface/eoip"]              = "/interfaces/eoip-tunnel",
            ["/interface/gre"]               = "/interfaces/gre-tunnel",
            ["/interface/ipip"]              = "/interfaces/ip-tunnel",
            ["/interface/lte"]               = "/interfaces/lte",
            ["/interface/macvlan"]           = "/interfaces/macvlan",
            ["/interface/veth"]              = "/interfaces/veth",
            ["/interface/vlan"]              = "/interfaces/vlan",
            ["/interface/vrrp"]              = "/interfaces/vrrp",
            ["/interface/vxlan"]             = "/interfaces/vxlan",
            ["/interface/wireguard"]         = "/wireguard/wireguard",
            ["/interface/wireguard/peers"]   = "/wireguard/wireguard-peer",

            // PPP tunnels (ppp.jg). The CLIENT interfaces are interface subtypes under the PPP menu; the
            // SERVER singletons are their own handlers ([27,x]). PPPoE is the odd one out: its "server" is a
            // list of service instances ('PPPoE Service'), not a singleton — which is exactly what the API's
            // /interface/pppoe-server/server is.
            ["/interface/l2tp-client"]       = "/ppp/l2tp-client",
            ["/interface/ovpn-client"]       = "/ppp/ovpn-client",
            ["/interface/pppoe-client"]      = "/ppp/pppoe-client",
            ["/interface/pptp-client"]       = "/ppp/pptp-client",
            ["/interface/sstp-client"]       = "/ppp/sstp-client",
            ["/interface/l2tp-server/server"]  = "/ppp/l2tp-server",     // singleton
            ["/interface/ovpn-server/server"]  = "/ppp/ovpn-server",
            ["/interface/pppoe-server/server"] = "/ppp/pppoe-service",
            ["/interface/pptp-server/server"]  = "/ppp/pptp-server",     // singleton
            ["/interface/sstp-server/server"]  = "/ppp/sstp-server",     // singleton

            // Legacy wireless (wlan6.jg). The interface list is the 'Wireless' subtype window; the rest are
            // handlers under the Wireless menu.
            ["/interface/wireless"]                   = "/wireless/wireless/wireless",
            ["/interface/wireless/access-list"]       = "/wireless/wireless/ap-access-rule",
            ["/interface/wireless/channels"]          = "/wireless/wireless/wireless-channel",
            ["/interface/wireless/connect-list"]      = "/wireless/wireless/station-connect-rule",
            ["/interface/wireless/registration-table"] = "/wireless/wireless/ap-client",
            ["/interface/wireless/security-profiles"] = "/wireless/wireless/security-profile",
            ["/interface/wireless/sniffer"]           = "/wireless/wireless/wireless-sniffer",

            // wifiwave2 (wave2.jg) — the WinBox menu drops the "wifi-" prefix on some leaves and keeps it on
            // others, so both spellings are needed.
            ["/interface/wifi"]                    = "/wifi/wifi",
            ["/interface/wifi/access-list"]        = "/wifi/access-list",
            ["/interface/wifi/aaa"]                = "/wifi/wifi-aaa",
            ["/interface/wifi/capsman"]            = "/wifi/capsman",
            ["/interface/wifi/cap"]                = "/wifi/cap",
            ["/interface/wifi/channel"]            = "/wifi/wifi-channel",
            ["/interface/wifi/configuration"]      = "/wifi/wifi-configuration",
            ["/interface/wifi/datapath"]           = "/wifi/wifi-datapath",
            ["/interface/wifi/interworking"]       = "/wifi/wifi-interworking",
            ["/interface/wifi/provisioning"]       = "/wifi/provisioning",
            ["/interface/wifi/radio"]              = "/wifi/radio",
            ["/interface/wifi/registration-table"] = "/wifi/registration",
            ["/interface/wifi/security"]           = "/wifi/wifi-security",
            ["/interface/wifi/steering"]           = "/wifi/wifi-steering",

            // CAPsMAN (wlan6.jg, menu Wireless ▸ CAPsMAN — every window label is prefixed "CAPs …").
            ["/caps-man/aaa"]                  = "/wireless/capsman/caps-aaa",
            ["/caps-man/access-list"]          = "/wireless/capsman/caps-access-rule",
            ["/caps-man/channel"]              = "/wireless/capsman/caps-channel",
            ["/caps-man/configuration"]        = "/wireless/capsman/caps-configuration",
            ["/caps-man/datapath"]             = "/wireless/capsman/caps-datapath-configuration",
            ["/caps-man/interface"]            = "/wireless/capsman/cap-interface",
            ["/caps-man/manager"]              = "/wireless/capsman/caps-manager",
            ["/caps-man/manager/interface"]    = "/wireless/capsman/caps-manager-interface",
            ["/caps-man/provisioning"]         = "/wireless/capsman/caps-provisioning",
            ["/caps-man/radio"]                = "/wireless/capsman/caps-radio",
            ["/caps-man/rates"]                = "/wireless/capsman/caps-rate",
            ["/caps-man/registration-table"]   = "/wireless/capsman/caps-ap-client",
            ["/caps-man/remote-cap"]           = "/wireless/capsman/caps-remote-ap",
            ["/caps-man/security"]             = "/wireless/capsman/caps-security-configuration",

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
            ["/interface/bridge/settings"]   = "/bridge/bridge-settings",   // singleton
            ["/interface/bridge/mdb"]        = "/bridge/bridge-mdb-entry",
            ["/interface/bridge/msti"]       = "/bridge/bridge-msti",

            // ── IP ──
            ["/ip/address"]                  = "/ip/addresses/address",
            ["/ip/arp"]                      = "/ip/arp/arp",
            ["/ip/pool"]                     = "/ip/pool/ip-pool",
            ["/ip/route"]                    = "/ip/routes/route",
            ["/ip/service"]                  = "/ip/services/ip-service",
            ["/ip/dns"]                      = "/ip/dns/dns-settings",      // singleton
            ["/ip/dns/static"]               = "/ip/dns/dns-static-entry",
            ["/ip/dns/cache"]                = "/ip/dns/dns-entry",
            ["/ip/dns/cache/all"]            = "/ip/dns/dns-entry",
            ["/ip/cloud"]                    = "/ip/cloud/cloud",           // singleton
            ["/ip/neighbor"]                 = "/ip/neighbors/neighbour",   // WinBox spells it "Neighbour"
            ["/ip/settings"]                 = "/ip/settings/ip-settings",  // singleton
            ["/ip/socks"]                    = "/ip/socks/socks-settings",  // singleton
            ["/ip/ssh"]                      = "/ip/ssh/ssh-settings",      // singleton
            ["/ip/smb"]                      = "/ip/smb/smb-settings",      // singleton
            ["/ip/smb/shares"]               = "/ip/smb/smb-share",
            ["/ip/smb/users"]                = "/ip/smb/smb-user",
            ["/ip/traffic-flow"]             = "/ip/traffic-flow/traffic-flow-settings", // singleton
            ["/ip/traffic-flow/target"]      = "/ip/traffic-flow/traffic-flow-target",
            ["/ip/upnp"]                     = "/ip/upnp/upnp-settings",    // singleton
            // Same handler [28,0] as the settings singleton above, different window: the per-interface list.
            // Resolved as a list because singleton-ness is read per window (WinboxJgCatalog._singletonPaths).
            ["/ip/upnp/interfaces"]          = "/ip/upnp/upnp",
            ["/ip/vrf"]                      = "/ip/vrf/vrf",
            ["/ip/proxy"]                    = "/ip/web-proxy/web-proxy-settings", // singleton; menu "Web Proxy"
            ["/ip/proxy/access"]             = "/ip/web-proxy/web-proxy-rule",

            // ── IP firewall (menu label "… Rule") ──
            ["/ip/firewall/filter"]          = "/ip/firewall/firewall-rule",
            ["/ip/firewall/nat"]             = "/ip/firewall/nat-rule",
            ["/ip/firewall/mangle"]          = "/ip/firewall/mangle-rule",
            ["/ip/firewall/raw"]             = "/ip/firewall/raw-rule",
            ["/ip/firewall/address-list"]    = "/ip/firewall/firewall-address-list",
            ["/ip/firewall/service-port"]    = "/ip/firewall/firewalling-service",
            ["/ip/firewall/layer7-protocol"] = "/ip/firewall/firewall-l7-protocol",
            ["/ip/firewall/connection/tracking"] = "/ip/firewall/connection-tracking", // singleton

            // ── IP DHCP ──
            ["/ip/dhcp-server"]              = "/ip/dhcp-server/dhcp-server",
            ["/ip/dhcp-server/lease"]        = "/ip/dhcp-server/dhcp-lease",
            ["/ip/dhcp-server/network"]      = "/ip/dhcp-server/dhcp-network",
            ["/ip/dhcp-client"]              = "/ip/dhcp-client/dhcp-client",
            ["/ip/dhcp-client/option"]       = "/ip/dhcp-client/dhcp-client-option",
            ["/ip/dhcp-server/alert"]        = "/ip/dhcp-server/dhcp-alert",
            ["/ip/dhcp-server/config"]       = "/ip/dhcp-server/dhcp-config",  // singleton
            ["/ip/dhcp-server/option"]       = "/ip/dhcp-server/dhcp-option",
            ["/ip/dhcp-relay"]               = "/ip/dhcp-relay/dhcp-relay",

            // ── IP IPsec (menu labels all carry the "IPsec " prefix) ──
            ["/ip/ipsec/active-peers"]       = "/ip/ipsec/ipsec-remote-peer",
            ["/ip/ipsec/group"]              = "/ip/ipsec/ipsec-group",
            ["/ip/ipsec/identity"]           = "/ip/ipsec/ipsec-identity",
            ["/ip/ipsec/installed-sa"]       = "/ip/ipsec/ipsec-sa",
            ["/ip/ipsec/key"]                = "/ip/ipsec/ipsec-key",
            ["/ip/ipsec/key/rsa"]            = "/ip/ipsec/ipsec-key",
            ["/ip/ipsec/mode-config"]        = "/ip/ipsec/ipsec-mode-config",
            ["/ip/ipsec/peer"]               = "/ip/ipsec/ipsec-peer",
            ["/ip/ipsec/policy"]             = "/ip/ipsec/ipsec-policy",
            ["/ip/ipsec/profile"]            = "/ip/ipsec/ipsec-profile",
            ["/ip/ipsec/proposal"]           = "/ip/ipsec/ipsec-proposal",
            ["/ip/ipsec/settings"]           = "/ip/ipsec/ipsec-user-settings", // singleton

            // ── IP hotspot (hotspot.jg, menu label "Hotspot …") ──
            ["/ip/hotspot/user"]             = "/ip/hotspot/hotspot-user",
            ["/ip/hotspot/user/profile"]     = "/ip/hotspot/hotspot-user-profile",
            ["/ip/hotspot/ip-binding"]       = "/ip/hotspot/hotspot-ip-binding",
            ["/ip/hotspot/active"]           = "/ip/hotspot/hotspot-active-user",
            ["/ip/hotspot/host"]             = "/ip/hotspot/hotspot-host",
            ["/ip/hotspot"]                  = "/ip/hotspot/hotspot-server",
            ["/ip/hotspot/profile"]          = "/ip/hotspot/hotspot-server-profile",
            ["/ip/hotspot/service-port"]     = "/ip/hotspot/hotspot-service-port",
            ["/ip/hotspot/cookie"]           = "/ip/hotspot/cookies",
            ["/ip/hotspot/walled-garden"]    = "/ip/hotspot/walled-garden-entry",
            ["/ip/hotspot/walled-garden/ip"] = "/ip/hotspot/walled-garden-ip-entry",

            // ── PPP (ppp.jg, menu label "PPP …") ──
            ["/ppp/profile"]                 = "/ppp/ppp-profile",
            ["/ppp/secret"]                  = "/ppp/ppp-secret",
            ["/ppp/active"]                  = "/ppp/ppp-active-user",
            ["/ppp/aaa"]                     = "/ppp/ppp-authentication&accounting", // singleton

            // ── System ──
            ["/system/identity"]             = "/system/identity/identity",          // singleton
            ["/system/resource"]             = "/system/resources/resources",        // singleton
            ["/system/health"]               = "/system/health/health",
            // The login-banner singleton: menu 'System ▸ Note', but the window is titled 'System Note'
            // ([24,33]), so the derived leaf doubles the menu word and the plain /system/note/note guess
            // misses it.
            ["/system/note"]                 = "/system/note/system-note",       // singleton
            ["/system/script"]               = "/system/scripts/script",
            ["/system/script/job"]           = "/system/scripts/job",
            ["/system/script/environment"]   = "/system/scripts/variable",
            ["/system/scheduler"]            = "/system/scheduler/schedule",
            ["/system/clock"]                = "/system/clock/clock",              // singleton
            ["/system/leds"]                 = "/system/leds/led-trigger",
            ["/system/leds/settings"]        = "/system/leds/led-settings",        // singleton
            ["/system/logging"]              = "/system/logging/log-rule",
            ["/system/logging/action"]       = "/system/logging/log-action",
            ["/system/ntp/client"]           = "/system/ntp-client/ntp-client",    // singleton
            ["/system/ntp/server"]           = "/system/ntp-server/ntp-server",    // singleton
            ["/system/package"]              = "/system/packages/package",
            ["/system/routerboard"]          = "/system/routerboard/routerboard",  // singleton
            ["/system/watchdog"]             = "/system/watchdog/watchdog",        // singleton
            ["/system/license"]              = "/system/license/license",          // singleton
            ["/system/console"]              = "/system/console/console",
            ["/system/history"]              = "/system/history/history",
            ["/system/upgrade/mirror"]       = "/system/packages/upgrade-mirror",  // singleton

            // ── Users / certificates / files (top-level API paths, nested WinBox menus) ──
            ["/user"]                        = "/system/users/user",
            ["/user/group"]                  = "/system/users/group",
            ["/user/active"]                 = "/system/users/active-user",
            ["/user/aaa"]                    = "/system/users/login-authentication&accounting", // singleton
            ["/user/ssh-keys"]               = "/system/users/ssh-keys",
            ["/user/ssh-keys/private"]       = "/system/users/ssh-private-keys",
            ["/certificate"]                 = "/system/certificates/certificate",
            ["/certificate/settings"]        = "/system/certificates/certificate-settings", // singleton
            ["/certificate/crl"]             = "/system/certificates/crl",
            // The Dude serves a 'file' window of its own (/dude/files/file) — name the router's Files menu.
            ["/file"]                        = "/files/file",
            ["/disk"]                        = "/system/disks/disk",
            ["/port"]                        = "/system/ports/port",
            ["/port/remote-access"]          = "/system/ports/remote-port",
            ["/radius"]                      = "/radius/radius-server",
            ["/radius/incoming"]             = "/radius/radius-incoming",           // singleton
            // SNMP is a top-level API path but sits under the WinBox IP menu.
            ["/snmp"]                        = "/ip/snmp/snmp-settings",            // singleton
            ["/snmp/community"]              = "/ip/snmp/snmp-community",

            // ── Queues ──
            ["/queue/simple"]                = "/queues/simple-queue",
            ["/queue/tree"]                  = "/queues/queue",
            ["/queue/type"]                  = "/queues/queue-type",
            ["/queue/interface"]             = "/queues/interface-queue",

            // ── Log (the live System Log viewer: top-level "Log" menu → "Log Entry" window, handler [3,4]) ──
            ["/log"]                         = "/log/log-entry",

            // ── Routing ──
            ["/routing/bgp/connection"]      = "/routing/bgp/bgp-connection",
            ["/routing/bgp/template"]        = "/routing/bgp/bgp-template",
            ["/routing/bgp/session"]         = "/routing/bgp/bgp-session",
            ["/routing/filter/rule"]         = "/routing/filters/routing-filter-rule",
            ["/routing/filter/community-list"] = "/routing/filters/community-set",
            // NOT /routing/filters/routing-filter-rule: routing RULES are the policy-routing table, a
            // different window under the Rules menu. The two labels both end in "rule" — this is the pair a
            // leaf-matching normalizer resolved backwards, which is why the map stays an explicit table.
            ["/routing/rule"]                = "/routing/rules/policy-routing-rule",
            ["/routing/table"]               = "/routing/tables/routing-table",
            ["/routing/settings"]            = "/routing/settings/routing-settings",  // singleton
            ["/routing/id"]                  = "/routing/router-id/router-id",
            ["/routing/nexthop"]             = "/routing/nexthops/nexthop",
            ["/routing/ospf/area"]           = "/routing/ospf/ospf-area",
            ["/routing/ospf/area/range"]     = "/routing/ospf/ospf-area-range",
            ["/routing/ospf/instance"]       = "/routing/ospf/ospf-instance",
            ["/routing/ospf/interface"]      = "/routing/ospf/ospf-interface",
            ["/routing/ospf/interface-template"] = "/routing/ospf/ospf-interface-template",
            ["/routing/ospf/lsa"]            = "/routing/ospf/ospf-lsa",
            ["/routing/ospf/neighbor"]       = "/routing/ospf/ospf-neighbor",
            ["/routing/ospf/static-neighbor"] = "/routing/ospf/ospf-static-neighbor",
            ["/routing/rip/instance"]        = "/routing/rip/rip-instance",
            ["/routing/rip/interface"]       = "/routing/rip/rip-interface",
            ["/routing/rip/neighbor"]        = "/routing/rip/rip-neighbor",
            ["/routing/igmp-proxy/interface"] = "/routing/igmp-proxy/igmp-proxy-interface",
            ["/routing/bfd/configuration"]   = "/routing/bfd/bfd-configuration",
            ["/routing/bfd/session"]         = "/routing/bfd/bfd-session",

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

            // ── Netwatch (advtool.jg, menu 'Tools/Netwatch' → window 'Netwatch Host', handler [51,1]) ──
            ["/tool/netwatch"]               = "/tools/netwatch/netwatch-host",

            // ── Other Tools windows ──
            // "Bandwidth Server" is the btest SERVER settings singleton; /tool/bandwidth-test above is the
            // client action window.
            ["/tool/bandwidth-server"]       = "/tools/btest-server/btest-server-settings", // singleton
            ["/tool/e-mail"]                 = "/tools/email/email-settings",               // singleton
            // /tool/graphing/<x> is the graphing RULE list ("<X> Graphing Rule"); the "<X> Graph" windows are
            // the collected graphs, which the API exposes as .../<x>/print with different fields.
            ["/tool/graphing"]               = "/tools/graphing/graphing-settings",         // singleton
            ["/tool/graphing/interface"]     = "/tools/graphing/interface-graphing-rule",
            ["/tool/graphing/queue"]         = "/tools/graphing/queue-graphing-rule",
            ["/tool/graphing/resource"]      = "/tools/graphing/resource-graphing-rule",
            ["/tool/mac-server"]             = "/tools/mac-server/mac-telnet-server",       // singleton
            ["/tool/mac-server/mac-winbox"]  = "/tools/mac-server/mac-winbox-server",       // singleton
            ["/tool/mac-server/ping"]        = "/tools/mac-server/mac-ping-server",         // singleton
            ["/tool/mac-server/sessions"]    = "/tools/mac-server/mac-session",
            ["/tool/romon"]                  = "/tools/romon/romon-settings",               // singleton
            ["/tool/romon/port"]             = "/tools/romon/romon-port",
            ["/tool/sniffer"]                = "/tools/packet-sniffer/packet-sniffer-settings", // singleton
            ["/tool/sniffer/host"]           = "/tools/packet-sniffer/sniffer-host",
            ["/tool/sniffer/packet"]         = "/tools/packet-sniffer/sniffer-packet",
            ["/tool/sniffer/protocol"]       = "/tools/packet-sniffer/sniffer-protocol",
            ["/tool/sniffer/connection"]     = "/tools/packet-sniffer/sniffer-connection",
            ["/tool/traffic-generator/packet-template"] = "/tools/traffic-generator/packet-template",
            ["/tool/traffic-generator/port"] = "/tools/traffic-generator/traffic-generator-port",
            ["/tool/traffic-generator/stream"] = "/tools/traffic-generator/packet-stream",

            // ── Wake on LAN (advtool.jg, menu 'Tools/WOL' → window 'Wake on LAN', handler [82]) ──
            //    A standalone type:'doit' window: no records, one action (cmd 1) taking the MAC address and
            //    an optional interface as fields. Invoked via ExecuteNonQuery, see RunActionWindow.
            ["/tool/wol"]                    = "/tools/wol/wake-on-lan",
        };

        private IReadOnlyDictionary<string, int[]> _derivedPaths;
        private IReadOnlyDictionary<string, Tuple<int, int>> _subtypeFilters;
        private readonly Dictionary<string, int[]> _overrides = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        // Session text alias apiPath → menu-label path; same role as ShippedAlias, resolved just before it.
        private readonly Dictionary<string, string> _aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// When <c>true</c>, a path that does not resolve verbatim is retried with each segment passed through the
        /// WinBox label normalizer (GUI-name addressing: <c>/IP/Firewall/Filter</c> or <c>/ip/firewall_filter</c>
        /// segments → <c>/ip/firewall/filter</c>). Opt-in; mirrors the field resolver's GUI-name retry.
        /// </summary>
        internal bool UseGuiNames { get; set; }

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
                : _aliases.TryGetValue(key, out var sessionMenuPath) ? sessionMenuPath
                : (ShippedAlias.TryGetValue(key, out var menuPath) ? menuPath : null);
            if (derivedKey != null && _subtypeFilters.TryGetValue(derivedKey, out var f))
            {
                typeKey = f.Item1; typeValue = f.Item2; return true;
            }
            return false;
        }

        /// <summary>
        /// The derived menu-label key <paramref name="apiPath"/> resolves through (itself when it is already a
        /// derived key, otherwise the session or shipped alias target), or <c>null</c> when it resolves by a
        /// handler override or not at all. Lets a caller ask the catalog about the specific WINDOW behind a
        /// path — e.g. whether it is a singleton — rather than about its handler, which several windows share.
        /// </summary>
        internal string ResolveDerivedKey(string apiPath)
        {
            string key = Normalize(apiPath);
            if (_overrides.ContainsKey(key)) return null;
            if (_derivedPaths == null) return null;
            if (_derivedPaths.ContainsKey(key)) return key;
            if (_aliases.TryGetValue(key, out var sessionMenuPath) && _derivedPaths.ContainsKey(sessionMenuPath))
                return sessionMenuPath;
            if (ShippedAlias.TryGetValue(key, out var menuPath) && _derivedPaths.ContainsKey(menuPath))
                return menuPath;
            if (UseGuiNames)
            {
                string guiKey = NormalizeGui(apiPath);
                if (!string.Equals(guiKey, key, StringComparison.OrdinalIgnoreCase))
                    return ResolveDerivedKey(guiKey);
            }
            return null;
        }

        /// <summary>Registers a session override <c>apiPath → handler</c> (highest priority).</summary>
        internal void AddOverride(string apiPath, int[] handler)
        {
            _overrides[Normalize(apiPath)] = handler;
        }

        /// <summary>
        /// Registers a session <b>text</b> alias <c>apiPath → WinBox menu-label path</c> (e.g.
        /// <c>/ppp/secret → /ppp/secrets/ppp-secret</c>). The handler number is still read live from the
        /// version-matched <c>.jg</c>, so unlike <see cref="AddOverride"/> the mapping survives a RouterOS upgrade.
        /// Resolved after session handler overrides and the direct derived map, before the shipped alias tail.
        /// </summary>
        internal void AddAlias(string apiPath, string winboxMenuPath)
        {
            _aliases[Normalize(apiPath)] = Normalize(winboxMenuPath);
        }

        /// <summary>
        /// Resolves an API path to its handler array, or <c>null</c> when no session override, direct
        /// .jg-derived entry, or shipped alias matches.
        /// </summary>
        internal int[] Resolve(string apiPath)
        {
            string key = Normalize(apiPath);
            if (TryResolve(key, out var handler)) return handler;

            // GUI-name addressing (opt-in): retry with each segment label-normalized so a path copied from the
            // WinBox menu tree ("/IP/Firewall/Filter", underscores, mixed case) resolves to its API path.
            if (UseGuiNames)
            {
                string guiKey = NormalizeGui(apiPath);
                if (!string.Equals(guiKey, key, StringComparison.OrdinalIgnoreCase)
                    && TryResolve(guiKey, out var guiHandler))
                    return guiHandler;
            }
            return null;
        }

        // Single resolution attempt for an already-normalized path key: session override → direct .jg-derived
        // entry → shipped apiPath→menu-label alias against the same derived map. False when none match.
        private bool TryResolve(string key, out int[] handler)
        {
            if (_overrides.TryGetValue(key, out handler)) return true;
            if (_derivedPaths != null)
            {
                // clean case: the menu label equals the API leaf (e.g. /ip/firewall/connection).
                if (_derivedPaths.TryGetValue(key, out handler)) return true;
                // session text alias (PathAlias): apiPath → menu-label path, handler still live from the .jg.
                if (_aliases.TryGetValue(key, out var sessionMenuPath)
                    && _derivedPaths.TryGetValue(sessionMenuPath, out handler)) return true;
                // irregular case: bridge apiPath → menu-label path, handler still live from the .jg.
                if (ShippedAlias.TryGetValue(key, out var menuPath)
                    && _derivedPaths.TryGetValue(menuPath, out handler)) return true;
            }
            handler = null;
            return false;
        }

        // GUI-name path normalizer: lower-case + slash-trim (Normalize) plus per-segment label normalization
        // (underscores/spaces/dots → API form), so "/IP/Firewall_Filter" → "/ip/firewall-filter".
        private static string NormalizeGui(string apiPath)
        {
            string p = Normalize(apiPath);
            var segs = p.Split('/');
            for (int i = 0; i < segs.Length; i++)
                if (segs[i].Length > 0)
                    segs[i] = WinboxFieldResolver.NormalizeLabel(segs[i]);
            return string.Join("/", segs);
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
