using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using tik4net.Connection;

namespace tik4net.Rest
{
    /// <summary>
    /// What a command path's trailing segment names, when it is not a verb with its own REST spelling
    /// (<see cref="RestRequestBuilder.HasOwnShape"/>). <c>/log/error</c> and <c>/ip/address</c> are the same shape, so
    /// the text alone cannot say; <see cref="RestConnection"/> decides (<c>Docs/findings-rest-api.md</c> §10).
    /// </summary>
    internal enum RestTrailingSegment
    {
        /// <summary>A menu: the command is an implicit <c>print</c> of it.</summary>
        Menu,

        /// <summary>A command: its path is POSTed as given, with the parameters as its inputs.</summary>
        Command,
    }

    /// <summary>
    /// Translates a MikroTik API command (path + parameters) into an HTTP request for the RouterOS REST API.
    /// Pure logic — no network dependency, unit-testable standalone.
    /// </summary>
    internal static class RestRequestBuilder
    {
        // The verbs REST spells in a way of its own. Every other command is a POST to its path.
        private static readonly HashSet<string> OwnShapeVerbs = new HashSet<string>(StringComparer.Ordinal)
        {
            "print", "get", "add", "set", "remove", "unset",
        };

        internal sealed class RestRequest
        {
            public HttpMethod Method { get; }
            public string RelativePath { get; }   // e.g. /ip/address, relative to the /rest base URL
            public string? JsonBody { get; }      // null for GET/DELETE

            internal RestRequest(HttpMethod method, string relativePath, string? jsonBody = null)
            {
                Method = method;
                RelativePath = relativePath;
                JsonBody = jsonBody;
            }
        }

        /// <summary>Whether the command's last segment is a verb REST spells itself, so that nothing has to be asked about it.</summary>
        internal static bool HasOwnShape(string commandText) => OwnShapeVerbs.Contains(TikPath.Verb(commandText));

        /// <summary>
        /// Builds a <see cref="RestRequest"/> from a MikroTik API command.
        /// </summary>
        /// <param name="commandText">API command path, e.g. /ip/address/print</param>
        /// <param name="parameters">Command parameters.</param>
        /// <param name="trailingSegment">What the last segment names when it has no shape of its own.</param>
        public static RestRequest Build(string commandText, IList<ITikCommandParameter> parameters,
            RestTrailingSegment trailingSegment = RestTrailingSegment.Menu)
        {
            string path = TikPath.Normalize(commandText);
            string menu = TikPath.Parent(path);

            switch (TikPath.Verb(path))
            {
                case "print":
                    return BuildPrint(menu, parameters);
                case "get":
                    return BuildGet(menu, parameters);
                case "add":
                    return BuildAdd(menu, parameters);
                case "set":
                    return BuildSet(menu, parameters);
                case "remove":
                    return BuildRemove(menu, parameters);
                case "unset":
                    return BuildCommandPost(path, parameters);
                default:
                    return trailingSegment == RestTrailingSegment.Command
                        ? BuildCommandPost(path, parameters)
                        : BuildPrint(path, parameters);
            }
        }

        // ── READ ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds the <c>get</c> verb: <c>GET /rest/path/row</c>, narrowed with <c>?.proplist=field</c> when a
        /// <c>value-name</c> was given. The row is <c>.id</c> or <c>number</c>, both of which take a name; a singleton
        /// menu has neither. REST has no <c>get</c> of its own (<c>POST /rest/interface/get</c> answers
        /// <c>400 no such command</c>), so the verb is translated rather than forwarded.
        /// </summary>
        private static RestRequest BuildGet(string menu, IList<ITikCommandParameter> parameters)
        {
            string? row = TikParameterFormat.FindInput(parameters, TikSpecialProperties.Id)
                          ?? TikParameterFormat.FindInput(parameters, "number");
            string? valueName = TikParameterFormat.FindInput(parameters, TikSpecialProperties.UnsetValueName);

            string path = row != null ? RowPath(menu, row) : menu;
            if (valueName != null)
                path += "?.proplist=" + Uri.EscapeDataString(valueName);

            return new RestRequest(HttpMethod.Get, path);
        }

        private static RestRequest BuildPrint(string menu, IList<ITikCommandParameter> parameters)
        {
            var query = new List<string>();
            var inputs = new Dictionary<string, string>();
            string? proplist = null;

            foreach (var p in parameters)
            {
                string name = TikParameterFormat.BareName(p.Name);
                if (name == TikSpecialProperties.Proplist)
                    proplist = p.Value;
                else if (IsSpecialParam(name))
                    continue;
                else if (IsFilterParam(p))
                    // A NULL value is the API's bare '?name' — "this property is set" — which REST's .query spells as the
                    // bare name too. 'name=' would ask for the property equal to the EMPTY STRING.
                    query.Add(p.Value == null ? name : name + "=" + p.Value);
                else if (p.ParameterFormat == TikCommandParameterFormat.NameValue || p.Name.StartsWith("=", StringComparison.Ordinal))
                    inputs[name] = p.Value ?? string.Empty;   // a command input (e.g. /tool/wol mac=...)
            }

            if (query.Count == 0 && inputs.Count == 0)
                return new RestRequest(HttpMethod.Get,
                    proplist == null ? menu : menu + "?.proplist=" + Uri.EscapeDataString(proplist));

            return new RestRequest(HttpMethod.Post, menu + "/print", WriteBody(inputs, query, proplist));
        }

        // ── WRITE ─────────────────────────────────────────────────────────────

        private static RestRequest BuildAdd(string menu, IList<ITikCommandParameter> parameters)
            => new RestRequest(HttpMethod.Put, menu, WriteBody(BuildInputs(parameters, includeFilters: false)));

        private static RestRequest BuildSet(string menu, IList<ITikCommandParameter> parameters)
        {
            string? id = TikParameterFormat.FindInput(parameters, TikSpecialProperties.Id);

            // PATCH addresses one record. No .id is a singleton (or 'numbers') and has nothing to put in the URL: PATCH
            // answers 400 "missing or invalid resource identifier" there. A list of ids in the URL is applied and then
            // answered with 404 (7.25). POST /set is the router's own spelling of both.
            if (id == null || id.IndexOf(',') >= 0)
                return new RestRequest(HttpMethod.Post, menu + "/set", WriteBody(BuildInputs(parameters, includeFilters: false)));

            var body = BuildInputs(parameters, includeFilters: false);
            body.Remove(TikSpecialProperties.Id);
            return new RestRequest(new HttpMethod("PATCH"), RowPath(menu, id), WriteBody(body));
        }

        private static RestRequest BuildRemove(string menu, IList<ITikCommandParameter> parameters)
        {
            string? id = TikParameterFormat.FindInput(parameters, TikSpecialProperties.Id);
            return id != null
                ? new RestRequest(HttpMethod.Delete, RowPath(menu, id))
                : BuildCommandPost(menu + "/remove", parameters);
        }

        /// <remarks>
        /// A Filter-format parameter is an input here, as it is for the API: <c>TikGenericCommand.ResolveParamsForRead</c>
        /// rewrites Default-format parameters to Filter, and commands that return a row (<c>/tool/wol</c>) are legitimately
        /// invoked through a read method. Dropped, RouterOS answers "missing =mac=" for a call that did supply the mac.
        /// </remarks>
        private static RestRequest BuildCommandPost(string path, IList<ITikCommandParameter> parameters)
        {
            var body = BuildInputs(parameters, includeFilters: true);
            return new RestRequest(HttpMethod.Post, path, body.Count > 0 ? WriteBody(body) : null);
        }

        // ── HELPERS ───────────────────────────────────────────────────────────

        // The row's URL. An id is a '*' plus hex or a name, which may hold '/', '?' or '#'. The router decodes all of them
        // but '*': /rest/interface/list/%2A2000057 answers 400 "no such command prefix" (7.25).
        private static string RowPath(string menu, string row)
            => menu + "/" + Uri.EscapeDataString(row).Replace("%2A", "*");

        private static bool IsFilterParam(ITikCommandParameter p)
            => p.Name.StartsWith("?", StringComparison.Ordinal) || p.ParameterFormat == TikCommandParameterFormat.Filter;

        /// <summary>
        /// Parameters that are NOT emitted as plain data words: the client-side markers
        /// (<see cref="TikTransportMarkers.IsClientMarker"/>; REST answers in JSON with the flags and the secrets, so
        /// it needs none of them), plus REST's own three:
        ///   <c>.proplist</c> — picked up separately by <see cref="BuildPrint"/>.
        ///   <c>.tag</c>      — no tag protocol over REST.
        ///   <c>detail</c>    — no-op in REST (full details are returned by default).
        /// </summary>
        private static bool IsSpecialParam(string bareName)
        {
            return bareName == TikSpecialProperties.Tag
                || bareName == TikSpecialProperties.Proplist
                || TikTransportMarkers.IsClientMarker(bareName)
                || string.Equals(bareName, "detail", StringComparison.OrdinalIgnoreCase);
        }

        // The JSON fields of a command's parameters. An .id without a value says nothing (a singleton has none).
        private static Dictionary<string, string> BuildInputs(IList<ITikCommandParameter> parameters, bool includeFilters)
        {
            var body = new Dictionary<string, string>();
            foreach (var p in parameters)
            {
                string name = TikParameterFormat.BareName(p.Name);
                if ((IsFilterParam(p) && !includeFilters) || IsSpecialParam(name))
                    continue;
                if (name == TikSpecialProperties.Id && string.IsNullOrEmpty(p.Value))
                    continue;
                body[name] = p.Value ?? string.Empty;
            }
            return body;
        }

        // Written by hand because every encoder of Utf8JsonWriter sends some text as \uXXXX (the relaxed one, what lies
        // outside the BMP), and RouterOS stores an escape as one code-page byte but raw text as it arrives
        // (Docs/findings-rest-api.md §1): an escape reads back as something else on every transport.
        private static string WriteBody(Dictionary<string, string> fields, List<string>? query = null, string? proplist = null)
        {
            var members = new List<string>();
            if (query != null && query.Count > 0)
                members.Add(JsonString(".query") + ":" + JsonArray(query));
            if (proplist != null)
                members.Add(JsonString(".proplist") + ":" + JsonArray(proplist.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)));
            foreach (var field in fields)
                members.Add(JsonString(field.Key) + ":" + JsonString(field.Value));
            return "{" + string.Join(",", members) + "}";
        }

        private static string JsonArray(IEnumerable<string> values) => "[" + string.Join(",", values.Select(JsonString)) + "]";

        private static string JsonString(string text)
        {
            var json = new StringBuilder(text.Length + 2).Append('"');
            foreach (char c in text)
            {
                if (c == '"' || c == '\\')
                    json.Append('\\').Append(c);
                else if (c < ' ')
                    json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else
                    json.Append(c);
            }
            return json.Append('"').ToString();
        }
    }
}
