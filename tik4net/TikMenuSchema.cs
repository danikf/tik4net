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
    /// What the router's own grammar says about one menu: its sub-menus and commands, the arguments of each command, the
    /// fields it reads and can clear, and the words an argument takes. Get it with
    /// <see cref="TikMenuSchemaExtensions.DescribeMenu(ITikConnection, string)"/>.
    /// </summary>
    /// <remarks>
    /// <para>The argument lists are what the router parses, so a name missing from them is a name the router refuses
    /// (except on <see cref="TikMenuSchemaSource.WinboxCatalog"/>, see there).</para>
    /// <para><see cref="ReadableFields"/> is the names <c>get value-name=</c> takes. It matched every field the API
    /// printed in the menus measured (<c>/ip/route</c> on 7.24.4 and 6.49.13); that it holds for every menu is not
    /// proven.</para>
    /// <para>Each list is asked the first time it is read and remembered.</para>
    /// </remarks>
    public sealed class TikMenuSchema
    {
        private readonly Func<string, string, IReadOnlyList<string>>? _values;
        private readonly Dictionary<string, IReadOnlyList<string>> _valuesCache
            = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        private readonly Func<string, IReadOnlyCollection<string>?> _arguments;
        private readonly Dictionary<string, IReadOnlyCollection<string>?> _argumentsCache
            = new Dictionary<string, IReadOnlyCollection<string>?>(StringComparer.Ordinal);

        private readonly Func<string?, IReadOnlyDictionary<string, string>>? _explanations;
        private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _explanationsCache
            = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

        private readonly Func<string, string, IReadOnlyList<string>>? _grammar;
        private readonly Dictionary<string, IReadOnlyList<string>> _grammarCache
            = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        private readonly Lazy<(IReadOnlyCollection<string> Commands, IReadOnlyCollection<string>? Submenus)> _tree;
        private readonly Lazy<IReadOnlyCollection<string>?> _readable, _unset;

        // A schema from fixed lists: a test's, or one a source knows whole.
        internal TikMenuSchema(string path, TikMenuSchemaSource source, IReadOnlyCollection<string> commands,
            IReadOnlyCollection<string>? addArguments, IReadOnlyCollection<string>? setArguments,
            IReadOnlyCollection<string>? readableFields, Func<string, string, IReadOnlyList<string>>? values,
            IReadOnlyCollection<string>? unsetFields = null, IReadOnlyCollection<string>? submenus = null)
            : this(path, source, () => (commands, submenus),
                  verb => verb == "add" ? addArguments : verb == "set" ? setArguments : null,
                  () => readableFields, () => unsetFields, values)
        {
        }

        // Each list is asked for the first time it is read: a filter check needs only the readable fields, and on a
        // RouterOS 6 CLI every list costs Tabs.
        internal TikMenuSchema(string path, TikMenuSchemaSource source,
            Func<(IReadOnlyCollection<string> Commands, IReadOnlyCollection<string>? Submenus)> tree,
            Func<string, IReadOnlyCollection<string>?> arguments,
            Func<IReadOnlyCollection<string>?> readableFields, Func<IReadOnlyCollection<string>?> unsetFields,
            Func<string, string, IReadOnlyList<string>>? values,
            Func<string?, IReadOnlyDictionary<string, string>>? explanations = null,
            Func<string, string, IReadOnlyList<string>>? grammar = null)
        {
            Path = path;
            Source = source;
            _tree = new Lazy<(IReadOnlyCollection<string>, IReadOnlyCollection<string>?)>(tree);
            _arguments = arguments;
            _readable = new Lazy<IReadOnlyCollection<string>?>(readableFields);
            _unset = new Lazy<IReadOnlyCollection<string>?>(unsetFields);
            _values = values;
            _explanations = explanations;
            _grammar = grammar;
        }

        /// <summary>The menu, as it was asked for (<c>/ip/route</c>).</summary>
        public string Path { get; }

        /// <summary>
        /// Set on a schema answered from the persistent cache: asks the router again and returns the live schema. A
        /// check that would refuse on a cached list calls it first, so a stale cache can never refuse a request the
        /// router takes. <c>null</c> on a schema the router answered.
        /// </summary>
        internal Func<TikMenuSchema>? Relearn { get; set; }

        /// <summary>Where this description came from.</summary>
        public TikMenuSchemaSource Source { get; }

        /// <summary>
        /// The menu's commands (<c>add</c>, <c>set</c>, <c>print</c>, <c>ping</c>, …), without its sub-menus. Empty on
        /// WinBox native.
        /// </summary>
        /// <remarks>On a RouterOS 6 CLI without colour (Telnet, SSH) the router does not say which listed word is a
        /// sub-menu, so the first read asks one Tab per word.</remarks>
        public IReadOnlyCollection<string> Commands => _tree.Value.Commands;

        /// <summary>
        /// The menu's sub-menus (<c>/ip/route</c>: <c>rule</c>, <c>vrf</c>, …), or <c>null</c> on WinBox native, which
        /// cannot say. See <see cref="Commands"/> for the cost on a RouterOS 6 CLI.
        /// </summary>
        public IReadOnlyCollection<string>? Submenus => _tree.Value.Submenus;

        /// <summary>
        /// Whether the rows' order is part of the configuration (the menu has <c>move</c>: firewall rules, queues, …), or
        /// <c>null</c> on WinBox native, which cannot say.
        /// </summary>
        public bool? IsOrdered => Source == TikMenuSchemaSource.WinboxCatalog ? (bool?)null : Commands.Contains("move");

        /// <summary>
        /// The arguments <paramref name="verb"/> takes (<c>add</c>, <c>set</c>, <c>ping</c>, …), or <c>null</c> when the
        /// menu has no such command. On WinBox native only <c>add</c> and <c>set</c> are known.
        /// </summary>
        public IReadOnlyCollection<string>? Arguments(string verb)
        {
            Guard.ArgumentNotNullOrEmptyString(verb, nameof(verb));
            lock (_argumentsCache)
            {
                if (_argumentsCache.TryGetValue(verb, out var cached))
                    return cached;
            }
            var arguments = _arguments(verb);
            lock (_argumentsCache)
                _argumentsCache[verb] = arguments;
            return arguments;
        }

        /// <summary>The arguments <c>add</c> takes, or <c>null</c> when the menu has no <c>add</c>.</summary>
        public IReadOnlyCollection<string>? AddArguments => Arguments("add");

        /// <summary>The arguments <c>set</c> takes, or <c>null</c> when the menu has no <c>set</c>.</summary>
        public IReadOnlyCollection<string>? SetArguments => Arguments("set");

        /// <summary>
        /// Fields the router says it can read and filter on (<c>get value-name=</c>), or <c>null</c> when the menu has no
        /// <c>get</c> to ask — see the remarks.
        /// </summary>
        public IReadOnlyCollection<string>? ReadableFields => _readable.Value;

        /// <summary>
        /// The names <c>unset value-name=</c> takes: the fields that can be cleared back to "not set" (a firewall rule's
        /// matchers, not its <c>chain</c> or <c>action</c>). <c>null</c> when the menu has no <c>unset</c>, and on WinBox
        /// native, which cannot say. The router's own list: 7.24.4 also offers <c>all</c>, <c>dynamic</c> and
        /// <c>static</c> on <c>/ip/firewall/filter</c>.
        /// </summary>
        public IReadOnlyCollection<string>? UnsetFields => _unset.Value;

        /// <summary>
        /// The words <paramref name="argument"/> of <paramref name="verb"/> suggests: an enum's members, or the names a
        /// reference field can point at now (interfaces, lists). Empty for a free value (a number, an address). Asked the
        /// first time and remembered; one round trip on the API, REST and a CLI transport, none on WinBox native.
        /// </summary>
        /// <param name="argument">The argument's RouterOS name (<c>action</c>).</param>
        /// <param name="verb">The command: <c>add</c>, <c>set</c>, or any other the menu has.</param>
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

        /// <summary>
        /// The router's description of <paramref name="word"/>: a sub-menu or a command of this menu when
        /// <paramref name="verb"/> is <c>null</c> (<c>add</c> → "Create a new item"), else an argument of that command
        /// (<c>distance</c> of <c>add</c> on <c>/ip/route</c> → "Administrative distance of the route"). <c>null</c> when
        /// the router describes nothing for it — many words have no text — and on WinBox native.
        /// </summary>
        /// <remarks>
        /// RouterOS 7 answers <c>/console/inspect request=syntax</c> on the API, REST and every CLI transport; RouterOS 6
        /// answers the help key (F1) on a CLI transport, and over the API it has neither. One round trip per menu or
        /// command, asked the first time and remembered; not kept in the on-disk grammar cache.
        /// </remarks>
        /// <param name="word">The sub-menu, command or argument (<c>distance</c>).</param>
        /// <param name="verb">The command the argument belongs to, or <c>null</c> for a word of the menu itself.</param>
        public string? Description(string word, string? verb = null)
        {
            Guard.ArgumentNotNullOrEmptyString(word, nameof(word));
            return Explanations(verb).TryGetValue(word, out var text) ? text : null;
        }

        /// <summary>
        /// The value grammar of <paramref name="argument"/>, as the router writes it, one definition per line:
        /// <c>Distance ::= Num</c>, <c>Num ::= 1..255    (integer number)</c>. Empty when the router gives none (an enum
        /// on RouterOS 7, whose words are <see cref="ValuesOf"/>) and on WinBox native. The notation is RouterOS's own and
        /// is not parsed here.
        /// </summary>
        /// <remarks>Where it comes from, and what it costs, as for <see cref="Description"/>. RouterOS 6 writes an enum's
        /// grammar too (<c>Chain ::= input | forward | output</c>), and cuts a long one with <c>...</c>.</remarks>
        /// <param name="argument">The argument's RouterOS name (<c>distance</c>).</param>
        /// <param name="verb">The command: <c>add</c>, <c>set</c>, or any other the menu has.</param>
        public IReadOnlyList<string> ValueGrammar(string argument, string verb = "set")
        {
            Guard.ArgumentNotNullOrEmptyString(argument, nameof(argument));
            Guard.ArgumentNotNullOrEmptyString(verb, nameof(verb));
            if (_grammar == null)
                return Array.Empty<string>();
            string key = verb + " " + argument;
            lock (_grammarCache)
            {
                if (_grammarCache.TryGetValue(key, out var cached))
                    return cached;
            }
            var grammar = _grammar(verb, argument);
            lock (_grammarCache)
                _grammarCache[key] = grammar;
            return grammar;
        }

        // Every description the router gives for one level: the menu's words (verb null) or one command's arguments.
        internal IReadOnlyDictionary<string, string> Explanations(string? verb)
        {
            if (_explanations == null)
                return EmptyExplanations;
            string key = verb ?? "";
            lock (_explanationsCache)
            {
                if (_explanationsCache.TryGetValue(key, out var cached))
                    return cached;
            }
            var explanations = _explanations(verb);
            lock (_explanationsCache)
                _explanationsCache[key] = explanations;
            return explanations;
        }

        private static readonly IReadOnlyDictionary<string, string> EmptyExplanations
            = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <inheritdoc/>
        public override string ToString()
            => Path + " (" + Source + "): add " + Count(AddArguments) + ", set " + Count(SetArguments) + ", readable "
               + Count(ReadableFields) + ", unset " + Count(UnsetFields);

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

        /// <summary>Puts <paramref name="schema"/> in place of what this connection remembered for the path.</summary>
        internal void Replace(string path, TikMenuSchema schema, string? variant = null)
        {
            string key = variant == null ? path : path + "|" + variant;
            lock (_byPath)
            {
                _byPath[key] = schema;
                _refused.Remove(key);
            }
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
            var submenus = Children(menu, "dir");

            return new TikMenuSchema(path, TikMenuSchemaSource.ConsoleInspect,
                () => (commands, submenus),
                verb => commands.Contains(verb) ? Arguments(connection, inspectPath + "," + verb) : null,
                () => commands.Contains("get") ? (IReadOnlyCollection<string>?)Completions(connection, inspectPath + ",get,value-name") : null,
                () => commands.Contains("unset") ? (IReadOnlyCollection<string>?)Completions(connection, inspectPath + ",unset,value-name") : null,
                (verb, argument) => Completions(connection, inspectPath + "," + verb + "," + argument),
                verb => Explanations(connection, verb == null ? inspectPath : inspectPath + "," + verb),
                (verb, argument) => Grammar(connection, inspectPath + "," + verb + "," + argument));
        }

        // 'syntax' on a menu or a command: one 'explanation' row per word, its description in 'text' (often empty).
        private static IReadOnlyDictionary<string, string> Explanations(ITikConnection connection, string inspectPath)
        {
            var explanations = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in Inspect(connection, "syntax", inspectPath))
            {
                string? symbol = row.GetResponseFieldOrDefault("symbol", null);
                string? text = row.GetResponseFieldOrDefault("text", null);
                if (row.GetResponseFieldOrDefault("symbol-type", null) == "explanation"
                    && !string.IsNullOrEmpty(symbol) && !string.IsNullOrEmpty(text) && symbol != "..")
                    explanations[symbol!] = text!;
            }
            return explanations;
        }

        // 'syntax' on an argument: 'definition' rows, the first naming the value and the rest what it is built of
        // (Distance, then Num = 1..255). An enum answers one empty definition.
        private static IReadOnlyList<string> Grammar(ITikConnection connection, string inspectPath)
            => Inspect(connection, "syntax", inspectPath)
                .Where(r => r.GetResponseFieldOrDefault("symbol-type", null) == "definition"
                            && !string.IsNullOrEmpty(r.GetResponseFieldOrDefault("symbol", null)))
                .Select(r => r.GetResponseField("symbol") + " ::= " + r.GetResponseFieldOrDefault("text", ""))
                .ToList();

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
