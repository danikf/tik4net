using System;
using System.Collections.Generic;
using System.Text.Json;
using tik4net.Connection;
using tik4net.Diagnostics;

namespace tik4net.Rest
{
    /// <summary>
    /// Reads what RouterOS answered to a REST request: the rows of a successful one and the exception of a failed one.
    /// Pure logic, no network dependency.
    /// </summary>
    internal static class RestResponseParser
    {
        /// <summary>
        /// The rows of a 2xx body: nothing for an empty body, <c>null</c>, <c>[]</c> and <c>{}</c>; one row for an object;
        /// a row per element for an array of objects.
        /// </summary>
        /// <returns><c>false</c> when the body is anything else, which is not an empty table.</returns>
        internal static bool TryReadRows(string body, out IList<TikRecordSentence> rows)
        {
            rows = new List<TikRecordSentence>();
            if (string.IsNullOrWhiteSpace(body))
                return true;
            try
            {
                using (var doc = JsonDocument.Parse(body))
                {
                    var root = doc.RootElement;
                    switch (root.ValueKind)
                    {
                        case JsonValueKind.Null:
                            return true;
                        case JsonValueKind.Object:
                            if (root.EnumerateObject().MoveNext())
                                rows.Add(ToRow(root));
                            return true;
                        case JsonValueKind.Array:
                            foreach (var element in root.EnumerateArray())
                            {
                                if (element.ValueKind != JsonValueKind.Object)
                                    return false;
                                rows.Add(ToRow(element));
                            }
                            return true;
                        default:
                            return false;
                    }
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>Whether <paramref name="body"/> is a JSON object or array: the only things the REST API answers with.</summary>
        internal static bool IsJsonDocument(string body)
        {
            try
            {
                using (var doc = JsonDocument.Parse(body))
                    return doc.RootElement.ValueKind == JsonValueKind.Object || doc.RootElement.ValueKind == JsonValueKind.Array;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>Whether <paramref name="body"/> looks like the web server's page rather than an answer of the REST API.</summary>
        internal static bool IsHtml(string body) => body.TrimStart().StartsWith("<", StringComparison.Ordinal);

        /// <summary>The exception for a non-2xx answer.</summary>
        internal static Exception ToException(ITikCommand command, int statusCode, string body)
        {
            // Before gaining /rest in 7.1 the www service serves webfig only, and a page is the web server answering,
            // not the REST API refusing a row. Open refuses such a router; this is the service changing under an open connection.
            if (statusCode == 404 && IsHtml(body))
                return new TikNoSuchCommandException(command, new TikTrapSentenceResult(
                    "the router answered the REST request with an HTML 404 page rather than JSON. Request: " + command.CommandText));

            string message = ReadErrorBody(statusCode, body);
            var kind = TikTrapClassifier.Classify(message);
            if (kind == TikTrapKind.Generic && statusCode == 404)
                kind = TikTrapKind.NoSuchItem;
            return TikTrapClassifier.ToException(kind, command, new TikTrapSentenceResult(message));
        }

        // The router's message and detail, the raw body when it did not send them, and the status when it sent nothing.
        private static string ReadErrorBody(int statusCode, string body)
        {
            string? message = null;
            string? detail = null;
            try
            {
                using (var doc = JsonDocument.Parse(body))
                {
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        // ToString() rather than GetString(): a non-string "message" must render as itself, not throw
                        // out of the path that reports the error.
                        if (doc.RootElement.TryGetProperty("message", out var messageElement))
                            message = messageElement.ToString();
                        if (doc.RootElement.TryGetProperty("detail", out var detailElement))
                            detail = detailElement.ToString();
                    }
                }
            }
            catch (JsonException ex)
            {
                if (TikWireTrace.Enabled)
                    TikWireTrace.Emit("rest.http", TikWireDir.Note,
                        $"HTTP {statusCode} body is not JSON ({ex.Message}); reported verbatim");
            }

            if (message == null && detail == null)
                return "HTTP " + statusCode + (string.IsNullOrWhiteSpace(body) ? " with an empty body" : ": " + body);
            if (string.IsNullOrEmpty(detail) || detail == message)
                return message ?? detail!;
            return message == null ? detail! : message + ": " + detail;
        }

        private static TikRecordSentence ToRow(JsonElement element)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
                fields[property.Name] = property.Value.ValueKind == JsonValueKind.Null ? string.Empty : property.Value.ToString();
            return new TikRecordSentence(fields);
        }
    }
}
