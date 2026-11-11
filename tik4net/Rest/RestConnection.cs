using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Connection;

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
    /// plus the request build (<see cref="RestRequestBuilder"/>) and reading the answer (<see cref="RestResponseParser"/>).
    /// <para>Capability is <see cref="TikConnectionCapability.Crud"/> | <see cref="TikConnectionCapability.Listen"/> |
    /// <see cref="TikConnectionCapability.AsyncCommands"/> | <see cref="TikConnectionCapability.CancelInFlight"/>.
    /// Listen and the callback monitors are emulated by polling, exactly as the CLI and native-WinBox
    /// transports do it (<see cref="PollingMonitorEngine"/>) — see <c>RunMonitorAsync</c> for what the
    /// router does and does not offer here. There is no <see cref="TikConnectionCapability.Streaming"/> and no
    /// Safe Mode: each call is an independent HTTP request, so RouterOS cannot bind safe mode's
    /// rollback-on-disconnect to "the connection". REST has no Safe Mode members at all (<see cref="ITikRestConnection"/>
    /// carries none), so calling one on a REST connection is a compile error.</para>
    /// <para>
    /// The Task-based command surface (<see cref="ITikCommandAsync"/>, reached through the <c>Execute*Async</c>
    /// extension methods) is <b>native</b> here rather than emulated: <c>HttpClient</c> is async to the bottom, so
    /// the async hooks are the real implementation and the synchronous ones block on them. That independence of
    /// each request is also what makes REST the cleanest cancellation story of all eleven transports — a cancelled
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
    /// A value of 0 or less means no bound of its own: the probe is then bounded by
    /// <see cref="ITikConnection.ReceiveTimeout"/>, as every transport's handshake is.
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
    /// <para>
    /// <b>Thread safety.</b> One open connection may be used from several threads, and commands overlap
    /// with nothing to configure: each one is an independent HTTP request on the shared
    /// <c>HttpClient</c>, so there is no channel to serialize. It never takes
    /// <see cref="TikCommandConnectionBase"/>'s command semaphore; up to eight requests are on the wire at once,
    /// and a ninth waits for a slot within its own timeout.
    /// </para>
    /// <para>
    /// That cap exists because <b>RouterOS answers only the first of several requests queued on one HTTP
    /// connection</b> (<c>Docs/findings-rest-api.md</c> §1). On .NET Framework, where <c>HttpClientHandler</c>
    /// runs on <c>HttpWebRequest</c>, a request beyond the per-host connection limit is pipelined onto a busy
    /// connection, so this transport raises that limit for the router's address above the cap when it opens.
    /// </para>
    /// <para>
    /// <b>The limit here is the router, not the client.</b> RouterOS keeps a REST session busy for as
    /// long as a command runs and buffers the whole response, so a long one blocks the requests behind
    /// it: measured on 7.23.2, requests issued after a <c>count=30</c> ping was abandoned at 5 s timed
    /// out for the remaining ~23 s the router took to finish it (<c>Docs/findings-rest-api.md</c> §12.1).
    /// Ordinary commands are short and overlap fine; keep long monitors off a connection that also
    /// carries them. The rest of the contract — <c>Close</c> not waiting for a running command, and the
    /// router's shared throughput ceiling — is on <see cref="ITikConnection"/>.
    /// </para>
    /// </remarks>
    public sealed class RestConnection : TikCommandConnectionBase, ITikRestConnection,
        ITikMonitorTransport, IPollingMonitorHost, ITikMenuSchemaConnection
    {
        // How often a continuous monitor re-reads the router: every 1000 ms here, every 500 ms on the CLI
        // (CliConnectionBase). A listen polls at PollingMonitorEngine.ListenPollIntervalMs on every transport.
        private const int MonitorPollIntervalMs = 1000;

        private readonly bool _useSsl;
        // _baseUrl: assigned in OpenInternalAsync (Open/OpenAsync), not the constructor — nothing
        // reads it before Open. _httpClient is genuinely nullable: Close() and a failed Open reset it to null.
        private HttpClient? _httpClient;
        private string _baseUrl = null!;

        private const string ConsoleInspectPath = "/console/inspect";

        // What the router said a verb-less path's last segment is. Forgotten on Open, as MenuSchemas is.
        private readonly ConcurrentDictionary<string, RestTrailingSegment> _trailingSegments
            = new ConcurrentDictionary<string, RestTrailingSegment>(StringComparer.OrdinalIgnoreCase);

        // At most this many requests are on the wire at once. RouterOS answers only the first of several requests
        // queued on one HTTP connection (Docs/findings-rest-api.md §1), so a request must never wait on a busy
        // connection: on .NET Framework HttpWebRequest allows two connections per host and pipelines everything
        // beyond that. OpenInternalAsync raises that limit above this cap, and this gate keeps the requests below
        // it. It also stays under the www service's default max-sessions (20).
        private const int MaxConcurrentRequests = 8;
        private readonly SemaphoreSlim _requestGate = new SemaphoreSlim(MaxConcurrentRequests, MaxConcurrentRequests);

        /// <summary>
        /// CRUD, <see cref="TikConnectionCapability.Listen"/> (polled — see <c>RunMonitorAsync</c>),
        /// <see cref="TikConnectionCapability.AsyncCommands"/> and <see cref="TikConnectionCapability.CancelInFlight"/>.
        /// No <see cref="TikConnectionCapability.Streaming"/> and no Safe Mode.
        /// </summary>
        public override TikConnectionCapability Capabilities =>
            TikConnectionCapability.Crud | TikConnectionCapability.Listen
            | TikConnectionCapability.AsyncCommands | TikConnectionCapability.CancelInFlight
            | TikConnectionCapability.MenuSchema;

        bool ITikMenuSchemaConnection.ValidateWrites { get => ValidateWrites; set => ValidateWrites = value; }

        TikMenuSchema ITikMenuSchemaConnection.DescribeMenu(string path, string? winboxLabels)
            => MenuSchemas.GetOrAdd(path, p => ConsoleInspectSchemaReader.Read(this, p));

        /// <inheritdoc/>
        /// <remarks>
        /// Applies to HTTPS only (<see cref="TikConnectionType.RestSsl"/>); on plain HTTP there is no
        /// certificate to judge. Shares its delegate shape with <see cref="tik4net.Api.ApiConnection"/>'s
        /// API-SSL validation, so one callback set on
        /// <see cref="TikConnectionSetup.CertificateValidationCallback"/> drives both transports.
        /// </remarks>
        public bool AllowInvalidCertificate { get; set; }

        /// <inheritdoc/>
        /// <remarks>Applies to HTTPS only (<see cref="TikConnectionType.RestSsl"/>), on every connection the client
        /// opens to the router.</remarks>
        public string? CertificateFingerprint { get; set; }

        /// <inheritdoc/>
        public RemoteCertificateValidationCallback? CertificateValidationCallback { get; set; }

        // The fingerprint of a certificate the pin refused, so the open probe reports the mismatch rather than
        // HttpClient's generic TLS failure.
        private string? _pinRefusedFingerprint;

        /// <summary>Creates a REST connection.</summary>
        /// <param name="useSsl">Use HTTPS (port 443) instead of HTTP (port 80).</param>
        // Only constructible via TikConnectionSetup/ConnectionFactory (same assembly).
        internal RestConnection(bool useSsl = false) : base(useSsl ? TikConnectionType.RestSsl : TikConnectionType.Rest)
        {
            _useSsl = useSsl;
        }

        // ── Open / Close ──────────────────────────────────────────────────────

        // As with the CRUD hooks below, the async form is the implementation and the synchronous one blocks
        // on it. A `Task.Run(() => Open(...))` façade would hold a thread-pool thread for the whole
        // connectivity/credential probe, an HTTP round trip SendHttpAsync awaits — exactly what the
        // AsyncCommands capability exists to rule out.

        /// <inheritdoc/>
        public override void Open(string host, int port, string user, string password)
            => OpenInternalAsync(host, port, user, password, CancellationToken.None)
                .GetAwaiter().GetResult();

        /// <inheritdoc/>
        public override Task OpenAsync(string host, int port, string user, string password,
            CancellationToken cancellationToken = default)
            => OpenInternalAsync(host, port, user, password, cancellationToken);

        private async Task OpenInternalAsync(string host, int port, string user, string password,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string scheme = _useSsl ? "https" : "http";
            _baseUrl = $"{scheme}://{host}:{port}/rest";
            _trailingSegments.Clear();

#if NETSTANDARD2_0
            // On .NET Framework HttpClientHandler runs on HttpWebRequest, whose per-host limit (2 by default) is what
            // decides when a request is pipelined onto a busy connection. Twice the request cap leaves a spare
            // connection for a request whose predecessor has not yet handed its connection back. Nothing reads the
            // limit on .NET Core, and net8.0 builds SocketsHttpHandler, which never pipelines.
            try
            {
                var servicePoint = System.Net.ServicePointManager.FindServicePoint(new Uri(_baseUrl));
                if (servicePoint.ConnectionLimit < 2 * MaxConcurrentRequests)
                    servicePoint.ConnectionLimit = 2 * MaxConcurrentRequests;
            }
            catch (PlatformNotSupportedException)
            {
                // A runtime without ServicePointManager has no HttpWebRequest underneath either.
            }
#endif

            var handler = new HttpClientHandler();
            if (_useSsl)
            {
                _pinRefusedFingerprint = null;
                var validate = TikTlsValidation.For(this, presented => _pinRefusedFingerprint = presented);
                if (validate != null)
                    handler.ServerCertificateCustomValidationCallback =
                        (request, cert, chain, errors) => validate(request, cert, chain, errors);
                // else: leave unset — HttpClientHandler performs standard OS chain/hostname validation
            }

            // Infinite here, and bounded per request in SendHttpAsync instead: HttpClient.Timeout is one value
            // for the client's whole lifetime, and the open probe and the commands after it are bounded by
            // different settings (ConnectTimeout vs Send/ReceiveTimeout). Leaving it set would make the
            // effective bound the smaller of the two, which is neither of the numbers the caller configured.
            _httpClient = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));

            // Probe: verify connectivity, credentials and that this is the REST API. Bounded even with ConnectTimeout <= 0 (TikTimeouts.HandshakeBoundMs).
            int probeMs = TikTimeouts.HandshakeBoundMs(ConnectTimeoutMs, ReceiveTimeoutMs);
            bool opened = false;
            try
            {
                // 401 = wrong credentials, already handled by SendHttpAsync → TikConnectionLoginException
                var (body, statusCode) = await SendHttpAsync(
                    new RestRequestBuilder.RestRequest(HttpMethod.Get, "/system/resource"), probeMs, cancellationToken).ConfigureAwait(false);
                // Any JSON answer past the 401 is the REST API with the login accepted, also an error: a user without
                // 'read' gets 500 "not allowed" here (7.25), and may well have rights to what it came for.
                if (!RestResponseParser.IsJsonDocument(body))
                    throw NotTheRestApi(port, statusCode, body);
                SetOpened();
                opened = true;
            }
            catch (TikConnectionReceiveTimeoutException ex)
            {
                // The router took the connection and answered the probe with silence: a login with no answer, as on
                // every transport (TikOpenGuard). The inner exception's "no response received" wording describes a read
                // on an established connection, not an open that never got that far.
                throw new TikConnectionLoginNoAnswerException("REST",
                    $"the open probe GET /rest/system/resource to {host}:{port}, nothing within {probeMs} ms "
                    + "(ConnectTimeout, or ReceiveTimeout when ConnectTimeout is not positive)", ex);
            }
            catch (OperationCanceledException)
            {
                // The caller's own cancel, and it leaves as itself. SendHttpAsync has already separated this from a
                // timeout — a cancellation nobody asked for becomes TikConnectionReceiveTimeoutException there.
                throw;
            }
            catch (Exception ex)
            {
                // Nothing answered (refused, unreachable, the connect timed out): the SocketException under HttpClient's
                // own exception, as the API and the terminals throw it. Anything else is what TikOpenGuard makes of it.
                for (Exception? inner = ex; inner != null; inner = inner.InnerException)
                    if (inner is System.Net.Sockets.SocketException socket)
                        throw socket;
                if (_pinRefusedFingerprint is string presented)
                    throw new TikConnectionHostKeyMismatchException("REST-SSL",
                        TikTlsValidation.PinnedFingerprint(this)!, presented, ex);
                throw TikOpenGuard.Translate(ex);
            }
            finally
            {
                if (!opened)
                {
                    _httpClient?.Dispose();
                    _httpClient = null;
                }
            }
        }

        private TikConnectionProtocolMismatchException NotTheRestApi(int port, int statusCode, string body)
        {
            // RouterOS gained /rest in 7.1; before that the www service serves webfig only, and every REST call lands on
            // the 404 page (measured on 6.49.21).
            string message = statusCode == 404 && RestResponseParser.IsHtml(body)
                ? "The router answered the REST probe with an HTML 404 page rather than JSON, so this RouterOS has no REST "
                  + "API: it was added in 7.1, and before that the www service serves webfig only. Use the binary API or a CLI transport on this router."
                : $"The REST probe GET /rest/system/resource on port {port} was answered with HTTP {statusCode} and a body that "
                  + "is not JSON, so what answers there is not the RouterOS REST API. Check the port, and that /ip/service has "
                  + (_useSsl ? "'www-ssl'" : "'www'") + " on it.";
            return new TikConnectionProtocolMismatchException(port, _useSsl, message);
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
        // synchronous ones block on them (async is the primitive, and nothing is pushed onto a thread-pool
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
        protected override async Task<IList<TikRecordSentence>> RunPrintAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            string body = await ExecuteReadAsync(descriptor, cancellationToken).ConfigureAwait(false);
            if (!RestResponseParser.TryReadRows(body, out var rows))
                throw new TikCommandUnexpectedResponseException("The router's answer is not rows: " + body,
                    CommandFor(descriptor), new TikTrapSentenceResult(body));
            // The router honours ?.proplist= but adds .id to every row whether listed or not (7.24.2); the base trims
            // the rows to .proplist (TikProplist), as the binary API returns them.
            return TikGetResult.Shape(descriptor, rows, rowAddressedByRouter: true);
        }

        /// <inheritdoc/>
        protected override async Task<string> RunAddAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            string body = await ExecuteReadAsync(descriptor, cancellationToken).ConfigureAwait(false);
            string? id = null;
            bool readable = RestResponseParser.TryReadRows(body, out var rows);
            if (readable && rows.Count > 0)
                rows[0].TryGetResponseField(TikSpecialProperties.Id, out id);
            if (id == null || id.Length == 0)
                throw TikAddIdNotRead.Create(CommandFor(descriptor), readable && rows.Count == 0 ? null : body);
            return id;
        }

        /// <inheritdoc/>
        protected override Task RunNonQueryAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
            => ExecuteAsync(descriptor, RestTrailingSegment.Command, cancellationToken);

        // ── Monitor / async / listen (ITikMonitorTransport) ────────────────────

        /// <summary>
        /// Dispatches a callback-based async command (<c>ExecuteWithCallback</c>/<c>LoadWithCallback</c>/<c>LoadListenWithCallback</c>)
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

            var table = PollingMonitorEngine.TryStartTableShape("rest", this, descriptor, verb, null, onRow, onError, onDone);
            if (table != null) return table;

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
        bool IPollingMonitorHost.ClosedByCaller => ClosedByCaller;

        /// <inheritdoc/>
        IList<TikRecordSentence> IPollingMonitorHost.PollSnapshot(TikCommandDescriptor printDescriptor)
            => TikProplist.Apply(printDescriptor, RunPrint(printDescriptor));   // not through InvokeRunPrint

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
            => PollingMonitorEngine.Run(this, handle, onError, onDone, () => PollingMonitorEngine.Emit(TikProplist.Apply(descriptor, RunPrint(descriptor)), handle, onRow));

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
                if (string.Equals(TikParameterFormat.BareName(p.Name), name, StringComparison.OrdinalIgnoreCase))
                    return descriptor;

            var parameters = new List<ITikCommandParameter>(descriptor.Parameters)
            {
                new TikCommandParameter(name, value, TikCommandParameterFormat.NameValue),
            };
            return new TikCommandDescriptor(descriptor.CommandText, parameters);
        }

        // ── HTTP execution ─────────────────────────────────────────────────────

        // A command that expects rows asks the router what a verb-less path is.
        private async Task<string> ExecuteReadAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            EnsureOpened();
            var trailingSegment = await TrailingSegmentAsync(descriptor.CommandText, cancellationToken).ConfigureAwait(false);
            return await ExecuteAsync(descriptor, trailingSegment, cancellationToken).ConfigureAwait(false);
        }

        // A command that expects none is an action whatever its last segment says (/log/error and friends).
        private async Task<string> ExecuteAsync(TikCommandDescriptor descriptor, RestTrailingSegment trailingSegment,
            CancellationToken cancellationToken)
        {
            var (body, statusCode) = await SendAndReadAsync(
                RestRequestBuilder.Build(descriptor.CommandText, descriptor.Parameters, trailingSegment), cancellationToken).ConfigureAwait(false);
            if (statusCode < 200 || statusCode >= 300)
                throw RestResponseParser.ToException(CommandFor(descriptor), statusCode, body);
            return body;
        }

        /// <summary>
        /// Whether a path's last segment is a menu or a command, from <c>/console/inspect</c>: <c>node-type</c> of the
        /// node itself is <c>cmd</c>, <c>dir</c> or <c>path</c>. Asked once per path and connection. What it lists nothing
        /// for is POSTed as given: <c>/ping</c> answers although inspect does not know it, and a path that does not exist
        /// gets the same 400 from POST as from GET. See <c>Docs/findings-rest-api.md</c> §10.
        /// </summary>
        private async Task<RestTrailingSegment> TrailingSegmentAsync(string commandText, CancellationToken cancellationToken)
        {
            string path = TikPath.Normalize(commandText);
            if (RestRequestBuilder.HasOwnShape(path) || string.Equals(path, ConsoleInspectPath, StringComparison.OrdinalIgnoreCase))
                return RestTrailingSegment.Command;
            if (_trailingSegments.TryGetValue(path, out var known))
                return known;

            var parameters = new List<ITikCommandParameter>
            {
                new TikCommandParameter("request", "child", TikCommandParameterFormat.NameValue),
                new TikCommandParameter("path", path.Trim('/').Replace('/', ','), TikCommandParameterFormat.NameValue),
            };
            RestTrailingSegment answer;
            try
            {
                string body = await ExecuteAsync(new TikCommandDescriptor(ConsoleInspectPath, parameters),
                    RestTrailingSegment.Command, cancellationToken).ConfigureAwait(false);
                answer = RestResponseParser.TryReadRows(body, out var rows) && rows.Any(IsMenuNode)
                    ? RestTrailingSegment.Menu : RestTrailingSegment.Command;
            }
            catch (TikCommandException)
            {
                // The router will not say (a user without 'read' gets 500 "not allowed"). The monitor verbs are the
                // commands a read reaches for by name; anything else is read as the menu it most likely is.
                answer = TikMonitorVerbs.Contains(TikPath.Verb(path)) ? RestTrailingSegment.Command : RestTrailingSegment.Menu;
            }
            _trailingSegments[path] = answer;
            return answer;
        }

        private static bool IsMenuNode(TikRecordSentence row)
        {
            if (row.GetResponseFieldOrDefault("type", null) != "self")
                return false;
            string? nodeType = row.GetResponseFieldOrDefault("node-type", null);
            return nodeType == "dir" || nodeType == "path";
        }

        /// <summary>
        /// Sends the request and reads the whole body with the tracing around it: every command's single point of
        /// contact with the network.
        /// </summary>
        private async Task<(string Body, int StatusCode)> SendAndReadAsync(RestRequestBuilder.RestRequest request, CancellationToken cancellationToken)
        {
            EnsureOpened();
            cancellationToken.ThrowIfCancellationRequested();

            if (RowTracingEnabled)
                FireWriteRow(request.Method.Method + " " + request.RelativePath + (request.JsonBody == null ? "" : " " + request.JsonBody));

            var answer = await SendHttpAsync(request, Math.Max(SendTimeoutMs, ReceiveTimeoutMs), cancellationToken).ConfigureAwait(false);
            FireReadRow(answer.Body);
            return answer;
        }

        // ── HTTP helpers ───────────────────────────────────────────────────────

        private HttpRequestMessage BuildHttpRequest(RestRequestBuilder.RestRequest req)
        {
            var httpReq = new HttpRequestMessage(req.Method, _baseUrl + req.RelativePath);
            if (req.JsonBody != null)
            {
                httpReq.Content = new StringContent(req.JsonBody, Encoding);
                // Not the constructor's overload, which adds a charset: some ROS versions match the media type exactly (findings §1).
                httpReq.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }
            return httpReq;
        }

        // timeoutMs is passed in rather than read here because the open probe and the commands after it are
        // bounded by different settings (ConnectTimeout vs Send/ReceiveTimeout), and HttpClient.Timeout can
        // only express one of them. The timeout is therefore a CTS of our own, linked with the caller's token —
        // which is also what lets the catch below say which of the two fired.
        private async Task<(string Body, int StatusCode)> SendHttpAsync(RestRequestBuilder.RestRequest request, int timeoutMs,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage response;
            using (var req = BuildHttpRequest(request))
            using (var timeoutCts = new CancellationTokenSource(timeoutMs > 0 ? timeoutMs : System.Threading.Timeout.Infinite))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
            {
                bool gateEntered = false;
                try
                {
                    // Inside the try, so a request that times out or is cancelled while waiting for a slot reports
                    // exactly as one that ran out of time on the wire.
                    await _requestGate.WaitAsync(linked.Token).ConfigureAwait(false);
                    gateEntered = true;
                    // HttpCompletionOption.ResponseContentRead (the default) — the body is read inside this
                    // call, so it is covered by the timeout and the token, and the ReadAsStringAsync the caller
                    // does afterwards only reads the buffer these token sources are no longer needed for.
                    // Non-null here: the open probe calls this right after assigning _httpClient, and every
                    // other caller goes through EnsureOpened(), which is only true once that assignment happened.
                    // Snapshotted, not read through the field: Close() nulls _httpClient, and a send that
                    // read the field a moment later would fail with a NullReferenceException instead of
                    // anything meaningful. Holding the reference also means the dispose below races the send
                    // rather than the field read, which is the failure we can actually translate.
                    HttpClient client = _httpClient
                        ?? throw ClosedWhileRunning(new ObjectDisposedException(nameof(HttpClient)));
                    response = await client.SendAsync(req, linked.Token).ConfigureAwait(false);
                }
                catch (ObjectDisposedException ex) when (!IsOpened)
                {
                    // Close() disposed the client while this request was on the wire.
                    throw ClosedWhileRunning(ex);
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    // A cancellation nobody asked for is our timeout, and the two must not be conflated:
                    // a caller that cancelled expects OperationCanceledException, while a request that ran out of
                    // time is a configuration problem the caller has to be able to see (see ITikCommandAsync's
                    // remarks). The token tells them apart.
                    throw new TikConnectionReceiveTimeoutException(TimeSpan.FromMilliseconds(timeoutMs), ex);
                }
                finally
                {
                    // ResponseContentRead: the body is already buffered, so the connection is free again here.
                    if (gateEntered)
                        _requestGate.Release();
                }
            }

            using (response)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    throw new TikConnectionLoginException(new Exception("HTTP 401 Unauthorized — check credentials."));

                // By the connection's Encoding, not a charset (RouterOS sends none): RouterOS holds bytes as they arrived.
                byte[] body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                return (Encoding.GetString(body), (int)response.StatusCode);
            }
        }
    }
}
