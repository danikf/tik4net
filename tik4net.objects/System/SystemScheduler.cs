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
        public string? Id { get; private set; }

        /// <summary>
        /// name — identifier for the scheduled task.
        /// </summary>
        [TikProperty("name", WinboxLabel = "Name")]
        public TikField<string?> Name { get; set; }

        /// <summary>
        /// interval — time between executions. <c>0s</c> means execute only at <see cref="StartTime"/>.
        /// </summary>
        [TikProperty("interval", DefaultValue = "0s", WinboxLabel = "Interval")]
        public TikField<TikDuration?> Interval { get; set; }

        /// <summary>
        /// start-date — date when the script first executes.
        /// </summary>
        [TikProperty("start-date", WinboxLabel = "Start Date")]
        public TikField<string?> StartDate { get; set; }

        /// <summary>
        /// start-time — time of initial script execution. The special value <c>startup</c>
        /// runs the script a few seconds after the system boots.
        /// </summary>
        [TikProperty("start-time", WinboxLabel = "Start Time")]
        public TikField<string?> StartTime { get; set; }

        /// <summary>
        /// on-event — script source to run, or the name of a script from /system/script.
        /// <para>
        /// Marked <see cref="TikPropertyAttribute.IsFreeText"/> for the same reason as
        /// <c>SystemScript.Source</c>: when it holds a script body rather than a script name, it carries
        /// <c>;</c> and <c>=</c>, which the CLI's <c>as-value</c> output cannot escape.
        /// </para>
        /// </summary>
        [TikProperty("on-event", IsFreeText = true, WinboxLabel = "On Event")]
        public TikField<string?> OnEvent { get; set; }

        /// <summary>
        /// policy — comma-separated list of user policies this script runs under
        /// (e.g. <c>read,write,policy,test</c>). Combination of flags, kept as string.
        /// </summary>
        [TikProperty("policy", WinboxLabel = "Policy")]
        public TikField<string?> Policy { get; set; }

        /// <summary>
        /// owner — user that owns/created the scheduled task (read-only).
        /// </summary>
        [TikProperty("owner", IsReadOnly = true, WinboxLabel = "Owner")]
        public TikField<string?> Owner { get; private set; }

        /// <summary>
        /// run-count — counter tracking how many times the script has executed (read-only).
        /// </summary>
        [TikProperty("run-count", IsReadOnly = true, WinboxLabel = "Run Count")]
        public TikField<int?> RunCount { get; private set; }

        /// <summary>
        /// next-run — when the script is scheduled to run next (read-only).
        /// </summary>
        [TikProperty("next-run", IsReadOnly = true, WinboxLabel = "Next Run")]
        public TikField<string?> NextRun { get; private set; }

        /// <summary>
        /// disabled — whether the scheduled task is disabled.
        /// </summary>
        [TikProperty("disabled")]
        public TikField<bool?> Disabled { get; set; }

        /// <summary>
        /// comment.
        /// </summary>
        [TikProperty("comment")]
        public TikField<string?> Comment { get; set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Format("{0} (interval={1}, on-event={2})", Name, Interval, OnEvent);
        }
    }
}
