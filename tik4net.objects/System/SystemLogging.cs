using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.System
{
    /// <summary>
    /// /system/logging — logging rules that map topic filters to a logging action.
    /// Each rule selects one or more <c>topics</c> (comma-separated, e.g. <c>info</c>, <c>error,warning</c>)
    /// and forwards matching messages to the named <c>action</c> (see /system/logging/action).
    /// Default rules ship with RouterOS and are marked <see cref="Default"/> = true / <see cref="Invalid"/> = false.
    /// </summary>
    [TikEntity("/system/logging", IncludeDetails = true)]
    public class SystemLogging
    {
        /// <summary>
        /// .id — primary key of the row.
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// topics — comma-separated list of log topics to match, e.g. <c>info</c>, <c>error</c>,
        /// <c>warning,critical</c>. Special value <c>!</c> (exclamation mark) negates a topic.
        /// Kept as a plain string because MikroTik accepts composite values and topic names
        /// can vary by RouterOS version.
        /// </summary>
        [TikProperty("topics", IsMandatory = true)]
        public string Topics { get; set; }

        /// <summary>
        /// action — name of the logging action (from /system/logging/action) that receives
        /// messages matching <see cref="Topics"/>. Common built-in action names: <c>memory</c>,
        /// <c>disk</c>, <c>echo</c>, <c>remote</c>.
        /// </summary>
        [TikProperty("action", IsMandatory = true)]
        public string Action { get; set; }

        /// <summary>
        /// prefix — text prepended to every log message that matches this rule.
        /// Empty string means no prefix.
        /// </summary>
        [TikProperty("prefix", DefaultValue = "")]
        public string Prefix { get; set; }

        /// <summary>
        /// regex — optional POSIX regular expression; only messages whose text matches
        /// this pattern are forwarded. Empty string disables filtering by regex.
        /// </summary>
        [TikProperty("regex", DefaultValue = "")]
        public string Regex { get; set; }

        /// <summary>
        /// disabled — when true the rule is inactive and no messages are forwarded.
        /// </summary>
        [TikProperty("disabled", DefaultValue = "false")]
        public bool Disabled { get; set; }

        /// <summary>
        /// invalid — read-only flag set by RouterOS when the rule references a non-existent
        /// action or is otherwise misconfigured.
        /// </summary>
        [TikProperty("invalid", IsReadOnly = true)]
        public bool Invalid { get; private set; }

        /// <summary>
        /// default — read-only flag that marks rules shipped with RouterOS as factory defaults.
        /// Default rules cannot be removed permanently; they are restored on reset.
        /// </summary>
        [TikProperty("default", IsReadOnly = true)]
        public bool Default { get; private set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Format("topics={0} → action={1}", Topics, Action);
        }
    }
}
