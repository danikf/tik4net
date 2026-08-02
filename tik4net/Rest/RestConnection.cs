using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using tik4net.Connection;

namespace tik4net.Rest
{
    /// <summary>
    /// MikroTik RouterOS REST API connection (HTTP/HTTPS).
    /// Requires RouterOS 7.1+ with www or www-ssl service enabled.
    /// </summary>
    /// <remarks>
    /// Rides the shared <see cref="TikCommandConnectionBase"/>: the command factory, low-level
    /// <see cref="TikCommandConnectionBase.CallCommandSync(string[])"/> dispatch, diagnostics and the generic
    /// <see cref="ITikCommand"/> (<see cref="TikGenericCommand"/>) are inherited. REST only supplies the three
    /// CRUD hooks (<see cref="RunPrint"/>/<see cref="RunAdd"/>/<see cref="RunNonQuery"/>) implemented over HTTP
    /// plus the request build (<see cref="RestRequestBuilder"/>), JSON parsing and HTTP-status→exception mapping.
    /// <para>Capability is <see cref="TikConnectionCapability.Crud"/> | <see cref="TikConnectionCapability.Listen"/>.
    /// Listen, async lists and monitors are emulated by polling, exactly as the CLI and native-WinBox
    /// transports do it (<see cref="PollingMonitorEngine"/>) — see <c>RunMonitorAsync</c> for what the
    /// router does and does not offer here. There is no <see cref="TikConnectionCapability.Streaming"/> and no
    /// Safe Mode: each call is an independent HTTP request, so RouterOS cannot bind safe mode's
    /// rollback-on-disconnect to "the connection"; the inherited <c>SafeMode*</c> methods throw.</para>
    /// <para>
    /// <b>Known RouterOS defect — REST logins are never released.</b> Using REST at all can leave rows in
    /// the router's <c>/user/active</c> table that never go away. This is a RouterOS bug (reported for
    /// 7.16 through 7.24rc1, several open MikroTik support tickets) and <b>no client can avoid or undo
    /// it</b>: the session lives above TCP, so neither <see cref="Close"/>, nor disposing this object, nor
    /// an HTTP <c>Connection: close</c> header, nor the client process exiting releases it, and RouterOS
    /// applies no idle timeout — rows measured on 7.23.2 were still listed after ~24 hours with no socket
    /// behind them. <c>/user/active/remove</c> refuses them (<c>action failed (6)</c>); only a reboot
    /// clears them. RouterOS usually reuses one session per (user, source address) rather than creating
    /// one per request, so rows accumulate slowly and irregularly rather than per call. To spot them
    /// afterwards, compare login and logout events in the router's own log
    /// (<c>/log print where message~"rest-api"</c>, topic <c>account</c>): a REST login is logged, its
    /// logout never is. Details and measurements in <c>Docs/findings-rest-api.md</c> §5.1.
    /// </para>
    /// </remarks>
    public class RestConnection : TikCommandConnectionBase, ITikMonitorTransport, IPollingMonitorHost
    {
        // How often a listen diff / a continuous monitor re-reads the router. Same cadence as the CLI and
        // native transports, which poll the same way for the same reason.
        private const int ListenPollIntervalMs = 1000;
        private const int MonitorPollIntervalMs = 1000;

        private readonly bool _useSsl;
        private readonly bool _allowInvalidCert;
        private readonly RemoteCertificateValidationCallback _certificateValidationCallback;
        private HttpClient _httpClient;
        private string _baseUrl;
        private string _authHeader;

        /// <inheritdoc/>
        protected override string DiagnosticPrefix => "REST";

        /// <summary>
        /// CRUD plus <see cref="TikConnectionCapability.Listen"/> (polled — see <c>RunMonitorAsync</c>).
        /// No <see cref="TikConnectionCapability.Streaming"/> and no Safe Mode.
        /// </summary>
        public override TikConnectionCapability Capabilities =>
            TikConnectionCapability.Crud | TikConnectionCapability.Listen;

        /// <summary>Creates a REST connection.</summary>
        /// <param name="useSsl">Use HTTPS (port 443) instead of HTTP (port 80).</param>
        /// <param name="allowInvalidCert">When <paramref name="useSsl"/>, accept self-signed/invalid certificates. Ignored when <paramref name="certificateValidationCallback"/> is set.</param>
        /// <param name="certificateValidationCallback">
        /// Optional custom certificate validation for HTTPS. When set, it takes full control and
        /// <paramref name="allowInvalidCert"/> is ignored. Shares its delegate shape with
        /// <see cref="tik4net.Api.ApiConnection"/>'s API-SSL validation, so the same callback can drive both
        /// transports via <see cref="TikConnectionSetup.CertificateValidationCallback"/>.
        /// </param>
        // Only constructible via TikConnectionSetup/ConnectionFactory (same assembly).
        internal RestConnection(bool useSsl = false, bool allowInvalidCert = true,
            RemoteCertificateValidationCallback certificateValidationCallback = null)
        {
            _useSsl = useSsl;
            _allowInvalidCert = allowInvalidCert;
            _certificateValidationCallback = certificateValidationCallback;
            DebugEnabled = System.Diagnostics.Debugger.IsAttached;
        }

        // ── Open / Close ──────────────────────────────────────────────────────

        /// <inheritdoc/>
        public override void Open(string host, string user, string password)
            => OpenInternal(host, _useSsl ? 443 : 80, user, password);

        /// <inheritdoc/>
        public override void Open(string host, int port, string user, string password)
            => OpenInternal(host, port, user, password);

        /// <inheritdoc/>
        public override Task OpenAsync(string host, string user, string password)
            => Task.Run(() => Open(host, user, password));

        /// <inheritdoc/>
        public override Task OpenAsync(string host, int port, string user, string password)
            => Task.Run(() => Open(host, port, user, password));

        private void OpenInternal(string host, int port, string user, string password)
        {
            string scheme = _useSsl ? "https" : "http";
            _baseUrl = $"{scheme}://{host}:{port}/rest";
            _authHeader = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));

            var handler = new HttpClientHandler();
            if (_useSsl)
            {
                if (_certificateValidationCallback != null)
                    handler.ServerCertificateCustomValidationCallback =
                        (request, cert, chain, errors) => _certificateValidationCallback(request, cert, chain, errors);
                else if (_allowInvalidCert)
                    handler.ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => true;
                // else: leave unset — HttpClientHandler performs standard OS chain/hostname validation
            }

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMilliseconds(Math.Max(SendTimeout, ReceiveTimeout))
            };
            _httpClient.DefaultRequestHeaders.Authorization =
                AuthenticationHeaderValue.Parse(_authHeader);

            // Probe: verify connectivity and credentials.
            try
            {
                SendHttpSync(new HttpRequestMessage(HttpMethod.Get, _baseUrl + "/system/resource"));
                // 401 = wrong credentials, already handled by SendHttpSync → TikConnectionLoginException
                SetOpened();
            }
            catch (TikConnectionLoginException)
            {
                _httpClient.Dispose();
                _httpClient = null;
                throw;
            }
            catch (Exception ex)
            {
                _httpClient.Dispose();
                _httpClient = null;
                throw new System.IO.IOException("REST connection failed: " + ex.Message, ex);
            }
        }

        /// <inheritdoc/>
        public override void Close()
        {
            SetClosed();
            _httpClient?.Dispose();
            _httpClient = null;
        }

        // ── CRUD hooks (over HTTP) ─────────────────────────────────────────────

        /// <inheritdoc/>
        internal override IList<TikRecordSentence> RunPrint(TikCommandDescriptor descriptor)
            => ExecuteRequestList(descriptor.CommandText, descriptor.Parameters);

        /// <inheritdoc/>
        internal override string RunAdd(TikCommandDescriptor descriptor)
        {
            var single = ExecuteRequestSingle(descriptor.CommandText, descriptor.Parameters);
            string id = null;
            single?.TryGetResponseField(TikSpecialProperties.Id, out id);
            return id;
        }

        /// <inheritdoc/>
        internal override void RunNonQuery(TikCommandDescriptor descriptor)
            => ExecuteRequest(descriptor.CommandText, descriptor.Parameters);

        // ── Monitor / async / listen (ITikMonitorTransport) ────────────────────

        /// <summary>
        /// Dispatches a callback-based async command (<c>ExecuteAsync</c>/<c>LoadAsync</c>/<c>LoadListenAsync</c>)
        /// onto a background worker that polls the router over ordinary HTTP requests: a <c>/path/listen</c>
        /// diffs snapshots by <c>.id</c>, a <c>/path/print</c> runs once off-thread, and a monitor verb is
        /// re-issued as a bounded one-shot on a timer (<c>ping</c>/<c>traceroute</c> run once and complete).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>RouterOS does have a REST <c>listen</c>, and it cannot be used.</b> The verb is real — POST
        /// <c>/rest/system/resource/listen</c> answers <c>400 unknown parameter follow-only</c>, which is the
        /// singleton refusing the <c>follow-only</c> that REST itself adds, not "no such command". On a table
        /// it is accepted and the request is held open. But nothing ever comes out of it: measured on 7.23.2,
        /// three windows of 20-30 s on <c>/ip/address/listen</c> and <c>/log/listen</c> received <b>0 bytes</b>
        /// while an add, a set and 60 log lines happened inside them. RouterOS buffers the whole response and
        /// flushes it when the command completes, and <c>listen</c> never completes — so the caller waits
        /// forever for a body that is being accumulated on the router. That is why this transport polls:
        /// not because "REST is stateless HTTP", but because the router's own streaming form delivers nothing.
        /// </para>
        /// <para>
        /// The same buffering decides how monitors are driven. An unbounded monitor over REST hangs with no
        /// output for the same reason (<c>POST /rest/ping {address}</c> and <c>POST /rest/interface/monitor-traffic
        /// {interface}</c> both received 0 bytes in 8 s), so every monitor is given the snapshot bound its verb
        /// takes — <see cref="TikMonitorVerbs.SnapshotBound"/>, the same fact the CLI transports append to the
        /// command line — unless the caller supplied that parameter themselves.
        /// </para>
        /// <para>
        /// Consequence worth knowing: an async <c>/ping</c> with no <c>count</c> is bounded to one reply here,
        /// where the binary API would stream replies until cancelled. That matches what the CLI transports do
        /// with the same command, and it is the reason to prefer an explicit <c>count</c> on a REST ping.
        /// </para>
        /// </remarks>
        TikMonitorHandle ITikMonitorTransport.RunMonitorAsync(TikCommandDescriptor descriptor,
            Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
        {
            EnsureOpened();
            string verb = TikPath.Verb(descriptor.CommandText);

            if (verb == "listen")
            {
                string listPath = TikPath.Parent(descriptor.CommandText);
                var printDescriptor = new TikCommandDescriptor(listPath + "/print", descriptor.Parameters);
                return PollingMonitorEngine.StartWorker("rest-listen",
                    h => PollingMonitorEngine.ListenLoop(this, printDescriptor, null, ListenPollIntervalMs, h, onRow, onError, onDone));
            }

            if (verb == "print" || verb == "getall")
                return PollingMonitorEngine.StartWorker("rest-asynclist",
                    h => PollingMonitorEngine.AsyncListOnce(this, descriptor, h, onRow, onError, onDone));

            var bounded = ApplySnapshotBound(descriptor, verb);

            // Self-terminating (ping/traceroute): one request → N rows → done, like the binary API's async
            // ping. Re-polling it would multiply the row count.
            if (TikMonitorVerbs.SelfTerminating(verb))
                return PollingMonitorEngine.StartWorker("rest-monitor-once",
                    h => MonitorOnce(bounded, h, onRow, onError, onDone));

            return PollingMonitorEngine.StartWorker("rest-monitor",
                h => PollingMonitorEngine.SnapshotLoop(this, bounded, MonitorPollIntervalMs, h, onRow, onError, onDone));
        }

        // ── IPollingMonitorHost (the loop scaffolding lives in PollingMonitorEngine) ──

        /// <inheritdoc/>
        bool IPollingMonitorHost.IsOpen => IsOpened;

        /// <inheritdoc/>
        IList<TikRecordSentence> IPollingMonitorHost.PollSnapshot(TikCommandDescriptor printDescriptor)
            => RunPrint(printDescriptor);

        /// <inheritdoc/>
        TikTrapSentenceResult IPollingMonitorHost.ToTrap(Exception ex) => new TikTrapSentenceResult(ex.Message);

        /// <summary>
        /// Runs a self-terminating monitor once and emits its rows. Deliberately not
        /// <see cref="PollingMonitorEngine.AsyncListOnce"/>: that one strips Filter-format parameters and
        /// evaluates them as a client-side query, and a monitor's parameters are its INPUTS, not a filter over
        /// a table — the distinction P2.51 is about.
        /// </summary>
        private void MonitorOnce(TikCommandDescriptor descriptor, TikMonitorHandle handle,
            Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
        {
            try
            {
                foreach (var row in RunPrint(descriptor))
                {
                    if (handle.CancelRequested) break;
                    onRow?.Invoke(row);
                }
            }
            catch (Exception ex)
            {
                if (!PollingMonitorEngine.Stopping(this, handle)) onError?.Invoke(new TikTrapSentenceResult(ex.Message));
            }
            finally { onDone?.Invoke(); }
        }

        // Appends the verb's snapshot bound (once / count=1 / duration=N) unless the caller already supplied a
        // parameter of that name — without it RouterOS runs the monitor forever and, because it buffers the
        // response until the command ends, answers with nothing at all rather than with a partial reading.
        // internal rather than private so the unit tests can pin it: getting this wrong does not fail, it
        // hangs — the worker sits on a request the router will never answer and the caller sees no rows,
        // no error and no completion.
        internal static TikCommandDescriptor ApplySnapshotBound(TikCommandDescriptor descriptor, string verb)
        {
            TikMonitorVerbs.SnapshotBound(verb, out string name, out string value);

            foreach (var p in descriptor.Parameters)
                if (string.Equals(p.Name.TrimStart('?', '='), name, StringComparison.OrdinalIgnoreCase))
                    return descriptor;

            var parameters = new List<ITikCommandParameter>(descriptor.Parameters)
            {
                new TikCommandParameter(name, value, TikCommandParameterFormat.NameValue),
            };
            return new TikCommandDescriptor(descriptor.CommandText, parameters);
        }

        // ── HTTP execution ─────────────────────────────────────────────────────

        // Only reached from RunNonQuery, so the builder is told so: an unrecognised trailing segment here is
        // an action verb, not a menu name (P2.48 — /log/error and friends).
        private void ExecuteRequest(string commandText, IList<ITikCommandParameter> parameters)
        {
            EnsureOpened();
            var req = RestRequestBuilder.Build(commandText, parameters, RestRequestBuilder.RestCallKind.NonQuery);
            FireWriteRow(req.Method.Method + " " + req.RelativePath);

            var httpResp = SendHttpSync(BuildHttpRequest(req));
            var body = httpResp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            FireReadRow(body);
            // parse errors only; we don't return anything
            ParseErrorOrIgnore(commandText, body, (int)httpResp.StatusCode, parameters);
        }

        private IList<TikRecordSentence> ExecuteRequestList(string commandText, IList<ITikCommandParameter> parameters)
        {
            EnsureOpened();
            var req = RestRequestBuilder.Build(commandText, parameters);
            FireWriteRow(req.Method.Method + " " + req.RelativePath);

            var httpResp = SendHttpSync(BuildHttpRequest(req));
            var body = httpResp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            FireReadRow(body);

            if (string.IsNullOrWhiteSpace(body) || body == "null" || body == "[]" || body == "{}")
                return new List<TikRecordSentence>();

            // Try to detect error first
            ParseErrorOrIgnore(commandText, body, (int)httpResp.StatusCode, parameters);

            return ParseResponseList(body);
        }

        private TikRecordSentence ExecuteRequestSingle(string commandText, IList<ITikCommandParameter> parameters)
        {
            EnsureOpened();
            var req = RestRequestBuilder.Build(commandText, parameters);
            FireWriteRow(req.Method.Method + " " + req.RelativePath);

            var httpResp = SendHttpSync(BuildHttpRequest(req));
            var body = httpResp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            FireReadRow(body);

            if (string.IsNullOrWhiteSpace(body) || body == "null" || body == "{}")
                return null;

            ParseErrorOrIgnore(commandText, body, (int)httpResp.StatusCode, parameters);

            return ParseSingleObject(body);
        }

        // ── HTTP helpers ───────────────────────────────────────────────────────

        private HttpRequestMessage BuildHttpRequest(RestRequestBuilder.RestRequest req)
        {
            var httpReq = new HttpRequestMessage(req.Method, _baseUrl + req.RelativePath);
            httpReq.Headers.Authorization = AuthenticationHeaderValue.Parse(_authHeader);

            if (req.JsonBody != null)
                httpReq.Content = new StringContent(req.JsonBody, Encoding.UTF8, "application/json");
            return httpReq;
        }

        private HttpResponseMessage SendHttpSync(HttpRequestMessage req)
        {
            var response = _httpClient.SendAsync(req).GetAwaiter().GetResult();

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                throw new TikConnectionLoginException(new Exception("HTTP 401 Unauthorized — check credentials."));

            return response;
        }

        // ── Response parsing ───────────────────────────────────────────────────

        private void ParseErrorOrIgnore(string commandText, string body, int statusCode, IList<ITikCommandParameter> parameters)
        {
            if (statusCode >= 200 && statusCode < 300)
                return;

            // Try to parse REST error body
            string message = null;
            string detail = null;
            try
            {
                using (var doc = JsonDocument.Parse(body))
                {
                    if (doc.RootElement.TryGetProperty("message", out var msgEl))
                        message = msgEl.GetString();
                    if (doc.RootElement.TryGetProperty("detail", out var detEl))
                        detail = detEl.GetString();
                }
            }
            catch { /* ignore JSON parse error, use raw body */ }

            string fullMessage = message ?? body;
            if (!string.IsNullOrEmpty(detail) && detail != message)
                fullMessage += ": " + detail;

            // Check both the combined message and the detail field independently for known patterns.
            // The specific-kind classification is shared with the API and CLI transports via TikTrapClassifier;
            // only REST's own signal (a bare 404 with no matching message text) is handled here.
            string checkText = (detail ?? "") + " " + (message ?? "") + " " + fullMessage;

            var fakeCmd = new TikGenericCommand(this, commandText, parameters.ToArray());
            var trapSentence = new TikTrapSentenceResult(fullMessage);

            var kind = TikTrapClassifier.Classify(checkText);
            if (kind == TikTrapKind.Generic && statusCode == 404)
                kind = TikTrapKind.NoSuchItem;

            switch (kind)
            {
                case TikTrapKind.NoSuchCommand:
                    throw new TikNoSuchCommandException(fakeCmd, trapSentence);
                case TikTrapKind.NoSuchItem:
                    throw new TikNoSuchItemException(fakeCmd, trapSentence);
                case TikTrapKind.AlreadyHaveSuchItem:
                    throw new TikAlreadyHaveSuchItemException(fakeCmd, trapSentence);
                default:
                    throw new TikCommandTrapException(fakeCmd, trapSentence);
            }
        }

        private static IList<TikRecordSentence> ParseResponseList(string body)
        {
            body = body.Trim();

            if (body.StartsWith("["))
            {
                // JSON array
                var result = new List<TikRecordSentence>();
                using (var doc = JsonDocument.Parse(body))
                {
                    foreach (var el in doc.RootElement.EnumerateArray())
                        result.Add(ParseJsonObject(el));
                }
                return result;
            }
            if (body.StartsWith("{"))
            {
                // Single object returned as list
                var single = ParseSingleObject(body);
                return single != null ? new List<TikRecordSentence> { single } : new List<TikRecordSentence>();
            }

            return new List<TikRecordSentence>();
        }

        private static TikRecordSentence ParseSingleObject(string body)
        {
            using (var doc = JsonDocument.Parse(body))
            {
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    return ParseJsonObject(doc.RootElement);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in doc.RootElement.EnumerateArray())
                        return ParseJsonObject(el);
                }
            }
            return null;
        }

        private static TikRecordSentence ParseJsonObject(JsonElement el)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in el.EnumerateObject())
            {
                string val = prop.Value.ValueKind == JsonValueKind.Null
                    ? string.Empty
                    : prop.Value.ToString();
                fields[prop.Name] = val;
            }
            return new TikRecordSentence(fields);
        }
    }
}
