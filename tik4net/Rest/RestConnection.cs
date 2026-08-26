using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Connection;
using tik4net.Diagnostics;

namespace tik4net.Rest
{
    /// <summary>
    /// MikroTik RouterOS REST API connection (HTTP/HTTPS).
    /// Requires RouterOS 7.1+ with www or www-ssl service enabled.
    /// </summary>
    /// <remarks>
    /// Rides the shared <see cref="TikCommandConnectionBase"/>: the command factory, diagnostics and the
    /// generic <see cref="ITikCommand"/> (<see cref="TikGenericCommand"/>) are inherited. REST only supplies the three
    /// CRUD hooks (<see cref="RunPrint"/>/<see cref="RunAdd"/>/<see cref="RunNonQuery"/>) implemented over HTTP
    /// plus the request build (<see cref="RestRequestBuilder"/>), JSON parsing and HTTP-status→exception mapping.
    /// <para>Capability is <see cref="TikConnectionCapability.Crud"/> | <see cref="TikConnectionCapability.Listen"/> |
    /// <see cref="TikConnectionCapability.AsyncCommands"/> | <see cref="TikConnectionCapability.CancelInFlight"/>.
    /// Listen and the callback monitors are emulated by polling, exactly as the CLI and native-WinBox
    /// transports do it (<see cref="PollingMonitorEngine"/>) — see <c>RunMonitorAsync</c> for what the
    /// router does and does not offer here. There is no <see cref="TikConnectionCapability.Streaming"/> and no
    /// Safe Mode: each call is an independent HTTP request, so RouterOS cannot bind safe mode's
    /// rollback-on-disconnect to "the connection"; the inherited <c>SafeMode*</c> methods throw.</para>
    /// <para>
    /// The Task-based command surface (<see cref="ITikCommandAsync"/>, reached through the <c>Execute*Async</c>
    /// extension methods) is <b>native</b> here rather than emulated: <c>HttpClient</c> is async to the bottom, so
    /// the async hooks are the real implementation and the synchronous ones block on them. That independence of
    /// each request is also what makes REST the cleanest cancellation story of all twelve transports — a cancelled
    /// token aborts the HTTP request, and since nothing is shared between requests, nothing that follows can be
    /// desynchronized by it. <b>But an aborted request does not stop the command on the router</b>: RouterOS runs
    /// it to the end and keeps its REST session busy meanwhile (measured in <c>Docs/findings-rest-api.md</c> §12.1),
    /// so cancelling a long monitor frees the caller, not the router.
    /// </para>
    /// <para>
    /// <see cref="ITikConnection.ConnectTimeout"/> bounds the whole <c>Open</c> probe here rather than the
    /// TCP handshake alone, on both target frameworks. <c>SocketsHttpHandler.ConnectTimeout</c> — the only
    /// thing that separates the two — does not exist on <c>netstandard2.0</c>, and <c>HttpClientHandler</c>
    /// exposes nothing equivalent; it does exist on <c>net8.0</c>, and this transport deliberately does not
    /// branch on the target framework, because one bound that means the same thing everywhere is worth more
    /// than a slightly tighter one on half the builds. The probe is a single small
    /// <c>GET /rest/system/resource</c>, so bounding it whole is the same guarantee in practice: an
    /// unreachable or black-holed router cannot hold <c>Open</c> for the OS connect default.
    /// A value of 0 or less means "no bound".
    /// </para>
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
    public sealed class RestConnection : TikCommandConnectionBase, ITikRestConnection,
        ITikMonitorTransport, IPollingMonitorHost
    {
        // How often a listen diff / a continuous monitor re-reads the router. Same cadence as the CLI and
        // native transports, which poll the same way for the same reason.
        private const int ListenPollIntervalMs = 1000;
        private const int MonitorPollIntervalMs = 1000;

        private readonly bool _useSsl;
        // _baseUrl/_authHeader: assigned in OpenInternalAsync (Open/OpenAsync), not the constructor — nothing
        // reads them before Open. _httpClient is genuinely nullable: Close() and a failed Open reset it to null.
        private HttpClient? _httpClient;
        private string _baseUrl = null!;
        private string _authHeader = null!;

        /// <inheritdoc/>
        protected override string DiagnosticPrefix => "REST";

        /// <summary>
        /// CRUD, <see cref="TikConnectionCapability.Listen"/> (polled — see <c>RunMonitorAsync</c>),
        /// <see cref="TikConnectionCapability.AsyncCommands"/> and <see cref="TikConnectionCapability.CancelInFlight"/>.
        /// No <see cref="TikConnectionCapability.Streaming"/> and no Safe Mode.
        /// </summary>
        public override TikConnectionCapability Capabilities =>
            TikConnectionCapability.Crud | TikConnectionCapability.Listen
            | TikConnectionCapability.AsyncCommands | TikConnectionCapability.CancelInFlight;

        /// <inheritdoc/>
        /// <remarks>
        /// Applies to HTTPS only (<see cref="TikConnectionType.RestSsl"/>); on plain HTTP there is no
        /// certificate to judge. Shares its delegate shape with <see cref="tik4net.Api.ApiConnection"/>'s
        /// API-SSL validation, so one callback set on
        /// <see cref="TikConnectionSetup.CertificateValidationCallback"/> drives both transports.
        /// </remarks>
        public bool AllowInvalidCertificate { get; set; }

        /// <inheritdoc/>
        public RemoteCertificateValidationCallback? CertificateValidationCallback { get; set; }

        /// <summary>Creates a REST connection.</summary>
        /// <param name="useSsl">Use HTTPS (port 443) instead of HTTP (port 80).</param>
        // Only constructible via TikConnectionSetup/ConnectionFactory (same assembly).
        internal RestConnection(bool useSsl = false)
        {
            _useSsl = useSsl;
            DebugEnabled = System.Diagnostics.Debugger.IsAttached;
        }

        // ── Open / Close ──────────────────────────────────────────────────────

        // As with the CRUD hooks below, the async form is the implementation and the synchronous one blocks
        // on it (D5). OpenAsync used to be `Task.Run(() => Open(...))` — which held a thread-pool thread for
        // the whole connectivity/credential probe, an HTTP round trip that SendHttpAsync could already await.
        // A Task.Run façade is exactly what the AsyncCommands capability exists to rule out, so leaving one
        // on the open path made the flag mean less than it says (P2.5).

        /// <inheritdoc/>
        public override void Open(string host, string user, string password)
            => OpenInternalAsync(host, _useSsl ? 443 : 80, user, password).GetAwaiter().GetResult();

        /// <inheritdoc/>
        public override void Open(string host, int port, string user, string password)
            => OpenInternalAsync(host, port, user, password).GetAwaiter().GetResult();

        /// <inheritdoc/>
        public override Task OpenAsync(string host, string user, string password)
            => OpenInternalAsync(host, _useSsl ? 443 : 80, user, password);

        /// <inheritdoc/>
        public override Task OpenAsync(string host, int port, string user, string password)
            => OpenInternalAsync(host, port, user, password);

        private async Task OpenInternalAsync(string host, int port, string user, string password)
        {
            string scheme = _useSsl ? "https" : "http";
            _baseUrl = $"{scheme}://{host}:{port}/rest";
            _authHeader = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));

            var handler = new HttpClientHandler();
            if (_useSsl)
            {
                var validate = CertificateValidationCallback;
                if (validate != null)
                    handler.ServerCertificateCustomValidationCallback =
                        (request, cert, chain, errors) => validate(request, cert, chain, errors);
                else if (AllowInvalidCertificate)
                    handler.ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => true;
                // else: leave unset — HttpClientHandler performs standard OS chain/hostname validation
            }

            // Infinite here, and bounded per request in SendHttpAsync instead: HttpClient.Timeout is one value
            // for the client's whole lifetime, and the open probe and the commands after it are bounded by
            // different settings (ConnectTimeout vs Send/ReceiveTimeout). Leaving it set would make the
            // effective bound the smaller of the two, which is neither of the numbers the caller configured.
            _httpClient = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
            _httpClient.DefaultRequestHeaders.Authorization =
                AuthenticationHeaderValue.Parse(_authHeader);

            // Probe: verify connectivity and credentials.
            try
            {
                await SendHttpAsync(new HttpRequestMessage(HttpMethod.Get, _baseUrl + "/system/resource"),
                    ConnectTimeout, CancellationToken.None).ConfigureAwait(false);
                // 401 = wrong credentials, already handled by SendHttpAsync → TikConnectionLoginException
                SetOpened();
            }
            catch (TikConnectionLoginException)
            {
                _httpClient.Dispose();
                _httpClient = null;
                throw;
            }
            catch (TikConnectionReceiveTimeoutException ex)
            {
                // Reported as a connect failure: what ran out is ConnectTimeout, and the generic wrapper below
                // would pass on the inner exception's "no response received" wording — which describes a read
                // on an established connection, not an open that never got that far.
                _httpClient.Dispose();
                _httpClient = null;
                throw new System.IO.IOException(
                    $"REST connection to {host}:{port} timed out: the router did not answer the open probe " +
                    $"within ConnectTimeout ({ConnectTimeout} ms).", ex);
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
        //
        // HttpClient is async to the bottom, so the Task-based hooks are the real implementation here and the
        // synchronous ones block on them (D5: async is the primitive, and nothing is pushed onto a thread-pool
        // thread to look asynchronous). Every await inside carries ConfigureAwait(false), which is also what keeps
        // the blocking wrapper safe to call from a UI or ASP.NET-classic SynchronizationContext.

        /// <inheritdoc/>
        protected override IList<TikRecordSentence> RunPrint(TikCommandDescriptor descriptor)
            => RunPrintAsync(descriptor, CancellationToken.None).GetAwaiter().GetResult();

        /// <inheritdoc/>
        protected override string RunAdd(TikCommandDescriptor descriptor)
            => RunAddAsync(descriptor, CancellationToken.None).GetAwaiter().GetResult();

        /// <inheritdoc/>
        protected override void RunNonQuery(TikCommandDescriptor descriptor)
            => RunNonQueryAsync(descriptor, CancellationToken.None).GetAwaiter().GetResult();

        /// <inheritdoc/>
        protected override Task<IList<TikRecordSentence>> RunPrintAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
            => ExecuteRequestListAsync(descriptor.CommandText, descriptor.Parameters, cancellationToken);

        /// <inheritdoc/>
        protected override async Task<string> RunAddAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            var single = await ExecuteRequestSingleAsync(descriptor.CommandText, descriptor.Parameters, cancellationToken).ConfigureAwait(false);
            string? id = null;
            single?.TryGetResponseField(TikSpecialProperties.Id, out id);
            // RunAddAsync's declared return type is non-nullable, matching RunAdd's contract across every
            // transport, but a response with no .id field genuinely yields null here (see the report).
            return id!;
        }

        /// <inheritdoc/>
        protected override Task RunNonQueryAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
            => ExecuteRequestAsync(descriptor.CommandText, descriptor.Parameters, cancellationToken);

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
                    // volatileFields is documented as nullable ("null compares all fields") but its parameter
                    // type isn't annotated — ListenLoop lives in Connection/, out of scope here.
                    h => PollingMonitorEngine.ListenLoop(this, printDescriptor, null!, ListenPollIntervalMs, h, onRow, onError, onDone));
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
        TikTrapSentenceResult IPollingMonitorHost.ToTrap(Exception ex) => TikTrapSentenceResult.FromException(ex);

        /// <summary>
        /// Runs a self-terminating monitor once and emits its rows. Deliberately not
        /// <see cref="PollingMonitorEngine.AsyncListOnce"/>: that one strips Filter-format parameters and
        /// evaluates them as a client-side query, and a monitor's parameters are its INPUTS, not a filter over
        /// a table.
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
        private async Task ExecuteRequestAsync(string commandText, IList<ITikCommandParameter> parameters,
            CancellationToken cancellationToken)
        {
            var (body, statusCode) = await SendAndReadAsync(commandText, parameters,
                RestRequestBuilder.RestCallKind.NonQuery, cancellationToken).ConfigureAwait(false);
            // parse errors only; we don't return anything
            ParseErrorOrIgnore(commandText, body, statusCode, parameters);
        }

        private async Task<IList<TikRecordSentence>> ExecuteRequestListAsync(string commandText,
            IList<ITikCommandParameter> parameters, CancellationToken cancellationToken)
        {
            var (body, statusCode) = await SendAndReadAsync(commandText, parameters,
                RestRequestBuilder.RestCallKind.Read, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(body) || body == "null" || body == "[]" || body == "{}")
                return new List<TikRecordSentence>();

            // Try to detect error first
            ParseErrorOrIgnore(commandText, body, statusCode, parameters);

            return ParseResponseList(body);
        }

        private async Task<TikRecordSentence?> ExecuteRequestSingleAsync(string commandText,
            IList<ITikCommandParameter> parameters, CancellationToken cancellationToken)
        {
            var (body, statusCode) = await SendAndReadAsync(commandText, parameters,
                RestRequestBuilder.RestCallKind.Read, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(body) || body == "null" || body == "{}")
                return null;

            ParseErrorOrIgnore(commandText, body, statusCode, parameters);

            return ParseSingleObject(body);
        }

        /// <summary>
        /// Builds the request, sends it and reads the whole body — the single point where this transport touches
        /// the network, so the request/response tracing and the token handling exist once.
        /// </summary>
        private async Task<(string Body, int StatusCode)> SendAndReadAsync(string commandText,
            IList<ITikCommandParameter> parameters, RestRequestBuilder.RestCallKind kind, CancellationToken cancellationToken)
        {
            EnsureOpened();
            cancellationToken.ThrowIfCancellationRequested();

            var req = RestRequestBuilder.Build(commandText, parameters, kind);
            FireWriteRow(req.Method.Method + " " + req.RelativePath);

            var httpResp = await SendHttpAsync(BuildHttpRequest(req), Math.Max(SendTimeout, ReceiveTimeout),
                cancellationToken).ConfigureAwait(false);
            var body = await httpResp.Content.ReadAsStringAsync().ConfigureAwait(false);
            FireReadRow(body);
            return (body, (int)httpResp.StatusCode);
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

        // (SendHttpSync removed in P2.5 — the open probe was its last caller, and nothing on this transport
        // needs a blocking HTTP send any more: the CRUD hooks and the open path all await SendHttpAsync.)
        //
        // timeoutMs is passed in rather than read here because the open probe and the commands after it are
        // bounded by different settings (ConnectTimeout vs Send/ReceiveTimeout), and HttpClient.Timeout can
        // only express one of them. The timeout is therefore a CTS of our own, linked with the caller's token —
        // which is also what lets the catch below say which of the two fired.
        private async Task<HttpResponseMessage> SendHttpAsync(HttpRequestMessage req, int timeoutMs,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage response;
            using (var timeoutCts = new CancellationTokenSource(timeoutMs > 0 ? timeoutMs : System.Threading.Timeout.Infinite))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
            {
                try
                {
                    // HttpCompletionOption.ResponseContentRead (the default) — the body is read inside this
                    // call, so it is covered by the timeout and the token, and the ReadAsStringAsync the caller
                    // does afterwards only reads the buffer these token sources are no longer needed for.
                    // Non-null here: the open probe calls this right after assigning _httpClient, and every
                    // other caller goes through EnsureOpened(), which is only true once that assignment happened.
                    response = await _httpClient!.SendAsync(req, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    // A cancellation nobody asked for is our timeout, and the two must not be conflated:
                    // a caller that cancelled expects OperationCanceledException, while a request that ran out of
                    // time is a configuration problem the caller has to be able to see (see ITikCommandAsync's
                    // remarks). The token tells them apart.
                    throw new TikConnectionReceiveTimeoutException(timeoutMs, ex);
                }
            }

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
            string? message = null;
            string? detail = null;
            // Falling back to the raw body is honest here — the caller still gets an error carrying whatever
            // the router said, nothing is invented — but the catch is narrowed to the parse itself so a bug in
            // this method cannot hide inside it (P2.25), and a body we could not read is traced.
            if (!string.IsNullOrEmpty(body))
            {
                try
                {
                    using (var doc = JsonDocument.Parse(body))
                    {
                        if (doc.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            // ToString() rather than GetString(): a non-string "message" (RouterOS has been
                            // seen answering with a number) must render as itself, not throw out of an error
                            // path whose whole job is to report the error that got us here.
                            if (doc.RootElement.TryGetProperty("message", out var msgEl))
                                message = msgEl.ToString();
                            if (doc.RootElement.TryGetProperty("detail", out var detEl))
                                detail = detEl.ToString();
                        }
                    }
                }
                catch (JsonException ex)
                {
                    if (TikWireTrace.Enabled)
                        TikWireTrace.Emit("rest.http", TikWireDir.Note,
                            $"HTTP {statusCode} body is not JSON ({ex.Message}); reported verbatim");
                }
            }

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

        private static TikRecordSentence? ParseSingleObject(string body)
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
