using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.Rest
{
    /// <summary>
    /// MikroTik RouterOS REST API connection (HTTP/HTTPS).
    /// Requires RouterOS 7.1+ with www or www-ssl service enabled.
    /// </summary>
    public class RestConnection : ITikConnection, ITikConnectionCapabilities
    {
        private readonly bool _useSsl;
        private readonly bool _allowInvalidCert;
        private HttpClient _httpClient;
        private string _baseUrl;
        private string _authHeader;
        private bool _isOpened;

        public bool DebugEnabled { get; set; }
        public bool IsOpened => _isOpened;
        public Encoding Encoding { get; set; } = Encoding.UTF8;
        public bool SendTagWithSyncCommand { get; set; }  // no-op for REST
        public int SendTimeout { get; set; } = 30000;
        public int ReceiveTimeout { get; set; } = 30000;

        public event EventHandler<TikConnectionCommCallbackEventArgs> OnReadRow;
        public event EventHandler<TikConnectionCommCallbackEventArgs> OnWriteRow;

        public TikConnectionCapability Capabilities
            => TikConnectionCapability.Crud;

        public RestConnection(bool useSsl = false, bool allowInvalidCert = true)
        {
            _useSsl = useSsl;
            _allowInvalidCert = allowInvalidCert;
            DebugEnabled = System.Diagnostics.Debugger.IsAttached;
        }

        // ── Open ──────────────────────────────────────────────────────────────

        public void Open(string host, string user, string password)
        {
            int port = _useSsl ? 443 : 80;
            OpenInternal(host, port, user, password);
        }

        public void Open(string host, int port, string user, string password)
        {
            OpenInternal(host, port, user, password);
        }

        public Task OpenAsync(string host, string user, string password)
        {
            return Task.Run(() => Open(host, user, password));
        }

        public Task OpenAsync(string host, int port, string user, string password)
        {
            return Task.Run(() => Open(host, port, user, password));
        }

        private void OpenInternal(string host, int port, string user, string password)
        {
            string scheme = _useSsl ? "https" : "http";
            _baseUrl = $"{scheme}://{host}:{port}/rest";
            _authHeader = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));

            var handler = new HttpClientHandler();
            if (_useSsl && _allowInvalidCert)
                handler.ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => true;

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMilliseconds(Math.Max(SendTimeout, ReceiveTimeout))
            };
            _httpClient.DefaultRequestHeaders.Authorization =
                AuthenticationHeaderValue.Parse(_authHeader);

            // Probe: verify connectivity and credentials
            try
            {
                var probe = SendHttpSync(new HttpRequestMessage(HttpMethod.Get, _baseUrl + "/system/resource"));
                // 401 = wrong credentials, already handled by SendHttpSync → TikConnectionLoginException
                _isOpened = true;
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

        // ── Close ─────────────────────────────────────────────────────────────

        public void Close()
        {
            _isOpened = false;
            _httpClient?.Dispose();
            _httpClient = null;
        }

        public void Dispose() => Close();

        // ── Safe Mode ─────────────────────────────────────────────────────────
        // REST is stateless (each call is an independent HTTP request), so RouterOS cannot keep safe mode
        // bound to "the connection" and the automatic rollback-on-disconnect it depends on does not work.
        // The /safe-mode/* commands execute, but provide no protection — so they are refused here and the
        // SafeMode capability is not reported. Use the binary API or a CLI transport for real safe mode.

        private const string RestSafeModeUnsupported =
            "REST is stateless and cannot bind RouterOS Safe Mode to a connection (no rollback-on-disconnect). " +
            "Use the binary API or a CLI transport (Telnet / MAC-Telnet / WinBox CLI).";

        /// <inheritdoc/>
        public void SafeModeTake() => throw new NotSupportedException(RestSafeModeUnsupported);

        /// <inheritdoc/>
        public void SafeModeRelease() => throw new NotSupportedException(RestSafeModeUnsupported);

        /// <inheritdoc/>
        public void SafeModeUnroll() => throw new NotSupportedException(RestSafeModeUnsupported);

        /// <inheritdoc/>
        public bool SafeModeGet() => false;

        // ── Command factory ───────────────────────────────────────────────────

        public ITikCommand CreateCommand()
            => new RestCommand(this);

        public ITikCommand CreateCommand(TikCommandParameterFormat defaultParameterFormat)
            => new RestCommand(this, defaultParameterFormat);

        public ITikCommand CreateCommand(string commandText, params ITikCommandParameter[] parameters)
            => new RestCommand(this, commandText, parameters);

        public ITikCommand CreateCommand(string commandText, TikCommandParameterFormat defaultParameterFormat, params ITikCommandParameter[] parameters)
            => new RestCommand(this, commandText, defaultParameterFormat, parameters);

        public ITikCommand CreateCommandAndParameters(string commandText, params string[] parameterNamesAndValues)
        {
            var cmd = new RestCommand(this, commandText);
            cmd.AddParameterAndValues(parameterNamesAndValues);
            return cmd;
        }

        public ITikCommand CreateCommandAndParameters(string commandText, TikCommandParameterFormat defaultParameterFormat, params string[] parameterNamesAndValues)
        {
            var cmd = new RestCommand(this, commandText, defaultParameterFormat);
            cmd.AddParameterAndValues(parameterNamesAndValues);
            return cmd;
        }

        public ITikCommandParameter CreateParameter(string name, string value)
            => new RestCommandParameter(name, value);

        public ITikCommandParameter CreateParameter(string name, string value, TikCommandParameterFormat parameterFormat)
            => new RestCommandParameter(name, value, parameterFormat);

        // ── CallCommandSync (low-level) ────────────────────────────────────────

        public IEnumerable<ITikSentence> CallCommandSync(params string[] commandRows)
            => CallCommandSync((IEnumerable<string>)commandRows);

        public IEnumerable<ITikSentence> CallCommandSync(IEnumerable<string> commandRows)
        {
            var rows = commandRows.ToArray();
            if (rows.Length == 0)
                throw new ArgumentException("commandRows must not be empty.");

            string commandText = rows[0];

            // Parse remaining rows as parameters
            var parameters = new List<ITikCommandParameter>();
            for (int i = 1; i < rows.Length; i++)
            {
                string row = rows[i];
                if (row.StartsWith(".tag=") || row.StartsWith(".tag ="))
                    continue;  // tags are ignored in REST

                if (row.StartsWith("?"))
                {
                    // Filter param: ?name=value or ?=name=value
                    string kv = row.TrimStart('?');
                    if (kv.StartsWith("="))
                        kv = kv.Substring(1);
                    int eq = kv.IndexOf('=');
                    if (eq >= 0)
                        parameters.Add(new RestCommandParameter(kv.Substring(0, eq), kv.Substring(eq + 1), TikCommandParameterFormat.Filter));
                }
                else if (row.StartsWith("="))
                {
                    // NameValue param: =name=value
                    string kv = row.Substring(1);
                    int eq = kv.IndexOf('=');
                    if (eq >= 0)
                        parameters.Add(new RestCommandParameter(kv.Substring(0, eq), kv.Substring(eq + 1), TikCommandParameterFormat.NameValue));
                }
            }

            string verb = commandText.TrimStart('/').Split('/').Last().ToLowerInvariant();

            // For add (PUT), return just a single ITikDoneSentence with ret=".id"
            // (matching the binary API's !done =ret=*X response format)
            if (verb == "add")
            {
                var single = ExecuteRequestSingle(commandText, parameters);
                string id = null;
                if (single != null)
                    single.TryGetResponseField(TikSpecialProperties.Id, out id);
                return new List<ITikSentence> { new RestDoneSentence(id) };
            }

            // For remove / non-query verbs, return just !done
            if (verb == "remove" || verb == "set" || verb == "unset" || verb == "move"
                || verb == "enable" || verb == "disable")
            {
                ExecuteRequest(commandText, parameters);
                return new List<ITikSentence> { new RestDoneSentence() };
            }

            // For reads, return list of !re rows + final !done
            var result = new List<ITikSentence>();
            result.AddRange(ExecuteRequestList(commandText, parameters));
            result.Add(new RestDoneSentence());
            return result;
        }

        // ── CallCommandAsync (not supported) ───────────────────────────────────

        public Thread CallCommandAsync(IEnumerable<string> commandRows, string tag, Action<ITikSentence> oneResponseCallback)
        {
            throw new NotSupportedException("REST transport does not support asynchronous commands. Use a transport that reports Listen capability.");
        }

        // ── Internal HTTP execution used by RestCommand ────────────────────────

        internal void ExecuteRequest(string commandText, IList<ITikCommandParameter> parameters)
        {
            EnsureOpened();
            var req = RestRequestBuilder.Build(commandText, parameters);
            FireWriteRow(req.Method.Method + " " + req.RelativePath);

            var httpReq = BuildHttpRequest(req);
            var httpResp = SendHttpSync(httpReq);
            var body = httpResp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            FireReadRow(body);
            // parse errors only; we don't return anything
            ParseErrorOrIgnore(commandText, body, (int)httpResp.StatusCode, parameters);
        }

        internal IList<RestReSentence> ExecuteRequestList(string commandText, IList<ITikCommandParameter> parameters)
        {
            EnsureOpened();
            var req = RestRequestBuilder.Build(commandText, parameters);
            FireWriteRow(req.Method.Method + " " + req.RelativePath);

            var httpReq = BuildHttpRequest(req);
            var httpResp = SendHttpSync(httpReq);
            var body = httpResp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            FireReadRow(body);

            if (string.IsNullOrWhiteSpace(body) || body == "null" || body == "[]" || body == "{}")
                return new List<RestReSentence>();

            // Try to detect error first
            ParseErrorOrIgnore(commandText, body, (int)httpResp.StatusCode, parameters);

            return ParseResponseList(body);
        }

        internal RestReSentence ExecuteRequestSingle(string commandText, IList<ITikCommandParameter> parameters)
        {
            EnsureOpened();
            var req = RestRequestBuilder.Build(commandText, parameters);
            FireWriteRow(req.Method.Method + " " + req.RelativePath);

            var httpReq = BuildHttpRequest(req);
            var httpResp = SendHttpSync(httpReq);
            var body = httpResp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            FireReadRow(body);

            if (string.IsNullOrWhiteSpace(body) || body == "null" || body == "{}")
                return null;

            ParseErrorOrIgnore(commandText, body, (int)httpResp.StatusCode, parameters);

            // Single object response
            return ParseSingleObject(body);
        }

        // ── HTTP helpers ───────────────────────────────────────────────────────

        private HttpRequestMessage BuildHttpRequest(RestRequestBuilder.RestRequest req)
        {
            var httpReq = new HttpRequestMessage(req.Method, _baseUrl + req.RelativePath);
            httpReq.Headers.Authorization = AuthenticationHeaderValue.Parse(_authHeader);

            if (req.JsonBody != null)
            {
                httpReq.Content = new StringContent(req.JsonBody, Encoding.UTF8, "application/json");
            }
            return httpReq;
        }

        private HttpResponseMessage SendHttpSync(HttpRequestMessage req)
        {
            var response = _httpClient.SendAsync(req).GetAwaiter().GetResult();

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                throw new TikConnectionLoginException(new Exception("HTTP 401 Unauthorized — check credentials."));

            return response;
        }

        private void EnsureOpened()
        {
            if (!_isOpened || _httpClient == null)
                throw new TikConnectionNotOpenException("REST connection is not open.");
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

            // Check both the combined message and the detail field independently for known patterns
            string checkText = ((detail ?? "") + " " + (message ?? "") + " " + fullMessage).ToLowerInvariant();

            // Create a synthetic RestCommand to pass to exceptions
            var fakeCmd = new RestCommand(this, commandText, parameters.ToArray());
            var trapSentence = new RestTrapSentence(fullMessage);

            if (checkText.Contains("no such command") || checkText.Contains("no such directory"))
                throw new TikNoSuchCommandException(fakeCmd, trapSentence);
            if (checkText.Contains("no such item") || checkText.Contains("missing or invalid resource identifier") || statusCode == 404)
                throw new TikNoSuchItemException(fakeCmd, trapSentence);
            if (checkText.Contains("already have") || checkText.Contains("item with such name already"))
                throw new TikAlreadyHaveSuchItemException(fakeCmd, trapSentence);

            throw new TikCommandTrapException(fakeCmd, trapSentence);
        }

        private static IList<RestReSentence> ParseResponseList(string body)
        {
            body = body.Trim();

            if (body.StartsWith("["))
            {
                // JSON array
                var result = new List<RestReSentence>();
                using (var doc = JsonDocument.Parse(body))
                {
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        result.Add(ParseJsonObject(el));
                    }
                }
                return result;
            }
            else if (body.StartsWith("{"))
            {
                // Single object returned as list
                var single = ParseSingleObject(body);
                return single != null ? new List<RestReSentence> { single } : new List<RestReSentence>();
            }

            return new List<RestReSentence>();
        }

        private static RestReSentence ParseSingleObject(string body)
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

        private static RestReSentence ParseJsonObject(JsonElement el)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in el.EnumerateObject())
            {
                string val = prop.Value.ValueKind == JsonValueKind.Null
                    ? string.Empty
                    : prop.Value.ToString();
                fields[prop.Name] = val;
            }
            return new RestReSentence(fields);
        }

        // ── Diagnostics ────────────────────────────────────────────────────────

        private void FireWriteRow(string word)
        {
            OnWriteRow?.Invoke(this, new TikConnectionCommCallbackEventArgs(word));
            if (DebugEnabled)
                System.Diagnostics.Debug.WriteLine("REST>> " + word);
        }

        private void FireReadRow(string word)
        {
            OnReadRow?.Invoke(this, new TikConnectionCommCallbackEventArgs(word));
            if (DebugEnabled)
                System.Diagnostics.Debug.WriteLine("REST<< " + (word?.Length > 200 ? word.Substring(0, 200) + "..." : word));
        }
    }
}
