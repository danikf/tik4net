using System;
using System.Collections.Generic;
using System.Linq;
using tik4net.Connection;

namespace tik4net
{
    /// <summary>Where a <see cref="TikMenuSchema"/> came from.</summary>
    public enum TikMenuSchemaSource
    {
        /// <summary>The router's <c>/console/inspect</c> (RouterOS 7), over the API, REST or a CLI transport.</summary>
        ConsoleInspect,

        /// <summary>Tab completion on a CLI transport (RouterOS 6, which has no <c>/console/inspect</c>).</summary>
        CliCompletion,

        /// <summary>
        /// The WinBox <c>.jg</c> catalog, on WinBox native. The names are tik4net's reconstruction of the API names from
        /// WinBox labels, not the router's own words, so a name missing here can be a gap in tik4net's mapping.
        /// </summary>
        WinboxCatalog,
    }

    /// <summary>
    /// What the router's own grammar says about one menu: its commands, the arguments of <c>add</c> and <c>set</c>, the
    /// fields it reads, and the words an argument takes. Get it with
    /// <see cref="TikMenuSchemaExtensions.DescribeMenu(ITikConnection, string)"/>.
    /// </summary>
    /// <remarks>
    /// <para>The argument lists are what the router parses, so a name missing from them is a name the router refuses
    /// (except on <see cref="TikMenuSchemaSource.WinboxCatalog"/>, see there).</para>
    /// <para><see cref="ReadableFields"/> is the names <c>get value-name=</c> takes. It matched every field the API
    /// printed in the menus measured (<c>/ip/route</c> on 7.24.4 and 6.49.13); that it holds for every menu is not
    /// proven.</para>
    /// </remarks>
    public sealed class TikMenuSchema
    {
        private readonly Func<string, string, IReadOnlyList<string>>? _values;
        private readonly Dictionary<string, IReadOnlyList<string>> _valuesCache
            = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        private readonly Lazy<IReadOnlyCollection<string>?> _add, _set, _readable;

        internal TikMenuSchema(string path, TikMenuSchemaSource source, IReadOnlyCollection<string> commands,
            IReadOnlyCollection<string>? addArguments, IReadOnlyCollection<string>? setArguments,
            IReadOnlyCollection<string>? readableFields, Func<string, string, IReadOnlyList<string>>? values)
            : this(path, source, commands, () => addArguments, () => setArguments, () => readableFields, values)
        {
        }

        // Each list is asked for the first time it is read: a filter check needs only the readable fields, and on a
        // RouterOS 6 CLI every list is a walk of several Tabs.
        internal TikMenuSchema(string path, TikMenuSchemaSource source, IReadOnlyCollection<string> commands,
            Func<IReadOnlyCollection<string>?> addArguments, Func<IReadOnlyCollection<string>?> setArguments,
            Func<IReadOnlyCollection<string>?> readableFields, Func<string, string, IReadOnlyList<string>>? values)
        {
            Path = path;
            Source = source;
            Commands = commands;
            _add = new Lazy<IReadOnlyCollection<string>?>(addArguments);
            _set = new Lazy<IReadOnlyCollection<string>?>(setArguments);
            _readable = new Lazy<IReadOnlyCollection<string>?>(readableFields);
            _values = values;
        }

        /// <summary>The menu, as it was asked for (<c>/ip/route</c>).</summary>
        public string Path { get; }

        /// <summary>Where this description came from.</summary>
        public TikMenuSchemaSource Source { get; }

        /// <summary>The menu's commands (<c>add</c>, <c>set</c>, <c>print</c>, …). Empty on WinBox native.</summary>
        public IReadOnlyCollection<string> Commands { get; }

        /// <summary>The arguments <c>add</c> takes, or <c>null</c> when the menu has no <c>add</c>.</summary>
        public IReadOnlyCollection<string>? AddArguments => _add.Value;

        /// <summary>The arguments <c>set</c> takes, or <c>null</c> when the menu has no <c>set</c>.</summary>
        public IReadOnlyCollection<string>? SetArguments => _set.Value;

        /// <summary>
        /// Fields the router says it can read and filter on (<c>get value-name=</c>), or <c>null</c> when the menu has no
        /// <c>get</c> to ask — see the remarks.
        /// </summary>
        public IReadOnlyCollection<string>? ReadableFields => _readable.Value;

        /// <summary>
        /// The words <paramref name="argument"/> of <paramref name="verb"/> suggests: an enum's members, or the names a
        /// reference field can point at now (interfaces, lists). Empty for a free value (a number, an address). Asked the
        /// first time and remembered; one round trip on the API, REST and a CLI transport, none on WinBox native.
        /// </summary>
        /// <param name="argument">The argument's RouterOS name (<c>action</c>).</param>
        /// <param name="verb"><c>add</c> or <c>set</c>.</param>
        public IReadOnlyList<string> ValuesOf(string argument, string verb = "set")
        {
            Guard.ArgumentNotNullOrEmptyString(argument, nameof(argument));
            Guard.ArgumentNotNullOrEmptyString(verb, nameof(verb));
            if (_values == null)
                return Array.Empty<string>();
            string key = verb + " " + argument;
            lock (_valuesCache)
            {
                if (_valuesCache.TryGetValue(key, out var cached))
                    return cached;
            }
            var values = _values(verb, argument);
            lock (_valuesCache)
                _valuesCache[key] = values;
            return values;
        }

        /// <inheritdoc/>
        public override string ToString()
            => Path + " (" + Source + "): add " + Count(AddArguments) + ", set " + Count(SetArguments) + ", readable "
               + Count(ReadableFields);

        private static string Count(IReadOnlyCollection<string>? names) => names == null ? "-" : names.Count.ToString();
    }

    /// <summary>A connection that can describe a menu (<see cref="TikConnectionCapability.MenuSchema"/>).</summary>
    internal interface ITikMenuSchemaConnection
    {
        /// <param name="path">Normalized menu path.</param>
        /// <param name="winboxLabels">An entity's API-name → WinBox-label pairs (<see cref="TikSpecialProperties.WinboxLabels"/>'s
        /// format), which WinBox native names its fields by; ignored elsewhere. <c>null</c> for a path alone.</param>
        TikMenuSchema DescribeMenu(string path, string? winboxLabels);

        /// <summary><see cref="TikConnectionSetup.ValidateWrites"/>, read by the O/R mapper before an entity write.</summary>
        bool ValidateWrites { get; set; }
    }

    /// <summary>Asks the router what a menu takes.</summary>
    public static class TikMenuSchemaExtensions
    {
        /// <summary>
        /// Describes <paramref name="path"/> (<c>/ip/route</c>) from the router's own grammar. Asked once per menu and
        /// remembered until the connection is opened again.
        /// </summary>
        /// <exception cref="TikConnectionCapabilityNotSupportedException">The transport has no way to ask
        /// (<see cref="TikConnectionCapability.MenuSchema"/>).</exception>
        /// <exception cref="TikNoSuchCommandException">The router has no such menu, or no way to describe one: RouterOS
        /// 6 has no <c>/console/inspect</c>, so over the API it refuses the question (a CLI transport asks by Tab
        /// completion instead).</exception>
        public static TikMenuSchema DescribeMenu(this ITikConnection connection, string path)
        {
            Guard.ArgumentNotNull(connection, nameof(connection));
            Guard.ArgumentNotNullOrEmptyString(path, nameof(path));
            connection.Require(TikConnectionCapability.MenuSchema, "DescribeMenu");
            return ((ITikMenuSchemaConnection)connection).DescribeMenu(TikMenuSchemaPath.Normalize(path), null);
        }

        // The entity overload's way in (tik4net.objects): the entity's WinBox labels name native's fields as the API does.
        internal static TikMenuSchema DescribeMenu(ITikConnection connection, string path, string? winboxLabels)
        {
            Guard.ArgumentNotNull(connection, nameof(connection));
            connection.Require(TikConnectionCapability.MenuSchema, "DescribeMenu");
            return ((ITikMenuSchemaConnection)connection).DescribeMenu(TikMenuSchemaPath.Normalize(path), winboxLabels);
        }
    }

    internal static class TikMenuSchemaPath
    {
        // "/ip/route/" and "ip/route" alike → "/ip/route".
        internal static string Normalize(string path) => "/" + path.Trim().Trim('/');

        // "/ip/route" → ["ip", "route"]
        internal static string[] Segments(string path) => path.Trim('/').Split('/');
    }

    /// <summary>
    /// Per-connection cache of described menus; a connection replaces it when it opens, as it replaces everything it
    /// learnt about the previous router.
    /// </summary>
    internal sealed class TikMenuSchemaCache
    {
        private readonly Dictionary<string, TikMenuSchema> _byPath = new Dictionary<string, TikMenuSchema>(StringComparer.Ordinal);

        // A refusal is remembered too: RouterOS 6 over the API refuses /console/inspect, and every filtered read asks
        // before it is sent — without this each one would pay a round trip for the same refusal.
        private readonly Dictionary<string, TikNoSuchCommandException> _refused
            = new Dictionary<string, TikNoSuchCommandException>(StringComparer.Ordinal);

        internal TikMenuSchema GetOrAdd(string path, Func<string, TikMenuSchema> describe, string? variant = null)
        {
            string key = variant == null ? path : path + "|" + variant;
            lock (_byPath)
            {
                if (_byPath.TryGetValue(key, out var schema))
                    return schema;
                if (_refused.TryGetValue(key, out var refusal))
                    throw refusal;
            }
            TikMenuSchema described;
            try
            {
                described = describe(path);
            }
            catch (TikNoSuchCommandException ex)
            {
                lock (_byPath)
                    _refused[key] = ex;
                throw;
            }
            lock (_byPath)
                _byPath[key] = described;
            return described;
        }
    }

    /// <summary>
    /// <c>/console/inspect</c> (RouterOS 7): the same commands over the API, REST and the CLI, sent through the
    /// connection's own command translation.
    /// </summary>
    internal static class ConsoleInspectSchemaReader
    {
        internal static TikMenuSchema Read(ITikConnection connection, string path)
        {
            string inspectPath = string.Join(",", TikMenuSchemaPath.Segments(path));

            // The menu node itself first: an absent menu and a menu without 'add' both answer [] to "children of add".
            var menu = Inspect(connection, "child", inspectPath);
            if (!menu.Any(r => r.GetResponseFieldOrDefault("type", null) == "self"))
            {
                var command = connection.CreateCommand("/console/inspect");
                throw new TikNoSuchCommandException(command,
                    new TikTrapSentenceResult("no such menu " + path + " (/console/inspect lists nothing for it)"));
            }
            var commands = Children(menu, "cmd");

            return new TikMenuSchema(path, TikMenuSchemaSource.ConsoleInspect, commands,
                () => commands.Contains("add") ? Arguments(connection, inspectPath + ",add") : null,
                () => commands.Contains("set") ? Arguments(connection, inspectPath + ",set") : null,
                () => commands.Contains("get") ? (IReadOnlyCollection<string>?)Completions(connection, inspectPath + ",get,value-name") : null,
                (verb, argument) => Completions(connection, inspectPath + "," + verb + "," + argument));
        }

        private static IReadOnlyCollection<string> Arguments(ITikConnection connection, string inspectPath)
            => Children(Inspect(connection, "child", inspectPath), "arg");

        private static List<string> Children(IEnumerable<ITikReSentence> rows, string nodeType)
            => rows.Where(r => r.GetResponseFieldOrDefault("type", null) == "child"
                            && r.GetResponseFieldOrDefault("node-type", null) == nodeType)
                   .Select(r => r.GetResponseField("name"))
                   .ToList();

        // The words a completion offers; syntax helpers ('[', '$', '"', the id prefix, '<value>') are 'show=false'.
        private static IReadOnlyList<string> Completions(ITikConnection connection, string inspectPath)
            => Inspect(connection, "completion", inspectPath)
                .Where(r => r.GetResponseFieldOrDefault("show", null) == "true")
                .Select(r => r.GetResponseFieldOrDefault("completion", null))
                .Where(c => !string.IsNullOrEmpty(c))
                .Select(c => c!)
                .Distinct(StringComparer.Ordinal)
                .ToList();

        private static List<ITikReSentence> Inspect(ITikConnection connection, string request, string inspectPath)
            => connection.CreateCommand("/console/inspect",
                    connection.CreateParameter("request", request, TikCommandParameterFormat.NameValue),
                    connection.CreateParameter("path", inspectPath, TikCommandParameterFormat.NameValue))
                .ExecuteList()
                .ToList();
    }
}
