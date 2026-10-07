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
        public string? Id { get; private set; }

        /// <summary>name — unique profile name, referenced by /ip/hotspot servers.</summary>
        [TikProperty("name", WinboxLabel = "Name")]
        public TikField<string?> Name { get; set; }

        /// <summary>hotspot-address — IP address of the HotSpot service; clients are redirected here. Default: 0.0.0.0 (auto).</summary>
        [TikProperty("hotspot-address", DefaultValue = "0.0.0.0", WinboxLabel = "Hotspot Address")]
        public TikField<string?> HotspotAddress { get; set; }

        /// <summary>dns-name — FQDN of the HotSpot gateway shown in browser redirects. Default: empty.</summary>
        [TikProperty("dns-name", DefaultValue = "", WinboxLabel = "DNS Name")]
        public TikField<string?> DnsName { get; set; }

        /// <summary>html-directory — directory under /flash/hotspot that contains the HotSpot HTML pages. Default: hotspot.</summary>
        [TikProperty("html-directory", DefaultValue = "hotspot")]
        public TikField<string?> HtmlDirectory { get; set; }

        /// <summary>html-directory-override — override path that takes precedence over html-directory when set. Default: empty.</summary>
        [TikProperty("html-directory-override", DefaultValue = "")]
        public TikField<string?> HtmlDirectoryOverride { get; set; }

        /// <summary>http-proxy — HTTP proxy address (IP:port) used for transparent proxying. Default: 0.0.0.0:0 (none).</summary>
        [TikProperty("http-proxy", DefaultValue = "0.0.0.0:0")]
        public TikField<string?> HttpProxy { get; set; }

        /// <summary>smtp-server — SMTP server address for sending messages from the HotSpot. Default: 0.0.0.0 (none).</summary>
        [TikProperty("smtp-server", DefaultValue = "0.0.0.0", WinboxLabel = "SMTP Server")]
        public TikField<string?> SmtpServer { get; set; }

        /// <summary>login-by — comma-separated list of login methods (cookie, http-chap, http-pap, https, mac, mac-cookie, trial). Default: cookie,http-chap.</summary>
        [TikProperty("login-by", DefaultValue = "cookie,http-chap", WinboxLabel = "Login By")]
        public TikField<string?> LoginBy { get; set; }

        /// <summary>http-cookie-lifetime — validity period of the authentication cookie. Default: 3d.</summary>
        [TikProperty("http-cookie-lifetime", DefaultValue = "3d", WinboxLabel = "HTTP Cookie Lifetime")]
        public TikField<TikDuration?> HttpCookieLifetime { get; set; }

        /// <summary>install-hotspot-queue — create a simple queue to limit overall HotSpot throughput when enabled.</summary>
        [TikProperty("install-hotspot-queue", DefaultValue = "no", WinboxLabel = "Install Hotspot Queue", MinRouterOs = "7")]
        public TikField<bool?> InstallHotspotQueue { get; set; }

        /// <summary>split-user-domain — when yes, the domain part is stripped from the username before RADIUS lookup.</summary>
        [TikProperty("split-user-domain", DefaultValue = "no", WinboxLabel = "Split User Domain")]
        public TikField<bool?> SplitUserDomain { get; set; }

        /// <summary>use-radius — when yes, user authentication is delegated to RADIUS instead of the local user database.</summary>
        [TikProperty("use-radius", DefaultValue = "no", WinboxLabel = "Use RADIUS")]
        public TikField<bool?> UseRadius { get; set; }

        /// <summary>ssl-certificate — certificate name for HTTPS login page (from /certificate).</summary>
        [TikProperty("ssl-certificate", DefaultValue = "none", WinboxLabel = "SSL Certificate")]
        public TikField<string?> SslCertificate { get; set; }

        /// <summary>rate-limit — simple queue rate limit applied to all users of this profile (format: rx-rate[/tx-rate] ...).</summary>
        [TikProperty("rate-limit", DefaultValue = "", WinboxLabel = "Rate Limit (rx/tx)")]
        public TikField<string?> RateLimit { get; set; }

        // --- RADIUS fields ---

        /// <summary>radius-accounting — send RADIUS accounting packets.</summary>
        [TikProperty("radius-accounting", DefaultValue = "yes", WinboxLabel = "Accounting")]
        public TikField<bool?> RadiusAccounting { get; set; }

        /// <summary>
        /// radius-interim-update — interval for sending RADIUS accounting interim-update packets, e.g. "5m".
        /// Default: <c>received</c> — RouterOS's word for the zero interval, meaning it follows whatever the
        /// RADIUS server asked for rather than sending on a schedule. The API prints the field only on a
        /// profile with <c>use-radius=yes</c>.
        /// </summary>
        [TikProperty("radius-interim-update", DefaultValue = "received", WinboxLabel = "Interim Update")]
        public TikField<TikDuration?> RadiusInterimUpdate { get; set; }

        /// <summary>radius-default-domain — domain appended to username for RADIUS lookups when no domain is specified.</summary>
        [TikProperty("radius-default-domain", DefaultValue = "", WinboxLabel = "Default Domain")]
        public TikField<string?> RadiusDefaultDomain { get; set; }

        /// <summary>radius-location-id — RADIUS NAS-Location-Id attribute value.</summary>
        [TikProperty("radius-location-id", DefaultValue = "", WinboxLabel = "Location ID")]
        public TikField<string?> RadiusLocationId { get; set; }

        /// <summary>radius-location-name — RADIUS NAS-Location-Name attribute value.</summary>
        [TikProperty("radius-location-name", DefaultValue = "", WinboxLabel = "Location Name")]
        public TikField<string?> RadiusLocationName { get; set; }

        /// <summary>radius-mac-format — format of MAC address sent in RADIUS User-Name for MAC authentication (e.g. XX:XX:XX:XX:XX:XX).</summary>
        [TikProperty("radius-mac-format", DefaultValue = "XX:XX:XX:XX:XX:XX", WinboxLabel = "MAC Format")]
        public TikField<string?> RadiusMacFormat { get; set; }

        /// <summary>nas-port-type — RADIUS NAS-Port-Type attribute value.</summary>
        [TikProperty("nas-port-type", DefaultValue = "wireless-802.11", WinboxLabel = "NAS Port Type")]
        public TikField<string?> NasPortType { get; set; }

        // --- MAC auth ---

        /// <summary>mac-auth-mode — how MAC authentication is performed (mac-as-username / mac-as-username-and-password).</summary>
        [TikProperty("mac-auth-mode", DefaultValue = "mac-as-username", WinboxLabel = "MAC Auth. Mode")]
        public TikField<string?> MacAuthMode { get; set; }

        /// <summary>mac-auth-password — password used when mac-auth-mode is mac-as-username-and-password.</summary>
        [TikProperty("mac-auth-password", DefaultValue = "", IsSensitive = true, WinboxLabel = "MAC Auth. Password")]
        public TikField<string?> MacAuthPassword { get; set; }

        // --- Trial ---

        /// <summary>trial-user-profile — user profile assigned to trial (unauthenticated time-limited) users.</summary>
        [TikProperty("trial-user-profile", DefaultValue = "default", WinboxLabel = "Trial User Profile")]
        public TikField<string?> TrialUserProfile { get; set; }

        /// <summary>trial-uptime-limit — maximum session time for trial users (0s = disabled).</summary>
        [TikProperty("trial-uptime-limit", DefaultValue = "30m", WinboxLabel = "Trial Uptime Limit")]
        public TikField<TikDuration?> TrialUptimeLimit { get; set; }

        /// <summary>trial-uptime-reset — interval after which the trial uptime counter resets (0s = no reset).</summary>
        [TikProperty("trial-uptime-reset", DefaultValue = "1d", WinboxLabel = "Trial Uptime Reset")]
        public TikField<TikDuration?> TrialUptimeReset { get; set; }

        // --- Read-only ---

        /// <summary>default — when true, this is the built-in default profile (cannot be deleted).</summary>
        [TikProperty("default", IsReadOnly = true)]
        public TikField<bool?> IsDefault { get; private set; }

        /// <summary>Human-readable profile summary.</summary>
        public override string? ToString() => Name.Value;
    }
}
