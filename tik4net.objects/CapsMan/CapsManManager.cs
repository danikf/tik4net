using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.CapsMan
{
    /// <summary>
    /// /caps-man/manager: Global CAPsMAN controller settings (legacy CAPsMAN, RouterOS 6.x).
    /// Controls whether the CAPsMAN controller is active on this router, which certificates
    /// are used for CAP authentication, where upgrade packages are stored, and the upgrade
    /// policy applied to connecting CAPs.
    /// This is a singleton menu — use <see cref="TikConnectionExtensions.LoadSingle{T}"/> to load it.
    /// </summary>
    [TikEntity("/caps-man/manager", IsSingleton = true)]
    public class CapsManManager
    {
        // ── Upgrade policy ────────────────────────────────────────────────────

        /// <summary>Upgrade policy values for the <see cref="UpgradePolicy"/> property.</summary>
        /// <seealso cref="UpgradePolicy"/>
        public enum UpgradePolicyType
        {
            /// <summary>none — do not attempt to upgrade connecting CAPs (default).</summary>
            [TikEnum("none")] None,
            /// <summary>require-same-version — only allow CAPs running the same RouterOS version; block provisioning otherwise.</summary>
            [TikEnum("require-same-version")] RequireSameVersion,
            /// <summary>suggest-same-version — recommend that CAPs upgrade to the same RouterOS version, but allow provisioning even if upgrade fails.</summary>
            [TikEnum("suggest-same-version")] SuggestSameVersion,
        }

        // ── Writable properties ───────────────────────────────────────────────

        /// <summary>
        /// enabled — enables or disables the CAPsMAN controller on this router.
        /// Default: no.
        /// </summary>
        [TikProperty("enabled", DefaultValue = "false")]
        public bool Enabled { get; set; }

        /// <summary>
        /// certificate — name of the device certificate used for DTLS-secured CAP connections,
        /// or "none" to use no certificate, or "auto" to auto-generate one.
        /// Default: none.
        /// </summary>
        [TikProperty("certificate", DefaultValue = "none")]
        public string Certificate { get; set; }

        /// <summary>
        /// ca-certificate — name of the CA certificate used to validate connecting CAPs,
        /// or "none" to skip CA validation, or "auto" to auto-generate one.
        /// Default: none.
        /// </summary>
        [TikProperty("ca-certificate", DefaultValue = "none")]
        public string CaCertificate { get; set; }

        /// <summary>
        /// require-peer-certificate — when true, all connecting CAPs must present a valid
        /// certificate signed by the configured CA; unauthenticated CAPs are rejected.
        /// Default: no.
        /// </summary>
        [TikProperty("require-peer-certificate", DefaultValue = "false")]
        public bool RequirePeerCertificate { get; set; }

        /// <summary>
        /// package-path — folder path on this router from which RouterOS upgrade packages
        /// are served to CAPs (e.g. "/upgrade"). An empty string causes CAPsMAN to use
        /// its own built-in packages for CAPs with the same CPU architecture.
        /// Default: "" (empty — use built-in packages).
        /// </summary>
        [TikProperty("package-path", DefaultValue = "")]
        public string PackagePath { get; set; }

        /// <summary>
        /// upgrade-policy — determines how CAPsMAN handles RouterOS version mismatches
        /// between the controller and connecting CAPs.
        /// Default: none.
        /// <seealso cref="UpgradePolicyType"/>
        /// </summary>
        [TikProperty("upgrade-policy", DefaultValue = "none")]
        public UpgradePolicyType UpgradePolicy { get; set; }

        /// <summary>Human-readable summary of the CAPsMAN manager state.</summary>
        public override string ToString() => string.Format("enabled={0} upgrade-policy={1}", Enabled, UpgradePolicy);
    }
}
