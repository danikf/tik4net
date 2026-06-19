using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.Hotspot
{
    /// <summary>
    /// /ip/hotspot/profile: HotSpot server profiles. A profile is a collection of server-level settings
    /// (HTML pages, login methods, RADIUS, cookie lifetime) shared by one or more HotSpot server instances.
    /// Not to be confused with user profiles (/ip/hotspot/user/profile → <see cref="HotspotUserProfile"/>).
    /// </summary>
    [TikEntity("/ip/hotspot/profile", IncludeDetails = true)]
    public class HotspotServerProfile
    {
        /// <summary>.id — primary key of the profile.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>name — unique profile name, referenced by /ip/hotspot servers.</summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>hotspot-address — IP address of the HotSpot service; clients are redirected here. Default: 0.0.0.0 (auto).</summary>
        [TikProperty("hotspot-address", DefaultValue = "0.0.0.0")]
        public string/*IP*/ HotspotAddress { get; set; }

        /// <summary>dns-name — FQDN of the HotSpot gateway shown in browser redirects. Default: empty.</summary>
        [TikProperty("dns-name", DefaultValue = "")]
        public string DnsName { get; set; }

        /// <summary>html-directory — directory under /flash/hotspot that contains the HotSpot HTML pages. Default: hotspot.</summary>
        [TikProperty("html-directory", DefaultValue = "hotspot")]
        public string HtmlDirectory { get; set; }

        /// <summary>html-directory-override — override path that takes precedence over html-directory when set. Default: empty.</summary>
        [TikProperty("html-directory-override", DefaultValue = "")]
        public string HtmlDirectoryOverride { get; set; }

        /// <summary>http-proxy — HTTP proxy address (IP:port) used for transparent proxying. Default: 0.0.0.0:0 (none).</summary>
        [TikProperty("http-proxy", DefaultValue = "0.0.0.0:0")]
        public string HttpProxy { get; set; }

        /// <summary>smtp-server — SMTP server address for sending messages from the HotSpot. Default: 0.0.0.0 (none).</summary>
        [TikProperty("smtp-server", DefaultValue = "0.0.0.0")]
        public string/*IP*/ SmtpServer { get; set; }

        /// <summary>login-by — comma-separated list of login methods (cookie, http-chap, http-pap, https, mac, mac-cookie, trial). Default: cookie,http-chap.</summary>
        [TikProperty("login-by", DefaultValue = "cookie,http-chap")]
        public string LoginBy { get; set; }

        /// <summary>http-cookie-lifetime — validity period of the authentication cookie. Default: 3d.</summary>
        [TikProperty("http-cookie-lifetime", DefaultValue = "3d")]
        public string/*time*/ HttpCookieLifetime { get; set; }

        /// <summary>install-hotspot-queue — create a simple queue to limit overall HotSpot throughput when enabled.</summary>
        [TikProperty("install-hotspot-queue", DefaultValue = "no")]
        public bool InstallHotspotQueue { get; set; }

        /// <summary>split-user-domain — when yes, the domain part is stripped from the username before RADIUS lookup.</summary>
        [TikProperty("split-user-domain", DefaultValue = "no")]
        public bool SplitUserDomain { get; set; }

        /// <summary>use-radius — when yes, user authentication is delegated to RADIUS instead of the local user database.</summary>
        [TikProperty("use-radius", DefaultValue = "no")]
        public bool UseRadius { get; set; }

        /// <summary>ssl-certificate — certificate name for HTTPS login page (from /certificate).</summary>
        [TikProperty("ssl-certificate", DefaultValue = "none")]
        public string SslCertificate { get; set; }

        /// <summary>rate-limit — simple queue rate limit applied to all users of this profile (format: rx-rate[/tx-rate] ...).</summary>
        [TikProperty("rate-limit", DefaultValue = "")]
        public string RateLimit { get; set; }

        // --- RADIUS fields ---

        /// <summary>radius-accounting — send RADIUS accounting packets.</summary>
        [TikProperty("radius-accounting", DefaultValue = "yes")]
        public bool RadiusAccounting { get; set; }

        /// <summary>radius-interim-update — interval for sending RADIUS accounting interim-update packets. Default: 0s (disabled).</summary>
        [TikProperty("radius-interim-update", DefaultValue = "0s")]
        public string/*time*/ RadiusInterimUpdate { get; set; }

        /// <summary>radius-default-domain — domain appended to username for RADIUS lookups when no domain is specified.</summary>
        [TikProperty("radius-default-domain", DefaultValue = "")]
        public string RadiusDefaultDomain { get; set; }

        /// <summary>radius-location-id — RADIUS NAS-Location-Id attribute value.</summary>
        [TikProperty("radius-location-id", DefaultValue = "")]
        public string RadiusLocationId { get; set; }

        /// <summary>radius-location-name — RADIUS NAS-Location-Name attribute value.</summary>
        [TikProperty("radius-location-name", DefaultValue = "")]
        public string RadiusLocationName { get; set; }

        /// <summary>radius-mac-format — format of MAC address sent in RADIUS User-Name for MAC authentication (e.g. XX:XX:XX:XX:XX:XX).</summary>
        [TikProperty("radius-mac-format", DefaultValue = "XX:XX:XX:XX:XX:XX")]
        public string RadiusMacFormat { get; set; }

        /// <summary>nas-port-type — RADIUS NAS-Port-Type attribute value.</summary>
        [TikProperty("nas-port-type", DefaultValue = "wireless-802.11")]
        public string NasPortType { get; set; }

        // --- MAC auth ---

        /// <summary>mac-auth-mode — how MAC authentication is performed (mac-as-username / mac-as-username-and-password).</summary>
        [TikProperty("mac-auth-mode", DefaultValue = "mac-as-username")]
        public string MacAuthMode { get; set; }

        /// <summary>mac-auth-password — password used when mac-auth-mode is mac-as-username-and-password.</summary>
        [TikProperty("mac-auth-password", DefaultValue = "")]
        public string MacAuthPassword { get; set; }

        // --- Trial ---

        /// <summary>trial-user-profile — user profile assigned to trial (unauthenticated time-limited) users.</summary>
        [TikProperty("trial-user-profile", DefaultValue = "default")]
        public string TrialUserProfile { get; set; }

        /// <summary>trial-uptime-limit — maximum session time for trial users (0s = disabled).</summary>
        [TikProperty("trial-uptime-limit", DefaultValue = "0s")]
        public string/*time*/ TrialUptimeLimit { get; set; }

        /// <summary>trial-uptime-reset — interval after which the trial uptime counter resets (0s = no reset).</summary>
        [TikProperty("trial-uptime-reset", DefaultValue = "0s")]
        public string/*time*/ TrialUptimeReset { get; set; }

        // --- Read-only ---

        /// <summary>default — when true, this is the built-in default profile (cannot be deleted).</summary>
        [TikProperty("default", IsReadOnly = true)]
        public bool IsDefault { get; private set; }

        /// <summary>Human-readable profile summary.</summary>
        public override string ToString() => Name;
    }
}
