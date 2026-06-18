using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.System
{
    /// <summary>
    /// /system/logging/action — defines where log entries are stored or sent.
    /// Each action specifies a target (memory buffer, disk file, console echo, remote syslog/email)
    /// and the target-specific parameters. Built-in actions (memory, disk, echo, remote) are
    /// marked read-only via the <see cref="Default"/> flag and cannot be deleted.
    /// </summary>
    [TikEntity("/system/logging/action", IncludeDetails = true)]
    public class SystemLoggingAction
    {
        /// <summary>Target type for a logging action.</summary>
        public enum LoggingTarget
        {
            /// <summary>Store log entries in an in-memory circular buffer.</summary>
            [TikEnum("memory")] Memory,
            /// <summary>Write log entries to a file on the router disk.</summary>
            [TikEnum("disk")] Disk,
            /// <summary>Print log entries to the console/terminal (echo).</summary>
            [TikEnum("echo")] Echo,
            /// <summary>Send log entries to a remote syslog server.</summary>
            [TikEnum("remote")] Remote,
            /// <summary>Send log entries via e-mail.</summary>
            [TikEnum("email")] Email,
        }

        /// <summary>Transport protocol used when target is <see cref="LoggingTarget.Remote"/>.</summary>
        public enum RemoteProtocolType
        {
            /// <summary>UDP (default, RFC 3164 syslog).</summary>
            [TikEnum("udp")] Udp,
            /// <summary>TCP.</summary>
            [TikEnum("tcp")] Tcp,
            /// <summary>TLS (encrypted TCP).</summary>
            [TikEnum("tls")] Tls,
        }

        /// <summary>Wire format when target is <see cref="LoggingTarget.Remote"/>.</summary>
        public enum RemoteLogFormatType
        {
            /// <summary>RouterOS default format.</summary>
            [TikEnum("default")] Default,
            /// <summary>RFC 3164 syslog format.</summary>
            [TikEnum("syslog")] Syslog,
            /// <summary>Common Event Format (CEF).</summary>
            [TikEnum("cef")] Cef,
        }

        /// <summary>RFC 3164 syslog facility code.</summary>
        public enum SyslogFacilityType
        {
            /// <summary>kernel messages</summary>
            [TikEnum("kern")] Kern,
            /// <summary>user-level messages</summary>
            [TikEnum("user")] User,
            /// <summary>mail system</summary>
            [TikEnum("mail")] Mail,
            /// <summary>system daemons (default)</summary>
            [TikEnum("daemon")] Daemon,
            /// <summary>security/authorization messages</summary>
            [TikEnum("auth")] Auth,
            /// <summary>messages generated internally by syslogd</summary>
            [TikEnum("syslog")] Syslog,
            /// <summary>line printer subsystem</summary>
            [TikEnum("lpr")] Lpr,
            /// <summary>network news subsystem</summary>
            [TikEnum("news")] News,
            /// <summary>UUCP subsystem</summary>
            [TikEnum("uucp")] Uucp,
            /// <summary>clock daemon (cron)</summary>
            [TikEnum("cron")] Cron,
            /// <summary>security/authorization messages (private)</summary>
            [TikEnum("authpriv")] Authpriv,
            /// <summary>FTP daemon</summary>
            [TikEnum("ftp")] Ftp,
            /// <summary>NTP subsystem</summary>
            [TikEnum("ntp")] Ntp,
            /// <summary>locally defined</summary>
            [TikEnum("local0")] Local0,
            /// <summary>locally defined</summary>
            [TikEnum("local1")] Local1,
            /// <summary>locally defined</summary>
            [TikEnum("local2")] Local2,
            /// <summary>locally defined</summary>
            [TikEnum("local3")] Local3,
            /// <summary>locally defined</summary>
            [TikEnum("local4")] Local4,
            /// <summary>locally defined</summary>
            [TikEnum("local5")] Local5,
            /// <summary>locally defined</summary>
            [TikEnum("local6")] Local6,
            /// <summary>locally defined</summary>
            [TikEnum("local7")] Local7,
        }

        /// <summary>RFC 3164 syslog severity level.</summary>
        public enum SyslogSeverityType
        {
            /// <summary>Map RouterOS severity automatically (default).</summary>
            [TikEnum("auto")] Auto,
            /// <summary>System is unusable.</summary>
            [TikEnum("emergency")] Emergency,
            /// <summary>Action must be taken immediately.</summary>
            [TikEnum("alert")] Alert,
            /// <summary>Critical conditions.</summary>
            [TikEnum("critical")] Critical,
            /// <summary>Error conditions.</summary>
            [TikEnum("error")] Error,
            /// <summary>Warning conditions.</summary>
            [TikEnum("warning")] Warning,
            /// <summary>Normal but significant condition.</summary>
            [TikEnum("notice")] Notice,
            /// <summary>Informational messages.</summary>
            [TikEnum("info")] Info,
            /// <summary>Debug-level messages.</summary>
            [TikEnum("debug")] Debug,
        }

        /// <summary>Timestamp format in syslog messages.</summary>
        public enum SyslogTimeFormatType
        {
            /// <summary>Classic BSD syslog timestamp (default, e.g. "Jan  1 00:00:00").</summary>
            [TikEnum("bsd-syslog")] BsdSyslog,
            /// <summary>ISO 8601 timestamp with milliseconds.</summary>
            [TikEnum("iso8601")] Iso8601,
        }

        // ── identity ──────────────────────────────────────────────────────────

        /// <summary>.id — primary key of the row.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// name — unique action name; for the <c>memory</c> target this is also the name of the
        /// in-memory log buffer visible in /log print.
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// target — storage destination for log entries.
        /// <seealso cref="LoggingTarget"/>
        /// </summary>
        [TikProperty("target", DefaultValue = "memory", IsMandatory = true)]
        public LoggingTarget Target { get; set; }

        // ── memory target ─────────────────────────────────────────────────────

        /// <summary>
        /// memory-lines — maximum number of records kept in the in-memory buffer (memory target only).
        /// Oldest entries are dropped when the limit is reached (unless <see cref="MemoryStopOnFull"/> is set).
        /// </summary>
        [TikProperty("memory-lines")] // router default 1000; omitted on add when left 0
        public int MemoryLines { get; set; }

        /// <summary>
        /// memory-stop-on-full — stop logging when the memory buffer is full (memory target only).
        /// </summary>
        [TikProperty("memory-stop-on-full", DefaultValue = "false")]
        public bool MemoryStopOnFull { get; set; }

        // ── disk target ───────────────────────────────────────────────────────

        /// <summary>
        /// disk-file-name — base name of the log file written to disk (disk target only).
        /// </summary>
        [TikProperty("disk-file-name", DefaultValue = "log")]
        public string DiskFileName { get; set; }

        /// <summary>
        /// disk-lines-per-file — maximum number of log lines per file before rotating (disk target only).
        /// </summary>
        [TikProperty("disk-lines-per-file")] // router default 100; omitted on add when left 0
        public int DiskLinesPerFile { get; set; }

        /// <summary>
        /// disk-file-count — number of rotated log files to keep (disk target only).
        /// </summary>
        [TikProperty("disk-file-count")] // router default 2; omitted on add when left 0
        public int DiskFileCount { get; set; }

        /// <summary>
        /// disk-stop-on-full — stop logging when all disk files are full (disk target only).
        /// </summary>
        [TikProperty("disk-stop-on-full", DefaultValue = "false")]
        public bool DiskStopOnFull { get; set; }

        // ── echo target ───────────────────────────────────────────────────────

        /// <summary>
        /// remember — keep unread console messages highlighted until viewed (echo target only).
        /// </summary>
        [TikProperty("remember")]
        public bool Remember { get; set; }

        // ── remote target ─────────────────────────────────────────────────────

        /// <summary>
        /// remote — IP address of the remote syslog server (remote target only).
        /// </summary>
        [TikProperty("remote", DefaultValue = "0.0.0.0")]
        public string/*IPv4*/ Remote { get; set; }

        /// <summary>
        /// remote-port — UDP/TCP port on the remote syslog server (remote target only).
        /// </summary>
        [TikProperty("remote-port")] // router default 514; omitted on add when left 0
        public int RemotePort { get; set; }

        /// <summary>
        /// remote-protocol — transport protocol used to reach the remote syslog server (remote target only).
        /// <seealso cref="RemoteProtocolType"/>
        /// </summary>
        [TikProperty("remote-protocol", DefaultValue = "udp")]
        public RemoteProtocolType RemoteProtocol { get; set; }

        /// <summary>
        /// remote-log-format — wire format of messages sent to the remote server (remote target only).
        /// <seealso cref="RemoteLogFormatType"/>
        /// </summary>
        [TikProperty("remote-log-format", DefaultValue = "default")]
        public RemoteLogFormatType RemoteLogFormat { get; set; }

        /// <summary>
        /// src-address — source IP address used when connecting to the remote syslog server.
        /// 0.0.0.0 means the router selects the address automatically.
        /// </summary>
        [TikProperty("src-address", DefaultValue = "0.0.0.0")]
        public string/*IPv4*/ SrcAddress { get; set; }

        /// <summary>
        /// vrf — VRF context used for remote syslog connections (RouterOS 7.19+).
        /// </summary>
        [TikProperty("vrf", DefaultValue = "main")]
        public string Vrf { get; set; }

        /// <summary>
        /// syslog-facility — RFC 3164 facility code included in syslog messages (remote target only).
        /// <seealso cref="SyslogFacilityType"/>
        /// </summary>
        [TikProperty("syslog-facility", DefaultValue = "daemon")]
        public SyslogFacilityType SyslogFacility { get; set; }

        /// <summary>
        /// syslog-severity — RFC 3164 severity level override; <c>auto</c> maps RouterOS topic severity
        /// automatically (remote target only).
        /// <seealso cref="SyslogSeverityType"/>
        /// </summary>
        [TikProperty("syslog-severity", DefaultValue = "auto")]
        public SyslogSeverityType SyslogSeverity { get; set; }

        /// <summary>
        /// syslog-time-format — timestamp format used in outgoing syslog messages (remote target only).
        /// <seealso cref="SyslogTimeFormatType"/>
        /// </summary>
        [TikProperty("syslog-time-format", DefaultValue = "bsd-syslog")]
        public SyslogTimeFormatType SyslogTimeFormat { get; set; }

        /// <summary>
        /// cef-event-delimiter — delimiter between CEF events in a batched UDP packet
        /// (remote target with <see cref="RemoteLogFormatType.Cef"/> only).
        /// Default is CRLF (<c>\r\n</c>).
        /// </summary>
        [TikProperty("cef-event-delimiter")]
        public string CefEventDelimiter { get; set; }

        // ── email target ──────────────────────────────────────────────────────

        /// <summary>
        /// email-to — recipient e-mail address (email target only).
        /// </summary>
        [TikProperty("email-to")]
        public string EmailTo { get; set; }

        /// <summary>
        /// email-start-tls — use STARTTLS when connecting to the SMTP relay (email target only).
        /// </summary>
        [TikProperty("email-start-tls", DefaultValue = "false")]
        public bool EmailStartTls { get; set; }

        // ── read-only / meta ──────────────────────────────────────────────────

        /// <summary>
        /// default — true for the four factory-default actions (memory, disk, echo, remote).
        /// These cannot be deleted (read-only).
        /// </summary>
        [TikProperty("default", IsReadOnly = true)]
        public bool Default { get; private set; }

        /// <summary>comment</summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Format("{0} ({1})", Name, Target);
        }
    }
}
