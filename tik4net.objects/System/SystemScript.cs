using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.System
{
    /// <summary>
    /// /system/script — script repository holding user-created scripts that can be executed
    /// automatically through events (scheduler, netwatch, VRRP), called by other scripts, or
    /// run manually via console or WinBox. Only scripts with equal or higher permission rights
    /// can execute other scripts.
    /// </summary>
    [TikEntity("/system/script", IncludeDetails = true)]
    public class SystemScript
    {
        /// <summary>
        /// .id — primary key of the row.
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// name — identifier for the script. Default auto-assigned as "Script[num]".
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// source — script source code content.
        /// </summary>
        [TikProperty("source")]
        public string Source { get; set; }

        /// <summary>
        /// policy — comma-separated list of applicable policies this script runs under
        /// (ftp, reboot, read, write, policy, test, password, sniff, sensitive, romon).
        /// Kept as string because it is a multi-value bitmask. Default: ftp,reboot,read,write,policy,test,password,sniff,sensitive,romon.
        /// </summary>
        [TikProperty("policy")]
        public string Policy { get; set; }

        /// <summary>
        /// dont-require-permissions — bypass the permissions check when the script executes;
        /// useful for services with limited permissions such as Netwatch. Default: no.
        /// WinBox: "Don't Require Permissions".
        /// </summary>
        // DefaultValue is the WIRE form ("no"/"yes"), not the C# literal — a bool serialises to "no"/"yes",
        // so DefaultValue="false" would never match and the field would be force-sent on every add/set
        // (which also makes the native WinBox M2 transport fail: it cannot resolve this field to an M2 key).
        [TikProperty("dont-require-permissions", DefaultValue = "no")]
        public bool DontRequirePermissions { get; set; }

        /// <summary>
        /// comment — descriptive comment for the script.
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>
        /// owner — user who created the script (read-only).
        /// </summary>
        [TikProperty("owner", IsReadOnly = true)]
        public string Owner { get; private set; }

        /// <summary>
        /// run-count — total number of times the script has been executed (read-only).
        /// </summary>
        [TikProperty("run-count", IsReadOnly = true)]
        public int RunCount { get; private set; }

        /// <summary>
        /// last-started — date and time of the most recent script invocation (read-only).
        /// Only present after the script has been run at least once.
        /// </summary>
        [TikProperty("last-started", IsReadOnly = true)]
        public string/*datetime*/ LastStarted { get; private set; }

        /// <summary>
        /// invalid — whether the script is in an invalid state (read-only, undocumented).
        /// </summary>
        [TikProperty("invalid", IsReadOnly = true)]
        public bool Invalid { get; private set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Format("{0} (owner={1}, run-count={2})", Name, Owner, RunCount);
        }
    }
}
