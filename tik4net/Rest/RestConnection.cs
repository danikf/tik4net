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
    /// <para>Capability is <see cref="TikConnectionCapability.Crud"/> only — REST is stateless, so there is no
    /// Listen/Streaming and no Safe Mode (each call is an independent HTTP request, so RouterOS cannot bind
    /// safe mode's rollback-on-disconnect to "the connection"; the inherited <c>SafeMode*</c> methods throw).</para>
    /// </remarks>
    public class RestConnection : TikCommandConnectionBase
    {
        private readonly bool _useSsl;
        private readonly bool _allowInvalidCert;
        private readonly RemoteCertificateValidationCallback _certificateValidationCallback;
        private HttpClient _httpClient;
        private string _baseUrl;
        private string _authHeader;

        /// <inheritdoc/>
        protected override string DiagnosticPrefix => "REST";

        /// <summary>REST supports only CRUD (no Listen/Streaming/SafeMode).</summary>
        public override TikConnectionCapability Capabilities => TikConnectionCapability.Crud;

        /// <summary>Creates a REST connection.</summary>
        /// <param name="useSsl">Use HTTPS (port 443) instead of HTTP (port 80).</param>
        /// <param name="allowInvalidCert">When <paramref name="useSsl"/>, accept self-signed/invalid certificates. Ignored when <paramref name="certificateValidationCallback"/> is set.</param>
        /// <param name="certificateValidationCallback">
        /// Optional custom certificate validation for HTTPS. When set, it takes full control and
        /// <paramref name="allowInvalidCert"/> is ignored. Shares its delegate shape with
        /// <see cref="tik4net.Api.ApiConnection"/>'s API-SSL validation, so the same callback can drive both
        /// transports via <see cref="TikConnectionSetup.CertificateValidationCallback"/>.
        /// </param>
        public RestConnection(bool useSsl = false, bool allowInvalidCert = true,
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

        // ── HTTP execution ─────────────────────────────────────────────────────

        private void ExecuteRequest(string commandText, IList<ITikCommandParameter> parameters)
        {
            EnsureOpened();
            var req = RestRequestBuilder.Build(commandText, parameters);
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
