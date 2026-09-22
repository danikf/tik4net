using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// Every enum-typed <c>[TikProperty]</c> in the mapper, against the value list the live router offers
    /// for that field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One unmapped value does not cost a property — <c>TikEnumMetadata.Parse</c> throws
    /// <c>FormatException</c> and the mapper fails the read of the <b>whole</b> menu. That is how
    /// <c>mark-routing</c> made every mangle table with a policy-routing rule unreadable since v1.2.0, and
    /// how <c>mstp</c> did the same to <c>/interface/bridge</c>. Both were found by a user, not by a test:
    /// nothing here read existing rows, and the lab router had no row using the missing value.
    /// </para>
    /// <para>
    /// <b>The lists are the router's own.</b> They were taken on RouterOS 7.24.3 by Tab-completing
    /// <c>&lt;menu&gt; add &lt;field&gt;=</c> (or <c>set</c> for a singleton) over Telnet — which enumerates
    /// what the menu ACCEPTS, not what some row happens to hold. Re-measure on a new RouterOS with
    /// <c>mikrotik_cli_complete</c> (the <c>mikrotik</c> skill) and add what it reports. A value the router
    /// drops is not a failure here: this asserts we can read everything it offers, not that the two lists
    /// are identical.
    /// </para>
    /// <para>
    /// <b>What completion cannot answer, and is therefore not covered here.</b> Where all values share a
    /// prefix the router completes it inline instead of listing, so those fields were re-probed with the
    /// prefix. Three <c>/interface/pppoe-client</c> fields accept <c>yes</c>/<c>no</c> but read back
    /// <c>true</c>/<c>false</c> on both Api and Telnet (measured on a temporary row), so their accepted
    /// spelling is not a reading defect. <c>/interface/ethernet</c> <c>flow-control-tx</c> and
    /// <c>flow-control-rx</c> are not RouterOS 7 fields at all (<i>unknown parameter</i>; the live ones are
    /// <c>tx-flow-control</c> / <c>rx-flow-control</c>, <c>auto|on|off</c>). <c>/interface/lte</c> has no
    /// hardware on the lab, and <c>/ip/ipsec/active-peers</c> <c>side</c> and
    /// <c>/routing/ospf/neighbor</c> <c>state</c> are read-only menus with nothing to complete against.
    /// <c>/interface/lte</c> <c>sms-protocol</c> IS covered — the menu completes it even with no modem;
    /// only <c>network-mode</c>, which needs a row to <c>set</c> against, is not.
    /// </para>
    /// </remarks>
    [TestClass]
    public class EntityEnumVocabularyTests
    {
        private sealed class Vocabulary
        {
            public Vocabulary(string menu, string field, Type enumType, params string[] routerValues)
            {
                Menu = menu; Field = field; EnumType = enumType; RouterValues = routerValues;
            }

            public string Menu { get; }
            public string Field { get; }
            public Type EnumType { get; }
            public string[] RouterValues { get; }
        }

        /// <summary>RouterOS 7.24.3, one entry per enum-typed mapped property.</summary>
        private static readonly Vocabulary[] Vocabularies =
        {
            new Vocabulary("/caps-man/access-list", "action", typeof(tik4net.Objects.CapsMan.CapsManAccessList.CapsManAccessListAction),
                "accept", "query-radius", "reject"),
            new Vocabulary("/caps-man/access-list", "vlan-mode", typeof(tik4net.Objects.CapsMan.CapsManAccessList.CapsManAccessListVlanMode),
                "no-tag", "use-service-tag", "use-tag"),
            new Vocabulary("/caps-man/channel", "extension-channel", typeof(tik4net.Objects.CapsMan.CapsManChannel.ExtensionChannelType),
                "Ce", "Ceee", "Ceeeeeee", "XX", "XXXX", "XXXXXXXX", "disabled", "eC", "eCee", "eCeeeeee", "eeCe", "eeCeeeee", "eeeC", "eeeCeeee", "eeeeCeee", "eeeeeCee", "eeeeeeCe", "eeeeeeeC"),
            new Vocabulary("/caps-man/configuration", "guard-interval", typeof(tik4net.Objects.CapsMan.CapsManConfiguration.GuardIntervalType),
                "any", "long"),
            new Vocabulary("/caps-man/configuration", "hw-protection-mode", typeof(tik4net.Objects.CapsMan.CapsManConfiguration.HwProtectionModeType),
                "cts-to-self", "none", "rts-cts"),
            new Vocabulary("/caps-man/configuration", "installation", typeof(tik4net.Objects.CapsMan.CapsManConfiguration.InstallationType),
                "any", "indoor", "outdoor"),
            new Vocabulary("/caps-man/configuration", "keepalive-frames", typeof(tik4net.Objects.CapsMan.CapsManConfiguration.KeepaliveFramesMode),
                "disabled", "enabled"),
            new Vocabulary("/caps-man/configuration", "mode", typeof(tik4net.Objects.CapsMan.CapsManConfiguration.OperatingMode),
                "ap"),
            new Vocabulary("/caps-man/configuration", "multicast-helper", typeof(tik4net.Objects.CapsMan.CapsManConfiguration.MulticastHelperMode),
                "default", "dhcp", "disabled", "full"),
            new Vocabulary("/caps-man/datapath", "arp", typeof(tik4net.Objects.CapsMan.CapsManDatapath.ArpMode),
                "disabled", "enabled", "local-proxy-arp", "proxy-arp", "reply-only"),
            new Vocabulary("/caps-man/datapath", "vlan-mode", typeof(tik4net.Objects.CapsMan.CapsManDatapath.VlanModeType),
                "no-tag", "use-service-tag", "use-tag"),
            new Vocabulary("/caps-man/interface", "arp", typeof(tik4net.Objects.CapsMan.CapsManInterface.ArpMode),
                "disabled", "enabled", "local-proxy-arp", "proxy-arp", "reply-only"),
            new Vocabulary("/caps-man/manager", "upgrade-policy", typeof(tik4net.Objects.CapsMan.CapsManManager.UpgradePolicyType),
                "none", "require-same-version", "suggest-same-version"),
            new Vocabulary("/caps-man/provisioning", "action", typeof(tik4net.Objects.CapsMan.CapsManProvisioning.CapsManProvisioningAction),
                "create-disabled", "create-dynamic-enabled", "create-enabled", "none"),
            new Vocabulary("/caps-man/provisioning", "name-format", typeof(tik4net.Objects.CapsMan.CapsManProvisioning.CapsManProvisioningNameFormat),
                "cap", "identity", "prefix", "prefix-identity"),
            new Vocabulary("/caps-man/security", "group-encryption", typeof(tik4net.Objects.CapsMan.CapsManSecurity.GroupEncryptionType),
                "aes-ccm", "tkip"),
            new Vocabulary("/certificate", "digest-algorithm", typeof(tik4net.Objects.Certificate.Certificate.DigestAlgorithmType),
                "md5", "sha1", "sha256", "sha384", "sha512"),
            new Vocabulary("/certificate", "key-size", typeof(tik4net.Objects.Certificate.Certificate.KeySizeType),
                "1024", "1536", "2048", "4096", "8192", "prime256v1", "secp384r1", "secp521r1"),
            new Vocabulary("/interface/bonding", "arp", typeof(tik4net.Objects.Interface.InterfaceBonding.ArpMode),
                "disabled", "enabled", "local-proxy-arp", "proxy-arp", "reply-only"),
            new Vocabulary("/interface/bonding", "lacp-mode", typeof(tik4net.Objects.Interface.InterfaceBonding.LacpParticipationMode),
                "active", "passive"),
            new Vocabulary("/interface/bonding", "lacp-rate", typeof(tik4net.Objects.Interface.InterfaceBonding.LacpRateMode),
                "1sec", "30secs"),
            new Vocabulary("/interface/bonding", "link-monitoring", typeof(tik4net.Objects.Interface.InterfaceBonding.LinkMonitoringMode),
                "arp", "mii", "none"),
            new Vocabulary("/interface/bonding", "mode", typeof(tik4net.Objects.Interface.InterfaceBonding.BondingMode),
                "802.3ad", "active-backup", "balance-alb", "balance-rr", "balance-tlb", "balance-xor", "broadcast"),
            new Vocabulary("/interface/bonding", "transmit-hash-policy", typeof(tik4net.Objects.Interface.InterfaceBonding.TransmitHashPolicyMode),
                "encap-2-and-3", "encap-3-and-4", "layer-2", "layer-2-and-3", "layer-3-and-4"),
            new Vocabulary("/interface/bridge", "arp", typeof(tik4net.Objects.Interface.InterfaceBridge.ArpMode),
                "disabled", "enabled", "local-proxy-arp", "proxy-arp", "reply-only"),
            new Vocabulary("/interface/bridge", "frame-types", typeof(tik4net.Objects.Interface.InterfaceBridge.FrameTypesMode),
                "admit-all", "admit-only-untagged-and-priority-tagged", "admit-only-vlan-tagged"),
            new Vocabulary("/interface/bridge", "multicast-router", typeof(tik4net.Objects.Interface.InterfaceBridge.MulticastRouterMode),
                "disabled", "permanent", "temporary-query"),
            new Vocabulary("/interface/bridge", "port-cost-mode", typeof(tik4net.Objects.Interface.InterfaceBridge.PortCostModeType),
                "long", "short"),
            new Vocabulary("/interface/bridge", "protocol-mode", typeof(tik4net.Objects.Interface.InterfaceBridge.ProtocolModeModes),
                "mstp", "none", "rstp", "stp"),
            new Vocabulary("/interface/bridge/filter", "action", typeof(tik4net.Objects.Interface.Bridge.BridgeFilter.ActionType),
                "accept", "drop", "jump", "log", "mark-packet", "passthrough", "return", "set-priority"),
            new Vocabulary("/interface/bridge/nat", "action", typeof(tik4net.Objects.Interface.Bridge.BridgeNat.ActionType),
                "accept", "arp-reply", "drop", "dst-nat", "jump", "log", "mark-packet", "passthrough", "redirect", "return", "set-priority", "src-nat"),
            new Vocabulary("/interface/eoip", "arp", typeof(tik4net.Objects.Interface.Tunnel.InterfaceEoip.ArpMode),
                "disabled", "enabled", "local-proxy-arp", "proxy-arp", "reply-only"),
            new Vocabulary("/interface/eoip", "loop-protect", typeof(tik4net.Objects.Interface.Tunnel.InterfaceEoip.LoopProtectMode),
                "default", "off", "on"),
            new Vocabulary("/interface/l2tp-client", "l2tp-proto-version", typeof(tik4net.Objects.Interface.Vpn.L2tpClient.L2tpProtoVersionType),
                "l2tpv2", "l2tpv3-ip", "l2tpv3-udp"),
            new Vocabulary("/interface/l2tp-client", "l2tpv3-cookie-length", typeof(tik4net.Objects.Interface.Vpn.L2tpClient.L2tpv3CookieLengthType),
                "0", "4-bytes", "8-bytes"),
            new Vocabulary("/interface/l2tp-client", "l2tpv3-digest-hash", typeof(tik4net.Objects.Interface.Vpn.L2tpClient.L2tpv3DigestHashType),
                "md5", "none", "sha1"),
            new Vocabulary("/interface/l2tp-server/server", "accept-proto-version", typeof(tik4net.Objects.Interface.Vpn.L2tpServer.AcceptProtoVersionType),
                "all", "l2tpv2", "l2tpv3"),
            new Vocabulary("/interface/l2tp-server/server", "accept-pseudowire-type", typeof(tik4net.Objects.Interface.Vpn.L2tpServer.AcceptPseudowireTypeValue),
                "all", "ether", "ppp"),
            new Vocabulary("/interface/l2tp-server/server", "caller-id-type", typeof(tik4net.Objects.Interface.Vpn.L2tpServer.CallerIdTypeValue),
                "ip-address", "number"),
            new Vocabulary("/interface/l2tp-server/server", "l2tpv3-cookie-length", typeof(tik4net.Objects.Interface.Vpn.L2tpServer.L2tpv3CookieLengthType),
                "0", "4-bytes", "8-bytes"),
            new Vocabulary("/interface/l2tp-server/server", "l2tpv3-digest-hash", typeof(tik4net.Objects.Interface.Vpn.L2tpServer.L2tpv3DigestHashType),
                "md5", "none", "sha1"),
            new Vocabulary("/interface/l2tp-server/server", "use-ipsec", typeof(tik4net.Objects.Interface.Vpn.L2tpServer.UseIpsecType),
                "no", "required", "yes"),
            new Vocabulary("/interface/lte", "sms-protocol", typeof(tik4net.Objects.Interface.InterfaceLte.SmsProtocolType),
                "at", "auto", "mbim"),
            new Vocabulary("/interface/ovpn-client", "mode", typeof(tik4net.Objects.Interface.Vpn.OvpnClient.TunnelMode),
                "ethernet", "ip"),
            new Vocabulary("/interface/ovpn-client", "protocol", typeof(tik4net.Objects.Interface.Vpn.OvpnClient.ProtocolType),
                "tcp", "udp"),
            new Vocabulary("/interface/ovpn-client", "tls-version", typeof(tik4net.Objects.Interface.Vpn.OvpnClient.TlsVersionType),
                "any", "only-1.2"),
            new Vocabulary("/interface/ovpn-server/server", "mode", typeof(tik4net.Objects.Interface.Vpn.OvpnServer.TunnelMode),
                "ethernet", "ip"),
            new Vocabulary("/interface/ovpn-server/server", "protocol", typeof(tik4net.Objects.Interface.Vpn.OvpnServer.ProtocolType),
                "tcp", "udp"),
            new Vocabulary("/interface/ovpn-server/server", "redirect-gateway", typeof(tik4net.Objects.Interface.Vpn.OvpnServer.RedirectGatewayMode),
                "def1", "disabled", "ipv6"),
            new Vocabulary("/interface/ovpn-server/server", "tls-version", typeof(tik4net.Objects.Interface.Vpn.OvpnServer.TlsVersionType),
                "any", "only-1.2"),
            new Vocabulary("/interface/ovpn-server/server", "user-auth-method", typeof(tik4net.Objects.Interface.Vpn.OvpnServer.UserAuthMethodType),
                "mschap2", "pap"),
            new Vocabulary("/interface/sstp-client", "tls-version", typeof(tik4net.Objects.Interface.Vpn.SstpClient.TlsVersionType),
                "any", "only-1.2"),
            new Vocabulary("/interface/sstp-server/server", "pfs", typeof(tik4net.Objects.Interface.Vpn.SstpServer.PfsType),
                "no", "required", "yes"),
            new Vocabulary("/interface/sstp-server/server", "tls-version", typeof(tik4net.Objects.Interface.Vpn.SstpServer.TlsVersionType),
                "any", "only-1.2"),
            new Vocabulary("/interface/vlan", "arp", typeof(tik4net.Objects.Interface.InterfaceVlan.ArpMode),
                "disabled", "enabled", "local-proxy-arp", "proxy-arp", "reply-only"),
            new Vocabulary("/interface/vlan", "loop-protect", typeof(tik4net.Objects.Interface.InterfaceVlan.LoopProtectMode),
                "default", "off", "on"),
            new Vocabulary("/interface/vrrp", "arp", typeof(tik4net.Objects.Interface.InterfaceVrrp.ArpMode),
                "disabled", "enabled", "local-proxy-arp", "proxy-arp", "reply-only"),
            new Vocabulary("/interface/vrrp", "authentication", typeof(tik4net.Objects.Interface.InterfaceVrrp.AuthenticationMode),
                "ah", "none", "simple"),
            new Vocabulary("/interface/vrrp", "connection-tracking-mode", typeof(tik4net.Objects.Interface.InterfaceVrrp.ConnectionTrackingModeType),
                "active-active", "passive-active"),
            new Vocabulary("/interface/vrrp", "v3-protocol", typeof(tik4net.Objects.Interface.InterfaceVrrp.V3ProtocolType),
                "ipv4", "ipv6"),
            new Vocabulary("/interface/vxlan", "arp", typeof(tik4net.Objects.Interface.Tunnel.InterfaceVxlan.ArpMode),
                "disabled", "enabled", "local-proxy-arp", "proxy-arp", "reply-only"),
            new Vocabulary("/interface/vxlan", "loop-protect", typeof(tik4net.Objects.Interface.Tunnel.InterfaceVxlan.LoopProtectMode),
                "default", "off", "on"),
            new Vocabulary("/interface/vxlan", "rem-csum", typeof(tik4net.Objects.Interface.Tunnel.InterfaceVxlan.RemCsumType),
                "both", "none", "rx", "tx"),
            new Vocabulary("/interface/vxlan", "vteps-ip-version", typeof(tik4net.Objects.Interface.Tunnel.InterfaceVxlan.VtepsIpVersionType),
                "ipv4", "ipv6"),
            new Vocabulary("/interface/wifi", "arp", typeof(tik4net.Objects.Interface.Wifi.InterfaceWifi.ArpMode),
                "disabled", "enabled", "local-proxy-arp", "proxy-arp", "reply-only"),
            new Vocabulary("/interface/wifi/access-list", "action", typeof(tik4net.Objects.Interface.Wifi.WifiAccessList.WifiAccessListAction),
                "accept", "query-radius", "reject"),
            new Vocabulary("/interface/wifi/channel", "skip-dfs-channels", typeof(tik4net.Objects.Interface.Wifi.WifiChannel.SkipDfsChannelsMode),
                "10min-cac", "all", "disabled"),
            new Vocabulary("/interface/wifi/configuration", "hw-protection-mode", typeof(tik4net.Objects.Interface.Wifi.WifiConfiguration.HwProtectionModeType),
                "cts-to-self", "none", "rts-cts"),
            new Vocabulary("/interface/wifi/configuration", "installation", typeof(tik4net.Objects.Interface.Wifi.WifiConfiguration.InstallationType),
                "indoor", "outdoor"),
            new Vocabulary("/interface/wifi/configuration", "manager", typeof(tik4net.Objects.Interface.Wifi.WifiConfiguration.ManagerType),
                "capsman", "capsman-or-local", "local"),
            new Vocabulary("/interface/wifi/configuration", "mode", typeof(tik4net.Objects.Interface.Wifi.WifiConfiguration.OperatingMode),
                "ap", "station", "station-bridge", "station-pseudobridge"),
            new Vocabulary("/interface/wifi/configuration", "multicast-enhance", typeof(tik4net.Objects.Interface.Wifi.WifiConfiguration.MulticastEnhanceMode),
                "disabled", "enabled"),
            new Vocabulary("/interface/wifi/configuration", "qos-classifier", typeof(tik4net.Objects.Interface.Wifi.WifiConfiguration.QosClassifierMode),
                "dscp-high-3-bits", "priority"),
            new Vocabulary("/interface/wifi/datapath", "traffic-processing", typeof(tik4net.Objects.Interface.Wifi.WifiDatapath.TrafficProcessingMode),
                "on-cap", "on-capsman", "on-capsman-secure"),
            new Vocabulary("/interface/wifi/provisioning", "action", typeof(tik4net.Objects.Interface.Wifi.WifiProvisioning.WifiProvisioningAction),
                "create-disabled", "create-dynamic-enabled", "create-enabled", "none", "use-network-config"),
            new Vocabulary("/interface/wifi/security", "beacon-protection", typeof(tik4net.Objects.Interface.Wifi.WifiSecurity.BeaconProtectionMode),
                "disabled", "enabled"),
            new Vocabulary("/interface/wifi/security", "eap-certificate-mode", typeof(tik4net.Objects.Interface.Wifi.WifiSecurity.EapCertificateModeType),
                "dont-verify-certificate", "no-certificates", "verify-certificate", "verify-certificate-with-crl"),
            new Vocabulary("/interface/wifi/security", "group-encryption", typeof(tik4net.Objects.Interface.Wifi.WifiSecurity.GroupEncryptionCipher),
                "ccmp", "ccmp-256", "gcmp", "gcmp-256", "tkip"),
            new Vocabulary("/interface/wifi/security", "management-encryption", typeof(tik4net.Objects.Interface.Wifi.WifiSecurity.ManagementEncryptionCipher),
                "cmac", "cmac-256", "gmac", "gmac-256"),
            new Vocabulary("/interface/wifi/security", "management-protection", typeof(tik4net.Objects.Interface.Wifi.WifiSecurity.ManagementProtectionMode),
                "allowed", "disabled", "required"),
            new Vocabulary("/interface/wifi/security", "sae-pwe", typeof(tik4net.Objects.Interface.Wifi.WifiSecurity.SaePweMethod),
                "both", "hash-to-element", "hunting-and-pecking"),
            new Vocabulary("/interface/wifi/security", "wps", typeof(tik4net.Objects.Interface.Wifi.WifiSecurity.WpsMode),
                "disable", "push-button"),
            new Vocabulary("/interface/wireless", "mode", typeof(tik4net.Objects.Interface.InterfaceWireless.WirelessMode),
                "alignment-only", "ap-bridge", "bridge", "nstreme-dual-slave", "station", "station-bridge", "station-pseudobridge", "station-pseudobridge-clone", "station-wds", "wds-slave"),
            new Vocabulary("/interface/wireless", "preamble-mode", typeof(tik4net.Objects.Interface.InterfaceWireless.WirelessPreambleMode),
                "both", "long", "short"),
            new Vocabulary("/interface/wireless", "tx-power-mode", typeof(tik4net.Objects.Interface.InterfaceWireless.WirelessTxPowerMode),
                "all-rates-fixed", "card-rates", "default", "manual-table"),
            new Vocabulary("/interface/wireless", "wireless-protocol", typeof(tik4net.Objects.Interface.InterfaceWireless.WirelessWirelessProtocol),
                "802.11", "any", "nstreme", "nv2", "nv2-nstreme", "nv2-nstreme-802.11", "unspecified"),
            new Vocabulary("/interface/wireless/security-profiles", "mode", typeof(tik4net.Objects.Interface.Wireless.WirelessSecurityProfile.SecurityMode),
                "dynamic-keys", "none", "static-keys-optional", "static-keys-required"),
            new Vocabulary("/interface/wireless/security-profiles", "radius-called-format", typeof(tik4net.Objects.Interface.Wireless.WirelessSecurityProfile.CalledFormatType),
                "mac", "mac:ssid", "ssid"),
            new Vocabulary("/interface/wireless/security-profiles", "radius-mac-mode", typeof(tik4net.Objects.Interface.Wireless.WirelessSecurityProfile.MacModeType),
                "as-username", "as-username-and-password"),
            new Vocabulary("/interface/wireless/security-profiles", "static-algo-0", typeof(tik4net.Objects.Interface.Wireless.WirelessSecurityProfile.StaticAlgoType),
                "104bit-wep", "40bit-wep", "aes-ccm", "none", "tkip"),
            new Vocabulary("/interface/wireless/security-profiles", "static-algo-1", typeof(tik4net.Objects.Interface.Wireless.WirelessSecurityProfile.StaticAlgoType),
                "104bit-wep", "40bit-wep", "aes-ccm", "none", "tkip"),
            new Vocabulary("/interface/wireless/security-profiles", "static-algo-2", typeof(tik4net.Objects.Interface.Wireless.WirelessSecurityProfile.StaticAlgoType),
                "104bit-wep", "40bit-wep", "aes-ccm", "none", "tkip"),
            new Vocabulary("/interface/wireless/security-profiles", "static-algo-3", typeof(tik4net.Objects.Interface.Wireless.WirelessSecurityProfile.StaticAlgoType),
                "104bit-wep", "40bit-wep", "aes-ccm", "none", "tkip"),
            new Vocabulary("/interface/wireless/security-profiles", "static-sta-private-algo", typeof(tik4net.Objects.Interface.Wireless.WirelessSecurityProfile.StaticAlgoType),
                "104bit-wep", "40bit-wep", "aes-ccm", "none", "tkip"),
            new Vocabulary("/interface/wireless/security-profiles", "static-transmit-key", typeof(tik4net.Objects.Interface.Wireless.WirelessSecurityProfile.TransmitKeyType),
                "key-0", "key-1", "key-2", "key-3"),
            new Vocabulary("/ip/cloud", "ddns-enabled", typeof(tik4net.Objects.Ip.DdnsEnabledMode),
                "auto", "yes"),
            new Vocabulary("/ip/dhcp-client", "add-default-route", typeof(tik4net.Objects.Ip.IpDhcpClient.AddDefaultRouteType),
                "no", "special-classless", "yes"),
            new Vocabulary("/ip/dhcp-server", "authoritative", typeof(tik4net.Objects.Ip.IpDhcpServer.AuthoritativeType),
                "after-10sec-delay", "after-2sec-delay", "no", "yes"),
            new Vocabulary("/ip/dhcp-server", "bootp-support", typeof(tik4net.Objects.Ip.IpDhcpServer.BootpSupportType),
                "dynamic", "none", "static"),
            new Vocabulary("/ip/firewall/filter", "action", typeof(tik4net.Objects.Ip.Firewall.FirewallFilter.ActionType),
                "accept", "add-dst-to-address-list", "add-src-to-address-list", "drop", "fasttrack-connection", "jump", "log", "passthrough", "reject", "return", "tarpit"),
            new Vocabulary("/ip/firewall/filter", "connection-state", typeof(tik4net.Objects.Ip.Firewall.FirewallFilter.ConnectionStateType),
                "established", "invalid", "new", "related", "untracked"),
            new Vocabulary("/ip/firewall/mangle", "action", typeof(tik4net.Objects.Ip.Firewall.FirewallMangle.ActionType),
                "accept", "add-dst-to-address-list", "add-src-to-address-list", "change-dscp", "change-mss", "change-ttl", "clear-df", "drop", "fasttrack-connection", "jump", "log", "mark-connection", "mark-packet", "mark-routing", "passthrough", "return", "route", "set-priority", "sniff-pc", "sniff-tzsp", "strip-ipv4-options"),
            new Vocabulary("/ip/firewall/raw", "action", typeof(tik4net.Objects.Ip.Firewall.FirewallRaw.ActionType),
                "accept", "add-dst-to-address-list", "add-src-to-address-list", "drop", "jump", "log", "notrack", "passthrough", "return"),
            new Vocabulary("/ip/hotspot/walled-garden", "action", typeof(tik4net.Objects.Ip.Hotspot.WalledGardenAction),
                "allow", "deny"),
            new Vocabulary("/ip/hotspot/walled-garden/ip", "action", typeof(tik4net.Objects.Ip.Hotspot.WalledGardenIpAction),
                "accept", "drop", "reject"),
            new Vocabulary("/ip/ipsec/identity", "auth-method", typeof(tik4net.Objects.Ip.Ipsec.IpsecIdentity.AuthMethodType),
                "digital-signature", "eap", "eap-radius", "pre-shared-key", "pre-shared-key-xauth", "rsa-key", "rsa-signature-hybrid"),
            new Vocabulary("/ip/ipsec/identity", "generate-policy", typeof(tik4net.Objects.Ip.Ipsec.IpsecIdentity.GeneratePolicyType),
                "no", "port-override", "port-strict"),
            new Vocabulary("/ip/ipsec/identity", "match-by", typeof(tik4net.Objects.Ip.Ipsec.IpsecIdentity.MatchByType),
                "certificate", "remote-id"),
            new Vocabulary("/ip/ipsec/identity", "my-id", typeof(tik4net.Objects.Ip.Ipsec.IpsecIdentity.MyIdType),
                "address", "auto", "dn", "fqdn", "key-id", "user-fqdn"),
            new Vocabulary("/ip/ipsec/identity", "remote-id", typeof(tik4net.Objects.Ip.Ipsec.IpsecIdentity.RemoteIdType),
                "address", "auto", "dn", "fqdn", "ignore", "key-id", "user-fqdn"),
            new Vocabulary("/ip/ipsec/mode-config", "use-responder-dns", typeof(tik4net.Objects.Ip.Ipsec.IpsecModeConfig.UseResponderDnsType),
                "exclusively", "no", "yes"),
            new Vocabulary("/ip/ipsec/peer", "exchange-mode", typeof(tik4net.Objects.Ip.Ipsec.IpsecPeer.ExchangeModeType),
                "aggressive", "ike2", "main"),
            new Vocabulary("/ip/ipsec/policy", "action", typeof(tik4net.Objects.Ip.Ipsec.IpsecPolicy.ActionType),
                "discard", "encrypt", "none"),
            new Vocabulary("/ip/ipsec/policy", "ipsec-protocols", typeof(tik4net.Objects.Ip.Ipsec.IpsecPolicy.IpsecProtocolsType),
                "ah", "esp"),
            new Vocabulary("/ip/ipsec/policy", "level", typeof(tik4net.Objects.Ip.Ipsec.IpsecPolicy.LevelType),
                "require", "unique", "use"),
            new Vocabulary("/ip/ipsec/profile", "hash-algorithm", typeof(tik4net.Objects.Ip.Ipsec.IpsecProfile.HashAlgorithmType),
                "md5", "sha1", "sha256", "sha384", "sha512"),
            new Vocabulary("/ip/ipsec/profile", "prf-algorithm", typeof(tik4net.Objects.Ip.Ipsec.IpsecProfile.PrfAlgorithmType),
                "auto", "sha1", "sha256", "sha384", "sha512"),
            new Vocabulary("/ip/ipsec/profile", "proposal-check", typeof(tik4net.Objects.Ip.Ipsec.IpsecProfile.ProposalCheckType),
                "claim", "exact", "obey", "strict"),
            new Vocabulary("/ip/ipsec/proposal", "pfs-group", typeof(tik4net.Objects.Ip.Ipsec.IpsecProposal.PfsGroupType),
                "ecp256", "ecp384", "ecp521", "modp1024", "modp1536", "modp2048", "modp3072", "modp4096", "modp6144", "modp768", "modp8192", "none"),
            new Vocabulary("/ip/proxy/access", "action", typeof(tik4net.Objects.Ip.Proxy.ProxyAccessAction),
                "allow", "deny", "redirect", "url-append"),
            new Vocabulary("/ip/proxy/access", "method", typeof(tik4net.Objects.Ip.Proxy.ProxyHttpMethod),
                "CONNECT", "DELETE", "GET", "HEAD", "OPTIONS", "POST", "PUT", "TRACE"),
            new Vocabulary("/ip/service", "tls-version", typeof(tik4net.Objects.Ip.IpService.TlsVersionType),
                "any", "only-1.2"),
            new Vocabulary("/ip/settings", "ipv4-multipath-hash-policy", typeof(tik4net.Objects.Ip.MultipathHashPolicy),
                "l3", "l3-inner", "l4"),
            new Vocabulary("/ip/settings", "rp-filter", typeof(tik4net.Objects.Ip.RpFilterMode),
                "loose", "no", "strict"),
            new Vocabulary("/ip/settings", "tcp-timestamps", typeof(tik4net.Objects.Ip.TcpTimestampsMode),
                "disabled", "enabled", "random-offset"),
            new Vocabulary("/ip/ssh", "ciphers", typeof(tik4net.Objects.Ip.SshCiphers),
                "3des-cbc", "aes-cbc", "aes-ctr", "aes-gcm", "auto", "null"),
            new Vocabulary("/ip/ssh", "forwarding-enabled", typeof(tik4net.Objects.Ip.SshForwardingMode),
                "both", "local", "no", "remote"),
            new Vocabulary("/ip/ssh", "host-key-type", typeof(tik4net.Objects.Ip.SshHostKeyType),
                "ed25519", "rsa"),
            new Vocabulary("/ip/ssh", "password-authentication", typeof(tik4net.Objects.Ip.SshPasswordAuth),
                "no", "yes", "yes-if-no-key"),
            new Vocabulary("/ip/ssh", "publickey-authentication-options", typeof(tik4net.Objects.Ip.SshPubkeyOptions),
                "none", "touch-required", "verify-required"),
            new Vocabulary("/ip/traffic-flow/target", "version", typeof(tik4net.Objects.Ip.TrafficFlow.IpTrafficFlowTarget.NetFlowVersion),
                "1", "5", "9", "ipfix"),
            new Vocabulary("/ip/upnp/interfaces", "type", typeof(tik4net.Objects.Ip.Upnp.UpnpInterfaceType),
                "external", "internal"),
            new Vocabulary("/radius", "protocol", typeof(tik4net.Objects.Radius.Radius.ProtocolType),
                "radsec", "udp"),
            new Vocabulary("/radius", "require-message-auth", typeof(tik4net.Objects.Radius.Radius.RequireMessageAuthType),
                "no", "yes-for-request-resp"),
            new Vocabulary("/routing/ospf/area", "nssa-translator", typeof(tik4net.Objects.Routing.Ospf.OspfArea.NssaTranslatorMode),
                "candidate", "no", "yes"),
            new Vocabulary("/routing/ospf/area", "type", typeof(tik4net.Objects.Routing.Ospf.OspfArea.OspfAreaType),
                "default", "nssa", "stub"),
            new Vocabulary("/routing/ospf/instance", "originate-default", typeof(tik4net.Objects.Routing.Ospf.OspfInstance.OriginateDefaultMode),
                "always", "if-installed", "never"),
            new Vocabulary("/routing/ospf/instance", "version", typeof(tik4net.Objects.Routing.Ospf.OspfInstance.OspfVersion),
                "2", "3"),
            new Vocabulary("/routing/ospf/interface-template", "type", typeof(tik4net.Objects.Routing.Ospf.OspfInterfaceTemplate.OspfNetworkType),
                "broadcast", "nbma", "ptmp", "ptmp-broadcast", "ptp", "ptp-unnumbered"),
            new Vocabulary("/routing/rule", "action", typeof(tik4net.Objects.Routing.RoutingRule.ActionType),
                "drop", "lookup", "lookup-only-in-table", "mangle", "unreachable"),
            new Vocabulary("/snmp", "trap-version", typeof(tik4net.Objects.Snmp.SnmpTrapVersion),
                "1", "2", "3"),
            new Vocabulary("/snmp/community", "authentication-protocol", typeof(tik4net.Objects.Snmp.SnmpCommunity.AuthProtocol),
                "MD5", "SHA1"),
            new Vocabulary("/snmp/community", "encryption-protocol", typeof(tik4net.Objects.Snmp.SnmpCommunity.EncryptProtocol),
                "AES", "DES"),
            new Vocabulary("/snmp/community", "security", typeof(tik4net.Objects.Snmp.SnmpCommunity.SecurityLevel),
                "authorized", "none", "private"),
            new Vocabulary("/system/leds", "type", typeof(tik4net.Objects.System.SystemLeds.LedType),
                "ap-cap", "fan-fault", "flash-access", "interface-activity", "interface-receive", "interface-speed", "interface-speed-1G", "interface-speed-2.5G", "interface-speed-25G", "interface-speed-100G", "interface-status", "interface-transmit", "modem-signal", "modem-technology", "off", "on", "poe-fault", "poe-out", "wireless-signal-strength", "wireless-status"),
            new Vocabulary("/system/logging/action", "remote-log-format", typeof(tik4net.Objects.System.SystemLoggingAction.RemoteLogFormatType),
                "cef", "default", "syslog"),
            new Vocabulary("/system/logging/action", "remote-protocol", typeof(tik4net.Objects.System.SystemLoggingAction.RemoteProtocolType),
                "tcp", "tls", "udp"),
            new Vocabulary("/system/logging/action", "syslog-facility", typeof(tik4net.Objects.System.SystemLoggingAction.SyslogFacilityType),
                "auth", "authpriv", "cron", "daemon", "ftp", "kern", "local0", "local1", "local2", "local3", "local4", "local5", "local6", "local7", "lpr", "mail", "news", "ntp", "syslog", "user", "uucp"),
            new Vocabulary("/system/logging/action", "syslog-severity", typeof(tik4net.Objects.System.SystemLoggingAction.SyslogSeverityType),
                "alert", "auto", "critical", "debug", "emergency", "error", "info", "notice", "warning"),
            new Vocabulary("/system/logging/action", "syslog-time-format", typeof(tik4net.Objects.System.SystemLoggingAction.SyslogTimeFormatType),
                "bsd-syslog", "iso8601"),
            new Vocabulary("/system/logging/action", "target", typeof(tik4net.Objects.System.SystemLoggingAction.LoggingTarget),
                "disk", "echo", "email", "memory", "remote", "script"),
            new Vocabulary("/system/ntp/client", "mode", typeof(tik4net.Objects.System.NtpClientMode),
                "broadcast", "manycast", "multicast", "unicast"),
            new Vocabulary("/tool/e-mail", "certificate-verification", typeof(tik4net.Objects.Tool.ToolEmail.EmailCertificateVerification),
                "no", "yes", "yes-without-crl"),
            new Vocabulary("/tool/e-mail", "tls", typeof(tik4net.Objects.Tool.ToolEmail.EmailTls),
                "no", "starttls", "yes"),
            new Vocabulary("/tool/netwatch", "record-type", typeof(tik4net.Objects.Tool.ToolNetwatch.DnsRecordType),
                "A", "AAAA", "MX", "NS"),
            new Vocabulary("/tool/netwatch", "type", typeof(tik4net.Objects.Tool.ToolNetwatch.NeType),
                "dns", "http-get", "https-get", "icmp", "simple", "tcp-conn"),        };

        /// <summary>
        /// RouterOS 6.49.13 (the lab's CHR2), the same sweep. A 6.x router prints its own words, and an entity read
        /// from it fails as surely as from 7.x on a word the enum lacks — so the enums hold the UNION of both.
        /// </summary>
        /// <remarks>
        /// 94 of the 159 enum properties answered; the rest are menus RouterOS 6 does not have, tables with no row
        /// to <c>set</c> against, and four fields whose values share a prefix (<c>frame-types</c>,
        /// <c>transmit-hash-policy</c>, <c>v3-protocol</c>, <c>static-transmit-key</c>): 6.49.13 completes the
        /// prefix inline and then lists nothing when asked with it, so those are unmeasured on 6.x.
        /// </remarks>
        private static readonly Vocabulary[] RouterOs6Vocabularies =
        {
            new Vocabulary("/caps-man/access-list", "action", typeof(tik4net.Objects.CapsMan.CapsManAccessList.CapsManAccessListAction),
                "accept", "query-radius", "reject"),
            new Vocabulary("/caps-man/access-list", "vlan-mode", typeof(tik4net.Objects.CapsMan.CapsManAccessList.CapsManAccessListVlanMode),
                "no-tag", "use-service-tag", "use-tag"),
            new Vocabulary("/caps-man/channel", "extension-channel", typeof(tik4net.Objects.CapsMan.CapsManChannel.ExtensionChannelType),
                "Ce", "Ceee", "Ceeeeeee", "XX", "XXXX", "XXXXXXXX", "disabled", "eC", "eCee", "eCeeeeee", "eeCe", "eeCeeeee", "eeeC", "eeeCeeee", "eeeeCeee", "eeeeeCee", "eeeeeeCe", "eeeeeeeC"),
            new Vocabulary("/caps-man/configuration", "guard-interval", typeof(tik4net.Objects.CapsMan.CapsManConfiguration.GuardIntervalType),
                "any", "long"),
            new Vocabulary("/caps-man/configuration", "hw-protection-mode", typeof(tik4net.Objects.CapsMan.CapsManConfiguration.HwProtectionModeType),
                "cts-to-self", "none", "rts-cts"),
            new Vocabulary("/caps-man/configuration", "installation", typeof(tik4net.Objects.CapsMan.CapsManConfiguration.InstallationType),
                "any", "indoor", "outdoor"),
            new Vocabulary("/caps-man/configuration", "keepalive-frames", typeof(tik4net.Objects.CapsMan.CapsManConfiguration.KeepaliveFramesMode),
                "disabled", "enabled"),
            new Vocabulary("/caps-man/configuration", "mode", typeof(tik4net.Objects.CapsMan.CapsManConfiguration.OperatingMode),
                "ap"),
            new Vocabulary("/caps-man/configuration", "multicast-helper", typeof(tik4net.Objects.CapsMan.CapsManConfiguration.MulticastHelperMode),
                "default", "dhcp", "disabled", "full"),
            new Vocabulary("/caps-man/datapath", "arp", typeof(tik4net.Objects.CapsMan.CapsManDatapath.ArpMode),
                "disabled", "enabled", "local-proxy-arp", "proxy-arp", "reply-only"),
            new Vocabulary("/caps-man/datapath", "vlan-mode", typeof(tik4net.Objects.CapsMan.CapsManDatapath.VlanModeType),
                "no-tag", "use-service-tag", "use-tag"),
            new Vocabulary("/caps-man/interface", "arp", typeof(tik4net.Objects.CapsMan.CapsManInterface.ArpMode),
                "disabled", "enabled", "local-proxy-arp", "proxy-arp", "reply-only"),
            new Vocabulary("/caps-man/manager", "upgrade-policy", typeof(tik4net.Objects.CapsMan.CapsManManager.UpgradePolicyType),
                "none", "require-same-version", "suggest-same-version"),
            new Vocabulary("/caps-man/provisioning", "action", typeof(tik4net.Objects.CapsMan.CapsManProvisioning.CapsManProvisioningAction),
                "create-disabled", "create-dynamic-enabled", "create-enabled", "none"),
            new Vocabulary("/caps-man/provisioning", "name-format", typeof(tik4net.Objects.CapsMan.CapsManProvisioning.CapsManProvisioningNameFormat),
                "cap", "identity", "prefix", "prefix-identity"),
            new Vocabulary("/caps-man/security", "group-encryption", typeof(tik4net.Objects.CapsMan.CapsManSecurity.GroupEncryptionType),
                "aes-ccm", "tkip"),
            new Vocabulary("/certificate", "key-size", typeof(tik4net.Objects.Certificate.Certificate.KeySizeType),
                "1024", "1536", "2048", "4096", "8192", "prime256v1", "secp384r1", "secp521r1"),
            new Vocabulary("/interface/bonding", "arp", typeof(tik4net.Objects.Interface.InterfaceBonding.ArpMode),
                "disabled", "enabled", "local-proxy-arp", "proxy-arp", "reply-only"),
            new Vocabulary("/interface/bonding", "lacp-rate", typeof(tik4net.Objects.Interface.InterfaceBonding.LacpRateMode),
                "1sec", "30secs"),
            new Vocabulary("/interface/bonding", "link-monitoring", typeof(tik4net.Objects.Interface.InterfaceBonding.LinkMonitoringMode),
                "arp", "mii", "none"),
            new Vocabulary("/interface/bonding", "mode", typeof(tik4net.Objects.Interface.InterfaceBonding.BondingMode),
                "802.3ad", "active-backup", "balance-alb", "balance-rr", "balance-tlb", "balance-xor", "broadcast"),
            new Vocabulary("/interface/bridge", "arp", typeof(tik4net.Objects.Interface.InterfaceBridge.ArpMode),
                "disabled", "enabled", "local-proxy-arp", "proxy-arp", "reply-only"),
            new Vocabulary("/interface/bridge", "multicast-router", typeof(tik4net.Objects.Interface.InterfaceBridge.MulticastRouterMode),
                "disabled", "permanent", "temporary-query"),
            new Vocabulary("/interface/bridge", "protocol-mode", typeof(tik4net.Objects.Interface.InterfaceBridge.ProtocolModeModes),
                "mstp", "none", "rstp", "stp"),
            new Vocabulary("/interface/bridge/filter", "action", typeof(tik4net.Objects.Interface.Bridge.BridgeFilter.ActionType),
                "accept", "drop", "jump", "log", "mark-packet", "passthrough", "return", "set-priority"),
            new Vocabulary("/interface/bridge/nat", "action", typeof(tik4net.Objects.Interface.Bridge.BridgeNat.ActionType),
                "accept", "arp-reply", "drop", "dst-nat", "jump", "log", "mark-packet", "passthrough", "redirect", "return", "set-priority", "src-nat"),
            new Vocabulary("/interface/eoip", "arp", typeof(tik4net.Objects.Interface.Tunnel.InterfaceEoip.ArpMode),
                "disabled", "enabled", "local-proxy-arp", "proxy-arp", "reply-only"),
            new Vocabulary("/interface/eoip", "loop-protect", typeof(tik4net.Objects.Interface.Tunnel.InterfaceEoip.LoopProtectMode),
                "default", "off", "on"),
            new Vocabulary("/interface/l2tp-server/server", "caller-id-type", typeof(tik4net.Objects.Interface.Vpn.L2tpServer.CallerIdTypeValue),
                "ip-address", "number"),
            new Vocabulary("/interface/l2tp-server/server", "use-ipsec", typeof(tik4net.Objects.Interface.Vpn.L2tpServer.UseIpsecType),
                "no", "required", "yes"),
            new Vocabulary("/interface/lte", "network-mode", typeof(tik4net.Objects.Interface.InterfaceLte.NetworkModeType),
                "3g", "gsm", "lte"),
            new Vocabulary("/interface/ovpn-client", "mode", typeof(tik4net.Objects.Interface.Vpn.OvpnClient.TunnelMode),
                "ethernet", "ip"),
            new Vocabulary("/interface/ovpn-server/server", "mode", typeof(tik4net.Objects.Interface.Vpn.OvpnServer.TunnelMode),
                "ethernet", "ip"),
            new Vocabulary("/interface/sstp-client", "tls-version", typeof(tik4net.Objects.Interface.Vpn.SstpClient.TlsVersionType),
                "any", "only-1.2"),
            new Vocabulary("/interface/sstp-server/server", "pfs", typeof(tik4net.Objects.Interface.Vpn.SstpServer.PfsType),
                "no", "yes"),
            new Vocabulary("/interface/sstp-server/server", "tls-version", typeof(tik4net.Objects.Interface.Vpn.SstpServer.TlsVersionType),
                "any", "only-1.2"),
            new Vocabulary("/interface/vlan", "arp", typeof(tik4net.Objects.Interface.InterfaceVlan.ArpMode),
                "disabled", "enabled", "local-proxy-arp", "proxy-arp", "reply-only"),
            new Vocabulary("/interface/vlan", "loop-protect", typeof(tik4net.Objects.Interface.InterfaceVlan.LoopProtectMode),
                "default", "off", "on"),
            new Vocabulary("/interface/vrrp", "arp", typeof(tik4net.Objects.Interface.InterfaceVrrp.ArpMode),
                "disabled", "enabled", "local-proxy-arp", "proxy-arp", "reply-only"),
            new Vocabulary("/interface/vrrp", "authentication", typeof(tik4net.Objects.Interface.InterfaceVrrp.AuthenticationMode),
                "ah", "none", "simple"),
            new Vocabulary("/interface/wireless", "mode", typeof(tik4net.Objects.Interface.InterfaceWireless.WirelessMode),
                "alignment-only", "ap-bridge", "bridge", "nstreme-dual-slave", "station", "station-bridge", "station-pseudobridge", "station-pseudobridge-clone", "station-wds", "wds-slave"),
            new Vocabulary("/interface/wireless", "preamble-mode", typeof(tik4net.Objects.Interface.InterfaceWireless.WirelessPreambleMode),
                "both", "long", "short"),
            new Vocabulary("/interface/wireless", "tx-power-mode", typeof(tik4net.Objects.Interface.InterfaceWireless.WirelessTxPowerMode),
                "all-rates-fixed", "card-rates", "default", "manual-table"),
            new Vocabulary("/interface/wireless", "wireless-protocol", typeof(tik4net.Objects.Interface.InterfaceWireless.WirelessWirelessProtocol),
                "802.11", "any", "nstreme", "nv2", "nv2-nstreme", "nv2-nstreme-802.11", "unspecified"),
            new Vocabulary("/interface/wireless/security-profiles", "mode", typeof(tik4net.Objects.Interface.Wireless.WirelessSecurityProfile.SecurityMode),
                "dynamic-keys", "none", "static-keys-optional", "static-keys-required"),
            new Vocabulary("/interface/wireless/security-profiles", "radius-called-format", typeof(tik4net.Objects.Interface.Wireless.WirelessSecurityProfile.CalledFormatType),
                "mac", "mac:ssid", "ssid"),
            new Vocabulary("/interface/wireless/security-profiles", "radius-mac-mode", typeof(tik4net.Objects.Interface.Wireless.WirelessSecurityProfile.MacModeType),
                "as-username"),
            new Vocabulary("/interface/wireless/security-profiles", "static-algo-0", typeof(tik4net.Objects.Interface.Wireless.WirelessSecurityProfile.StaticAlgoType),
                "104bit-wep", "40bit-wep", "aes-ccm", "none", "tkip"),
            new Vocabulary("/interface/wireless/security-profiles", "static-algo-1", typeof(tik4net.Objects.Interface.Wireless.WirelessSecurityProfile.StaticAlgoType),
                "104bit-wep", "40bit-wep", "aes-ccm", "none", "tkip"),
            new Vocabulary("/interface/wireless/security-profiles", "static-algo-2", typeof(tik4net.Objects.Interface.Wireless.WirelessSecurityProfile.StaticAlgoType),
                "104bit-wep", "40bit-wep", "aes-ccm", "none", "tkip"),
            new Vocabulary("/interface/wireless/security-profiles", "static-algo-3", typeof(tik4net.Objects.Interface.Wireless.WirelessSecurityProfile.StaticAlgoType),
                "104bit-wep", "40bit-wep", "aes-ccm", "none", "tkip"),
            new Vocabulary("/interface/wireless/security-profiles", "static-sta-private-algo", typeof(tik4net.Objects.Interface.Wireless.WirelessSecurityProfile.StaticAlgoType),
                "104bit-wep", "40bit-wep", "aes-ccm", "none", "tkip"),
            new Vocabulary("/ip/cloud", "ddns-enabled", typeof(tik4net.Objects.Ip.DdnsEnabledMode),
                "no", "yes"),
            new Vocabulary("/ip/dhcp-client", "add-default-route", typeof(tik4net.Objects.Ip.IpDhcpClient.AddDefaultRouteType),
                "no", "special-classless", "yes"),
            new Vocabulary("/ip/dhcp-server", "authoritative", typeof(tik4net.Objects.Ip.IpDhcpServer.AuthoritativeType),
                "after-10sec-delay", "after-2sec-delay", "no", "yes"),
            new Vocabulary("/ip/dhcp-server", "bootp-support", typeof(tik4net.Objects.Ip.IpDhcpServer.BootpSupportType),
                "dynamic", "none", "static"),
            new Vocabulary("/ip/firewall/filter", "action", typeof(tik4net.Objects.Ip.Firewall.FirewallFilter.ActionType),
                "accept", "add-dst-to-address-list", "add-src-to-address-list", "drop", "fasttrack-connection", "jump", "log", "passthrough", "reject", "return", "tarpit"),
            new Vocabulary("/ip/firewall/filter", "connection-state", typeof(tik4net.Objects.Ip.Firewall.FirewallFilter.ConnectionStateType),
                "established", "invalid", "new", "related", "untracked"),
            new Vocabulary("/ip/firewall/mangle", "action", typeof(tik4net.Objects.Ip.Firewall.FirewallMangle.ActionType),
                "accept", "add-dst-to-address-list", "add-src-to-address-list", "change-dscp", "change-mss", "change-ttl", "clear-df", "fasttrack-connection", "jump", "log", "mark-connection", "mark-packet", "mark-routing", "passthrough", "return", "route", "set-priority", "sniff-pc", "sniff-tzsp", "strip-ipv4-options"),
            new Vocabulary("/ip/firewall/raw", "action", typeof(tik4net.Objects.Ip.Firewall.FirewallRaw.ActionType),
                "accept", "add-dst-to-address-list", "add-src-to-address-list", "drop", "jump", "log", "notrack", "passthrough", "return"),
            new Vocabulary("/ip/hotspot/walled-garden", "action", typeof(tik4net.Objects.Ip.Hotspot.WalledGardenAction),
                "allow", "deny"),
            new Vocabulary("/ip/hotspot/walled-garden/ip", "action", typeof(tik4net.Objects.Ip.Hotspot.WalledGardenIpAction),
                "accept", "drop", "reject"),
            new Vocabulary("/ip/ipsec/identity", "auth-method", typeof(tik4net.Objects.Ip.Ipsec.IpsecIdentity.AuthMethodType),
                "digital-signature", "eap", "eap-radius", "pre-shared-key", "pre-shared-key-xauth", "rsa-key", "rsa-signature-hybrid"),
            new Vocabulary("/ip/ipsec/identity", "generate-policy", typeof(tik4net.Objects.Ip.Ipsec.IpsecIdentity.GeneratePolicyType),
                "no", "port-override", "port-strict"),
            new Vocabulary("/ip/ipsec/identity", "match-by", typeof(tik4net.Objects.Ip.Ipsec.IpsecIdentity.MatchByType),
                "certificate", "remote-id"),
            new Vocabulary("/ip/ipsec/identity", "my-id", typeof(tik4net.Objects.Ip.Ipsec.IpsecIdentity.MyIdType),
                "address", "auto", "dn", "fqdn", "key-id", "user-fqdn"),
            new Vocabulary("/ip/ipsec/identity", "remote-id", typeof(tik4net.Objects.Ip.Ipsec.IpsecIdentity.RemoteIdType),
                "auto", "fqdn", "ignore", "key-id", "user-fqdn"),
            new Vocabulary("/ip/ipsec/mode-config", "use-responder-dns", typeof(tik4net.Objects.Ip.Ipsec.IpsecModeConfig.UseResponderDnsType),
                "exclusively", "no", "yes"),
            new Vocabulary("/ip/ipsec/peer", "exchange-mode", typeof(tik4net.Objects.Ip.Ipsec.IpsecPeer.ExchangeModeType),
                "aggressive", "base", "ike2", "main"),
            new Vocabulary("/ip/ipsec/policy", "action", typeof(tik4net.Objects.Ip.Ipsec.IpsecPolicy.ActionType),
                "discard", "encrypt", "none"),
            new Vocabulary("/ip/ipsec/policy", "ipsec-protocols", typeof(tik4net.Objects.Ip.Ipsec.IpsecPolicy.IpsecProtocolsType),
                "ah", "esp"),
            new Vocabulary("/ip/ipsec/policy", "level", typeof(tik4net.Objects.Ip.Ipsec.IpsecPolicy.LevelType),
                "require", "unique", "use"),
            new Vocabulary("/ip/ipsec/profile", "hash-algorithm", typeof(tik4net.Objects.Ip.Ipsec.IpsecProfile.HashAlgorithmType),
                "md5", "sha1", "sha256", "sha384", "sha512"),
            new Vocabulary("/ip/ipsec/profile", "prf-algorithm", typeof(tik4net.Objects.Ip.Ipsec.IpsecProfile.PrfAlgorithmType),
                "auto", "sha1", "sha256", "sha384", "sha512"),
            new Vocabulary("/ip/ipsec/profile", "proposal-check", typeof(tik4net.Objects.Ip.Ipsec.IpsecProfile.ProposalCheckType),
                "claim", "exact", "obey", "strict"),
            new Vocabulary("/ip/ipsec/proposal", "pfs-group", typeof(tik4net.Objects.Ip.Ipsec.IpsecProposal.PfsGroupType),
                "ec2n155", "ec2n185", "ecp256", "ecp384", "ecp521", "modp1024", "modp1536", "modp2048", "modp3072", "modp4096", "modp6144", "modp768", "modp8192", "none"),
            new Vocabulary("/ip/proxy/access", "action", typeof(tik4net.Objects.Ip.Proxy.ProxyAccessAction),
                "allow", "deny"),
            new Vocabulary("/ip/proxy/access", "method", typeof(tik4net.Objects.Ip.Proxy.ProxyHttpMethod),
                "CONNECT", "DELETE", "GET", "HEAD", "OPTIONS", "POST", "PUT", "TRACE"),
            new Vocabulary("/ip/service", "tls-version", typeof(tik4net.Objects.Ip.IpService.TlsVersionType),
                "any", "only-1.2"),
            new Vocabulary("/ip/settings", "rp-filter", typeof(tik4net.Objects.Ip.RpFilterMode),
                "loose", "no", "strict"),
            new Vocabulary("/ip/ssh", "forwarding-enabled", typeof(tik4net.Objects.Ip.SshForwardingMode),
                "both", "local", "no", "remote"),
            new Vocabulary("/ip/traffic-flow/target", "version", typeof(tik4net.Objects.Ip.TrafficFlow.IpTrafficFlowTarget.NetFlowVersion),
                "1", "5", "9", "ipfix"),
            new Vocabulary("/ip/upnp/interfaces", "type", typeof(tik4net.Objects.Ip.Upnp.UpnpInterfaceType),
                "external", "internal"),
            new Vocabulary("/radius", "protocol", typeof(tik4net.Objects.Radius.Radius.ProtocolType),
                "radsec", "udp"),
            new Vocabulary("/routing/ospf/area", "type", typeof(tik4net.Objects.Routing.Ospf.OspfArea.OspfAreaType),
                "default", "nssa", "stub"),
            new Vocabulary("/snmp", "trap-version", typeof(tik4net.Objects.Snmp.SnmpTrapVersion),
                "1", "2", "3"),
            new Vocabulary("/snmp/community", "authentication-protocol", typeof(tik4net.Objects.Snmp.SnmpCommunity.AuthProtocol),
                "MD5", "SHA1"),
            new Vocabulary("/snmp/community", "encryption-protocol", typeof(tik4net.Objects.Snmp.SnmpCommunity.EncryptProtocol),
                "AES", "DES"),
            new Vocabulary("/snmp/community", "security", typeof(tik4net.Objects.Snmp.SnmpCommunity.SecurityLevel),
                "authorized", "none", "private"),
            new Vocabulary("/system/leds", "type", typeof(tik4net.Objects.System.SystemLeds.LedType),
                "ap-cap", "fan-fault", "flash-access", "modem-signal", "modem-technology", "off", "on", "poe-fault", "poe-out", "wireless-signal-strength", "wireless-status"),
            new Vocabulary("/system/logging/action", "syslog-facility", typeof(tik4net.Objects.System.SystemLoggingAction.SyslogFacilityType),
                "auth", "authpriv", "cron", "daemon", "ftp", "kern", "local0", "local1", "local2", "local3", "local4", "local5", "local6", "local7", "lpr", "mail", "news", "ntp", "syslog", "user", "uucp"),
            new Vocabulary("/system/logging/action", "syslog-severity", typeof(tik4net.Objects.System.SystemLoggingAction.SyslogSeverityType),
                "alert", "auto", "critical", "debug", "emergency", "error", "info", "notice", "warning"),
            new Vocabulary("/system/logging/action", "syslog-time-format", typeof(tik4net.Objects.System.SystemLoggingAction.SyslogTimeFormatType),
                "bsd-syslog", "iso8601"),
            new Vocabulary("/system/logging/action", "target", typeof(tik4net.Objects.System.SystemLoggingAction.LoggingTarget),
                "disk", "echo", "email", "memory", "remote"),
        };

        [TestMethod]
        public void EveryEnumKnowsEveryValueItsRouterMenuOffers()
        {
            var failures = new List<string>();

            foreach (var v in Vocabularies.Concat(RouterOs6Vocabularies))
            {
                // The [TikEnum] values are what TikEnumMetadata builds its parse table from, so a member
                // without the attribute is not a parse key at all — count keys the way the mapper does.
                var known = new HashSet<string>(
                    v.EnumType.GetFields(BindingFlags.Public | BindingFlags.Static)
                        .Select(f => f.GetCustomAttribute<TikEnumAttribute>()?.Value)
                        .Where(value => !string.IsNullOrEmpty(value))!,
                    StringComparer.OrdinalIgnoreCase);

                var missing = v.RouterValues.Where(value => !known.Contains(value)).ToList();
                if (missing.Count > 0)
                    failures.Add($"{v.Menu} {v.Field} ({v.EnumType.Name}): {string.Join(", ", missing)}");
            }

            // Accumulated, not first-failure: the useful answer is how far the vocabularies have drifted
            // from the router, not which one tripped first.
            Assert.AreEqual(0, failures.Count,
                "These menus accept values the entity cannot read, so a router using any of them fails the "
                + "whole LoadAll of that menu:" + Environment.NewLine
                + string.Join(Environment.NewLine, failures));
        }

        [TestMethod]
        public void TheTableCoversEveryEnumTypedMappedProperty()
        {
            var mapped = typeof(TikEntityAttribute).Assembly.GetTypes()
                .Where(t => t.GetCustomAttribute<TikEntityAttribute>() != null)
                .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(p => new { Entity = t, Property = p, Attribute = p.GetCustomAttribute<TikPropertyAttribute>() })
                    .Where(x => x.Attribute != null)
                    .Where(x => (Nullable.GetUnderlyingType(x.Property.PropertyType) ?? x.Property.PropertyType).IsEnum)
                    .Select(x => $"{t.GetCustomAttribute<TikEntityAttribute>()!.EntityPath} {x.Attribute!.FieldName}"))
                .ToList();

            var covered = new HashSet<string>(Vocabularies.Select(v => $"{v.Menu} {v.Field}"));
            var uncovered = mapped.Where(m => !covered.Contains(m)).OrderBy(m => m, StringComparer.Ordinal).ToList();

            // The nine deliberate exclusions are listed in the class remarks, with the measurement behind
            // each. A NEW enum property lands here, which is the point: it has to be measured or excused.
            Assert.AreEqual(8, uncovered.Count,
                "Enum-typed mapped properties with no measured vocabulary:" + Environment.NewLine
                + string.Join(Environment.NewLine, uncovered));
        }
    }
}
