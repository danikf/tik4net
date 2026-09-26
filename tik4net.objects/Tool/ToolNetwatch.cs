using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Tool
{
    /// <summary>
    /// Netwatch monitors the state of hosts on the network. It does this by sending ICMP pings,
    /// TCP connections, HTTP/HTTPS requests, or DNS queries at a defined interval. When a state
    /// change is detected (up/down), user-defined scripts are executed. Available probe types in
    /// RouterOS 7: simple, icmp, tcp-conn, http-get, https-get, dns.
    /// <para>See also: https://help.mikrotik.com/docs/display/ROS/Netwatch</para>
    /// </summary>
    [TikEntity("/tool/netwatch", IncludeDetails = true)]
    public class ToolNetwatch
    {
        /// <summary>.id — primary key of the netwatch entry.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>name — entry name (optional label).</summary>
        [TikProperty("name", WinboxLabel = "Name")]
        public TikValue<string?> Name { get; set; }

        /// <summary>host — IP address or DNS name of the host to probe.</summary>
        [TikProperty("host", WinboxLabel = "Host")]
        public TikValue<string?> Host { get; set; }

        /// <summary>
        /// type — probe method. Determines which type-specific fields apply.
        /// RouterOS 7 adds dns and https-get in addition to the classic simple/icmp/tcp-conn/http-get.
        /// </summary>
        /// <seealso cref="NeType"/>
        [TikProperty("type", DefaultValue = "simple", WinboxLabel = "Type")]
        public TikValue<NeType?> Type { get; set; }

        /// <summary>Probe type values for <see cref="Type"/>.</summary>
        public enum NeType
        {
            /// <summary>simple — quick ICMP check (legacy/default; fewer options than icmp).</summary>
            [TikEnum("simple")] Simple,
            /// <summary>icmp — full ICMP probe with statistics (packet-count, thresholds, etc.).</summary>
            [TikEnum("icmp")] Icmp,
            /// <summary>tcp-conn — TCP connection probe.</summary>
            [TikEnum("tcp-conn")] TcpConn,
            /// <summary>http-get — HTTP GET probe.</summary>
            [TikEnum("http-get")] HttpGet,
            /// <summary>https-get — HTTPS GET probe (RouterOS 7).</summary>
            [TikEnum("https-get")] HttpsGet,
            /// <summary>dns — DNS query probe (RouterOS 7).</summary>
            [TikEnum("dns")] Dns,
        }

        // ── Timing ──────────────────────────────────────────────────────────────

        /// <summary>interval — time between successive probe attempts (e.g. "10s", "1m"). Default: 10s.</summary>
        [TikProperty("interval", DefaultValue = "10s", WinboxLabel = "Interval")]
        public TikValue<TikDuration?> Interval { get; set; }

        /// <summary>timeout — maximum wait time for a probe response. Default: 3s.</summary>
        [TikProperty("timeout", DefaultValue = "3s", WinboxLabel = "Timeout")]
        public TikValue<TikDuration?> Timeout { get; set; }

        /// <summary>start-delay — delay before the first probe after the entry is enabled. Default: 3s.</summary>
        [TikProperty("start-delay", DefaultValue = "3s", WinboxLabel = "Start Delay")]
        public TikValue<TikDuration?> StartDelay { get; set; }

        /// <summary>startup-delay — delay after a system restart before the first probe. Default: 5m.</summary>
        [TikProperty("startup-delay", DefaultValue = "5m", WinboxLabel = "Startup Delay")]
        public TikValue<TikDuration?> StartupDelay { get; set; }

        // ── Scripts ─────────────────────────────────────────────────────────────

        /// <summary>up-script — RouterOS script to execute when a host transitions from down to up.</summary>
        [TikProperty("up-script", IsFreeText = true)]
        public TikValue<string?> UpScript { get; set; }

        /// <summary>down-script — RouterOS script to execute when a host transitions from up to down.</summary>
        [TikProperty("down-script", IsFreeText = true)]
        public TikValue<string?> DownScript { get; set; }

        /// <summary>test-script — RouterOS script to execute after each probe (regardless of state).</summary>
        [TikProperty("test-script", IsFreeText = true)]
        public TikValue<string?> TestScript { get; set; }

        // ── Behaviour ───────────────────────────────────────────────────────────

        /// <summary>ignore-initial-up — when yes, skip up-script on the first Unknown→Up transition. Default: no.</summary>
        [TikProperty("ignore-initial-up", DefaultValue = "no", WinboxLabel = "Ignore Initial Up")]
        public TikValue<bool?> IgnoreInitialUp { get; set; }

        /// <summary>ignore-initial-down — when yes, skip down-script on the first Unknown→Down transition. Default: no.</summary>
        [TikProperty("ignore-initial-down", DefaultValue = "no", WinboxLabel = "Ignore Initial Down")]
        public TikValue<bool?> IgnoreInitialDown { get; set; }

        /// <summary>src-address — source IP address to use for probes.</summary>
        [TikProperty("src-address", WinboxLabel = "Src. Address")]
        public TikValue<string?> SrcAddress { get; set; }

        /// <summary>disabled — when true the entry is disabled and probes are not sent.</summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public TikValue<bool?> Disabled { get; set; }

        /// <summary>comment — free-form comment.</summary>
        [TikProperty("comment")]
        public TikValue<string?> Comment { get; set; }

        // ── ICMP-specific (type=icmp) ────────────────────────────────────────────

        /// <summary>packet-count — number of ICMP packets sent per probe cycle (type=icmp). Default: 10.</summary>
        [TikProperty("packet-count", WinboxLabel = "Packet Count")] // router default 10; omitted on add when left 0
        public TikValue<int?> PacketCount { get; set; }

        /// <summary>packet-interval — interval between individual ICMP packets within one probe cycle (type=icmp). Default: 50ms.</summary>
        [TikProperty("packet-interval", DefaultValue = "50ms", WinboxLabel = "Packet Interval")]
        public TikValue<TikDuration?> PacketInterval { get; set; }

        /// <summary>packet-size — IP datagram size for ICMP packets (type=icmp). Default: 54.</summary>
        [TikProperty("packet-size", WinboxLabel = "Packet Size")] // router default 54; omitted on add when left 0
        public TikValue<int?> PacketSize { get; set; }

        /// <summary>ttl — time-to-live value for ICMP packets (type=icmp). Default: 255.</summary>
        [TikProperty("ttl", WinboxLabel = "TTL")] // router default 255; omitted on add when left 0
        public TikValue<int?> Ttl { get; set; }

        /// <summary>accept-icmp-time-exceeded — accept ICMP Type 11 "Time Exceeded" as a valid response (type=icmp). Default: no.</summary>
        [TikProperty("accept-icmp-time-exceeded", DefaultValue = "no", WinboxLabel = "Accept ICMP Time Exceeded")]
        public TikValue<bool?> AcceptIcmpTimeExceeded { get; set; }

        /// <summary>early-failure-detection — stop the probe cycle early when a failure is already confirmed (type=icmp). Default: no.</summary>
        [TikProperty("early-failure-detection", DefaultValue = "no", WinboxLabel = "Early Failure Detection")]
        public TikValue<bool?> EarlyFailureDetection { get; set; }

        /// <summary>early-success-detection — stop the probe cycle early when success is already confirmed (type=icmp). Default: no.</summary>
        [TikProperty("early-success-detection", DefaultValue = "no", WinboxLabel = "Early Success Detection")]
        public TikValue<bool?> EarlySuccessDetection { get; set; }

        /// <summary>thr-max — maximum RTT threshold; probe fails when any packet exceeds this (type=icmp). Default: 1s.</summary>
        [TikProperty("thr-max", DefaultValue = "1s", WinboxLabel = "Thr. Max")]
        public TikValue<TikDuration?> ThrMax { get; set; }

        /// <summary>thr-avg — average RTT threshold (type=icmp). Default: 100ms.</summary>
        [TikProperty("thr-avg", DefaultValue = "100ms", WinboxLabel = "Thr. Avg")]
        public TikValue<TikDuration?> ThrAvg { get; set; }

        /// <summary>thr-stdev — RTT standard-deviation threshold (type=icmp). Default: 250ms.</summary>
        [TikProperty("thr-stdev", DefaultValue = "250ms", WinboxLabel = "Thr. Stdev")]
        public TikValue<TikDuration?> ThrStdev { get; set; }

        /// <summary>thr-jitter — jitter threshold (type=icmp). Default: 1s.</summary>
        [TikProperty("thr-jitter", DefaultValue = "1s", WinboxLabel = "Thr. Jitter")]
        public TikValue<TikDuration?> ThrJitter { get; set; }

        /// <summary>thr-loss-percent — packet loss percentage threshold; probe fails when loss exceeds this value (type=icmp). Default: 85.</summary>
        [TikProperty("thr-loss-percent", DefaultValue = "85", WinboxLabel = "Thr. Loss Percent")]
        public TikValue<string?> ThrLossPercent { get; set; }

        /// <summary>thr-loss-count — packet loss count threshold; probe fails when lost packets exceed this value (type=icmp). Default: 4294967295.</summary>
        [TikProperty("thr-loss-count", WinboxLabel = "Thr. Loss Count")] // router default 4294967295; omitted on add when left 0
        public TikValue<long?> ThrLossCount { get; set; }

        // ── TCP-specific (type=tcp-conn) ─────────────────────────────────────────

        /// <summary>port — TCP port number to connect to (type=tcp-conn; also used for http-get / https-get). Default: 80.</summary>
        [TikProperty("port", WinboxLabel = "Port")] // router default 80; omitted on add when left 0
        public TikValue<int?> Port { get; set; }

        /// <summary>thr-tcp-conn-time — TCP connection time threshold range (e.g. "5ms-30ms"); probe fails when connection time is outside this range (type=tcp-conn).</summary>
        [TikProperty("thr-tcp-conn-time")]
        public TikValue<TikDuration?> ThrTcpConnTime { get; set; }

        // ── HTTP/HTTPS-specific (type=http-get / https-get) ──────────────────────

        /// <summary>http-codes — comma-separated list of HTTP status codes considered a successful response (e.g. "200,301"). Default: empty (200–299 range applies).</summary>
        [TikProperty("http-codes", WinboxLabel = "HTTP Codes")]
        public TikValue<string?> HttpCodes { get; set; }

        /// <summary>thr-http-time — HTTP response time threshold; probe fails when response time exceeds this (type=http-get/https-get). Default: 10s.</summary>
        [TikProperty("thr-http-time", DefaultValue = "10s", WinboxLabel = "Thr. HTTP Time")]
        public TikValue<TikDuration?> ThrHttpTime { get; set; }

        /// <summary>certificate — name of a certificate to use for HTTPS verification (type=https-get).</summary>
        [TikProperty("certificate", WinboxLabel = "Certificate")]
        public TikValue<string?> Certificate { get; set; }

        /// <summary>check-certificate — when yes, validate the server certificate's trust chain (type=https-get). Default: no.</summary>
        [TikProperty("check-certificate", DefaultValue = "no", WinboxLabel = "Check Certificate")]
        public TikValue<bool?> CheckCertificate { get; set; }

        // ── DNS-specific (type=dns) ──────────────────────────────────────────────

        /// <summary>record-type — DNS record type to query (type=dns). Default: A.</summary>
        /// <seealso cref="DnsRecordType"/>
        [TikProperty("record-type", DefaultValue = "A", WinboxLabel = "Record Type")]
        public TikValue<DnsRecordType?> RecordType { get; set; }

        /// <summary>DNS record type values for <see cref="RecordType"/>.</summary>
        public enum DnsRecordType
        {
            /// <summary>A — IPv4 address record.</summary>
            [TikEnum("A")] A,
            /// <summary>AAAA — IPv6 address record.</summary>
            [TikEnum("AAAA")] Aaaa,
            /// <summary>MX — mail exchange record.</summary>
            [TikEnum("MX")] Mx,
            /// <summary>NS — name server record.</summary>
            [TikEnum("NS")] Ns,
        }

        /// <summary>dns-server — IP address of the DNS server to use for queries (type=dns). Defaults to the system DNS server.</summary>
        [TikProperty("dns-server", WinboxLabel = "DNS Server")]
        public TikValue<string?> DnsServer { get; set; }

        // ── Read-only status fields ──────────────────────────────────────────────

        /// <summary>status — current probe state: up, down, or unknown. Read-only.</summary>
        [TikProperty("status", IsReadOnly = true, WinboxLabel = "Status")]
        public TikValue<string?> Status { get; private set; }

        /// <summary>since — timestamp of the last state change. Read-only.</summary>
        [TikProperty("since", IsReadOnly = true, WinboxLabel = "Since")]
        public TikValue<string?> Since { get; private set; }

        /// <summary>done-tests — total number of probe attempts completed. Read-only.</summary>
        [TikProperty("done-tests", IsReadOnly = true, WinboxLabel = "Done Tests")]
        public TikValue<string?> DoneTests { get; private set; }

        /// <summary>failed-tests — number of failed probe attempts. Read-only.</summary>
        [TikProperty("failed-tests", IsReadOnly = true, WinboxLabel = "Failed Tests")]
        public TikValue<string?> FailedTests { get; private set; }

        // ── ICMP read-only counters (present when type=icmp and at least one probe has run) ──

        /// <summary>sent-count — number of ICMP packets sent in the last probe cycle. Read-only.</summary>
        [TikProperty("sent-count", IsReadOnly = true, WinboxLabel = "Sent Count")]
        public TikValue<string?> SentCount { get; private set; }

        /// <summary>response-count — number of ICMP responses received in the last probe cycle. Read-only.</summary>
        [TikProperty("response-count", IsReadOnly = true, WinboxLabel = "Response Count")]
        public TikValue<string?> ResponseCount { get; private set; }

        /// <summary>loss-count — number of lost ICMP packets in the last probe cycle. Read-only.</summary>
        [TikProperty("loss-count", IsReadOnly = true, WinboxLabel = "Loss Count")]
        public TikValue<string?> LossCount { get; private set; }

        /// <summary>loss-percent — packet loss percentage in the last probe cycle. Read-only.</summary>
        [TikProperty("loss-percent", IsReadOnly = true, WinboxLabel = "Loss Percent")]
        public TikValue<string?> LossPercent { get; private set; }

        // ── TCP read-only counters (present when type=tcp-conn and at least one probe has run) ──

        /// <summary>tcp-connect-time — measured TCP connection time in the last probe cycle. Read-only.</summary>
        [TikProperty("tcp-connect-time", IsReadOnly = true, WinboxLabel = "TCP Connect Time")]
        public TikValue<string?> TcpConnectTime { get; private set; }

        /// <summary>Returns a human-readable summary of this netwatch entry.</summary>
        public override string ToString()
        {
            return string.Format("{0} [{1}] => {2}", Host, Type, Status);
        }
    }
}
