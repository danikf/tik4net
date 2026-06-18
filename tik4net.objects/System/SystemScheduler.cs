using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.System
{
    /// <summary>
    /// /system/scheduler — the scheduler can trigger script execution at a particular time moment,
    /// after a specified time interval, or both.
    /// </summary>
    [TikEntity("/system/scheduler", IncludeDetails = true)]
    public class SystemScheduler
    {
        /// <summary>
        /// .id — primary key of the row.
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// name — identifier for the scheduled task.
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// interval — time between executions. <c>0s</c> means execute only at <see cref="StartTime"/>.
        /// </summary>
        [TikProperty("interval", DefaultValue = "0s")]
        public string/*time*/ Interval { get; set; }

        /// <summary>
        /// start-date — date when the script first executes.
        /// </summary>
        [TikProperty("start-date")]
        public string/*date*/ StartDate { get; set; }

        /// <summary>
        /// start-time — time of initial script execution. The special value <c>startup</c>
        /// runs the script a few seconds after the system boots.
        /// </summary>
        [TikProperty("start-time")]
        public string/*time*/ StartTime { get; set; }

        /// <summary>
        /// on-event — script source to run, or the name of a script from /system/script.
        /// </summary>
        [TikProperty("on-event")]
        public string OnEvent { get; set; }

        /// <summary>
        /// policy — comma-separated list of user policies this script runs under
        /// (e.g. <c>read,write,policy,test</c>). Combination of flags, kept as string.
        /// </summary>
        [TikProperty("policy")]
        public string Policy { get; set; }

        /// <summary>
        /// owner — user that owns/created the scheduled task (read-only).
        /// </summary>
        [TikProperty("owner", IsReadOnly = true)]
        public string Owner { get; private set; }

        /// <summary>
        /// run-count — counter tracking how many times the script has executed (read-only).
        /// </summary>
        [TikProperty("run-count", IsReadOnly = true)]
        public int RunCount { get; private set; }

        /// <summary>
        /// next-run — when the script is scheduled to run next (read-only).
        /// </summary>
        [TikProperty("next-run", IsReadOnly = true)]
        public string NextRun { get; private set; }

        /// <summary>
        /// disabled — whether the scheduled task is disabled.
        /// </summary>
        [TikProperty("disabled")]
        public bool Disabled { get; set; }

        /// <summary>
        /// comment.
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Format("{0} (interval={1}, on-event={2})", Name, Interval, OnEvent);
        }
    }
}
