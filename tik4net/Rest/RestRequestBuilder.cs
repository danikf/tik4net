using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace tik4net.Rest
{
    /// <summary>
    /// Translates a MikroTik API command (path + parameters) into an HTTP request for the RouterOS REST API.
    /// Pure logic — no network dependency, unit-testable standalone.
    /// </summary>
    internal static class RestRequestBuilder
    {
        // Verbs recognised as the trailing segment of a MikroTik API command path.
        private static readonly HashSet<string> _writeVerbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "add", "set", "remove", "unset", "move",
            "enable", "disable", "comment",
            "cancel", "shutdown", "reboot", "poweroff",
            "flush", "reset", "reset-counters",
            "export", "reload", "run", "check", "test",
            "monitor", "start", "stop", "install", "upgrade",
        };

        private static readonly HashSet<string> _readVerbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "print", "listen",
        };

        internal sealed class RestRequest
        {
            public HttpMethod Method { get; }
            public string RelativePath { get; }   // e.g. /rest/ip/address
            public string JsonBody { get; }       // null for GET/DELETE

            internal RestRequest(HttpMethod method, string relativePath, string jsonBody = null)
            {
                Method = method;
                RelativePath = relativePath;
                JsonBody = jsonBody;
            }
        }

        /// <summary>
        /// Builds a <see cref="RestRequest"/> from a MikroTik API command.
        /// </summary>
        /// <param name="commandText">API command path, e.g. /ip/address/print</param>
        /// <param name="parameters">Command parameters.</param>
        public static RestRequest Build(string commandText, IList<ITikCommandParameter> parameters)
        {
            // Normalise: ensure leading slash, no trailing slash
            commandText = commandText.Trim();
            if (!commandText.StartsWith("/"))
                commandText = "/" + commandText;

            string[] segments = commandText.TrimStart('/').Split('/');
            string lastSeg = segments.Last();

            string verb;
            string apiPath;

            if (_readVerbs.Contains(lastSeg) || _writeVerbs.Contains(lastSeg))
            {
                verb = lastSeg.ToLowerInvariant();
                apiPath = "/" + string.Join("/", segments.Take(segments.Length - 1));
            }
            else
            {
                // No explicit verb → implicit print (read)
                verb = "print";
                apiPath = "/" + string.Join("/", segments);
            }

            // restBase is relative to the /rest base URL (e.g. "/interface", not "/rest/interface")
            string restBase = apiPath;

            switch (verb)
            {
                case "print":
                    return BuildPrint(restBase, parameters);

                case "add":
                    return BuildAdd(restBase, parameters);

                case "set":
                    return BuildSet(restBase, parameters);

                case "remove":
                    return BuildRemove(restBase, parameters);

                case "unset":
                    return BuildUnset(restBase, parameters);

                default:
                    // Generic command — POST to /rest/path/verb
                    return BuildGenericPost(restBase + "/" + verb, parameters);
            }
        }

        // ── READ ─────────────────────────────────────────────────────────────

        private static RestRequest BuildPrint(string restBase, IList<ITikCommandParameter> parameters)
        {
            var filterParams = parameters
                .Where(p => IsFilterParam(p))
                .Where(p => !IsSpecialParam(p.Name))
                .ToList();

            var proplist = parameters
                .FirstOrDefault(p => p.Name == TikSpecialProperties.Proplist);

            // NameValue params that are NOT special: command input params (e.g. /tool/wol mac=...)
            var dataParams = parameters
                .Where(p => !IsFilterParam(p) && !IsSpecialParam(p.Name))
                .Where(p => p.ParameterFormat == TikCommandParameterFormat.NameValue
                            || p.Name.StartsWith("="))
                .Select(p => new { Name = NormaliseParamName(p.Name), p.Value })
                .ToList();

            bool hasFilters  = filterParams.Count > 0;
            bool hasProplist = proplist != null;
            bool hasData     = dataParams.Count > 0;

            // Neither filters nor data → GET (with optional ?proplist= query param)
            if (!hasFilters && !hasData)
            {
                if (!hasProplist)
                    return new RestRequest(HttpMethod.Get, restBase);

                // GET /rest/path?.proplist=a,b,c
                string query = "?.proplist=" + Uri.EscapeDataString(proplist.Value);
                return new RestRequest(HttpMethod.Get, restBase + query);
            }

            // Has filters or data params → POST /rest/path/print with body
            var bodyObj = new Dictionary<string, object>();

            if (filterParams.Count > 0)
            {
                var queries = filterParams
                    .Select(p => NormaliseParamName(p.Name) + "=" + p.Value)
                    .ToList();
                bodyObj[".query"] = queries;
            }

            if (proplist != null)
            {
                var fields = proplist.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                bodyObj[".proplist"] = fields;
            }

            // Command input params go as regular JSON fields alongside filters
            foreach (var dp in dataParams)
                bodyObj[dp.Name] = dp.Value ?? string.Empty;

            return new RestRequest(HttpMethod.Post, restBase + "/print", SerialiseBody(bodyObj));
        }

        // ── WRITE ─────────────────────────────────────────────────────────────

        private static RestRequest BuildAdd(string restBase, IList<ITikCommandParameter> parameters)
        {
            var body = BuildNameValueBody(parameters, exclude: null);
            return new RestRequest(HttpMethod.Put, restBase, SerialiseBody(body));
        }

        private static RestRequest BuildSet(string restBase, IList<ITikCommandParameter> parameters)
        {
            string id = GetIdFromParams(parameters);

            var body = BuildNameValueBody(parameters, exclude: TikSpecialProperties.Id);

            if (!string.IsNullOrEmpty(id))
            {
                // PATCH /rest/path/{id}
                return new RestRequest(new HttpMethod("PATCH"), restBase + "/" + id, SerialiseBody(body));
            }
            else
            {
                // Singleton — PATCH /rest/path (no id in path)
                return new RestRequest(new HttpMethod("PATCH"), restBase, SerialiseBody(body));
            }
        }

        private static RestRequest BuildRemove(string restBase, IList<ITikCommandParameter> parameters)
        {
            string id = GetIdFromParams(parameters);
            if (string.IsNullOrEmpty(id))
                throw new InvalidOperationException("REST remove requires .id parameter.");

            return new RestRequest(HttpMethod.Delete, restBase + "/" + id);
        }

        private static RestRequest BuildUnset(string restBase, IList<ITikCommandParameter> parameters)
        {
            string id = GetIdFromParams(parameters);
            if (string.IsNullOrEmpty(id))
                throw new InvalidOperationException("REST unset requires .id parameter.");

            string fieldName = parameters
                .FirstOrDefault(p => string.Equals(p.Name, TikSpecialProperties.UnsetValueName, StringComparison.OrdinalIgnoreCase))
                ?.Value;

            if (string.IsNullOrEmpty(fieldName))
                throw new InvalidOperationException("REST unset requires value-name parameter.");

            // PATCH /rest/path/{id} with {fieldName: null}
            // System.Text.Json: we need to write null explicitly
            var body = $"{{\"{fieldName}\":null}}";
            return new RestRequest(new HttpMethod("PATCH"), restBase + "/" + id, body);
        }

        private static RestRequest BuildGenericPost(string url, IList<ITikCommandParameter> parameters)
        {
            var body = BuildNameValueBody(parameters, exclude: null);
            string json = body.Count > 0 ? SerialiseBody(body) : null;
            return new RestRequest(HttpMethod.Post, url, json);
        }

        // ── HELPERS ───────────────────────────────────────────────────────────

        private static bool IsFilterParam(ITikCommandParameter p)
        {
            if (p.Name.StartsWith("?"))
                return true;
            return p.ParameterFormat == TikCommandParameterFormat.Filter;
        }

        private static bool IsSpecialParam(string name)
        {
            // .tag, .proplist are handled separately
            // "detail" is a no-op in REST (full details are returned by default)
            return name == TikSpecialProperties.Tag
                || name == TikSpecialProperties.Proplist
                || string.Equals(name, "detail", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormaliseParamName(string name)
        {
            // Strip leading ? or ?= to get bare field name
            if (name.StartsWith("?="))
                return name.Substring(2);
            if (name.StartsWith("?"))
                return name.Substring(1);
            return name;
        }

        private static string GetIdFromParams(IList<ITikCommandParameter> parameters)
        {
            return parameters
                .FirstOrDefault(p => string.Equals(NormaliseParamName(p.Name), TikSpecialProperties.Id, StringComparison.OrdinalIgnoreCase))
                ?.Value;
        }

        /// <summary>
        /// Builds the JSON body dict from NameValue parameters, excluding special ones.
        /// </summary>
        private static Dictionary<string, string> BuildNameValueBody(IList<ITikCommandParameter> parameters, string exclude)
        {
            var body = new Dictionary<string, string>();
            foreach (var p in parameters)
            {
                if (IsFilterParam(p))
                    continue;  // filter params don't go in body

                string name = NormaliseParamName(p.Name);

                if (name == TikSpecialProperties.Tag
                    || name == TikSpecialProperties.Proplist)
                    continue;

                if (!string.IsNullOrEmpty(exclude)
                    && string.Equals(name, exclude, StringComparison.OrdinalIgnoreCase))
                    continue;

                body[name] = p.Value ?? string.Empty;
            }
            return body;
        }

        private static string SerialiseBody(Dictionary<string, string> body)
        {
            using (var ms = new System.IO.MemoryStream())
            using (var writer = new Utf8JsonWriter(ms))
            {
                writer.WriteStartObject();
                foreach (var kv in body)
                {
                    writer.WriteString(kv.Key, kv.Value);
                }
                writer.WriteEndObject();
                writer.Flush();
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static string SerialiseBody(Dictionary<string, object> body)
        {
            using (var ms = new System.IO.MemoryStream())
            using (var writer = new Utf8JsonWriter(ms))
            {
                WriteObjectValue(writer, body);
                writer.Flush();
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static void WriteObjectValue(Utf8JsonWriter writer, Dictionary<string, object> obj)
        {
            writer.WriteStartObject();
            foreach (var kv in obj)
            {
                writer.WritePropertyName(kv.Key);
                if (kv.Value is IEnumerable<string> strList)
                {
                    writer.WriteStartArray();
                    foreach (var s in strList)
                        writer.WriteStringValue(s);
                    writer.WriteEndArray();
                }
                else if (kv.Value is string s2)
                {
                    writer.WriteStringValue(s2);
                }
                else if (kv.Value == null)
                {
                    writer.WriteNullValue();
                }
                else
                {
                    writer.WriteStringValue(kv.Value.ToString());
                }
            }
            writer.WriteEndObject();
        }
    }
}
