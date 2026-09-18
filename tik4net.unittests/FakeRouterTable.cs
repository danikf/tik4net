using System;
using System.Collections.Generic;
using System.Linq;
using tik4net.Objects;
using tik4net.Testing;

namespace tik4net.unittests
{
    /// <summary>
    /// A stateful, in-memory stand-in for one router menu (one <c>[TikEntity]</c> path), wired into a
    /// <see cref="TikFakeConnection"/>.
    /// <para>
    /// <see cref="TikFakeConnection.WithEntities{TEntity}(TEntity[])"/> answers reads from a fixed list, which is
    /// enough to test a single call. It is not enough to test <see cref="TikListMerge{TEntity}"/>: a merge issues a
    /// whole sequence of <c>/add</c>, <c>/set</c>, <c>/unset</c>, <c>/remove</c> and <c>/move</c> commands whose only
    /// meaningful result is the state — and, for an ordered menu, the <em>order</em> — they leave behind. This table
    /// applies those commands to a row list, so a test can assert the router state the merge produced instead of
    /// asserting the commands it happened to emit.
    /// </para>
    /// <para>
    /// Rows are held at wire level (field name → string value), exactly as the router holds them, so entity mapping
    /// and value conversion still run for real on the way in and out.
    /// </para>
    /// </summary>
    internal sealed class FakeRouterTable<TEntity>
        where TEntity : new()
    {
        private readonly TikEntityMetadata _metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
        private readonly List<Dictionary<string, string>> _rows = new List<Dictionary<string, string>>();
        private int _nextId = 1;

        /// <summary>Commands the table applied, in order — for asserting <em>how</em> a merge got there.</summary>
        public List<string> AppliedCommands { get; } = new List<string>();

        /// <summary>
        /// Called with each write command (its words joined by spaces) before the table applies it. Returning a
        /// message refuses the command with that <c>!trap</c> and leaves the table untouched — a router that fails
        /// part-way through a merge. Also the place to cancel a token after the n-th command.
        /// </summary>
        public Func<string, string> BeforeWrite { get; set; }

        /// <summary>Router path of the emulated menu (e.g. <c>/ip/firewall/mangle</c>).</summary>
        public string Path => _metadata.EntityPath;

        /// <summary>Ids of the current rows, in router order.</summary>
        public IEnumerable<string> Ids => _rows.Select(r => r[TikSpecialProperties.Id]);

        /// <summary>Adds rows to the table as if they had always been on the router (assigning ids).</summary>
        public FakeRouterTable<TEntity> Seed(params TEntity[] entities)
        {
            foreach (var entity in entities)
                _rows.Add(ToRow(entity, "*" + _nextId++));
            return this;
        }

        /// <summary>
        /// Adds a row the router made itself (<c>dynamic=true</c>, like the fasttrack counter rule): the table
        /// refuses to move, remove or change it, and to move anything in front of it — as RouterOS does.
        /// </summary>
        public FakeRouterTable<TEntity> SeedDynamic(TEntity entity)
        {
            var row = ToRow(entity, "*" + _nextId++);
            row["dynamic"] = "true";
            _rows.Add(row);
            return this;
        }

        /// <summary>Reads the current table back as entities, in router order.</summary>
        public IList<TEntity> Load(ITikConnection connection)
            => connection.LoadAll<TEntity>().ToList();

        /// <summary>Registers the read and write handlers for this menu on <paramref name="connection"/>.</summary>
        public TikFakeConnection AttachTo(TikFakeConnection connection)
        {
            connection.WithResponse(
                rows => rows.First() == Path + _metadata.LoadCommand,
                _ => _rows
                    .Select(r => (ITikSentence)new TikFakeReSentence(new Dictionary<string, string>(r)))
                    .Concat(new ITikSentence[] { new TikFakeDoneSentence() })
                    .ToList());

            connection.WithResponse(rows => rows.First() == Path + "/add", rows =>
            {
                var refused = Refuse(rows);
                if (refused != null)
                    return refused;
                Record(rows);
                var row = ParseParameters(rows);
                string id = "*" + _nextId++;
                row[TikSpecialProperties.Id] = id;
                if (row.TryGetValue("place-before", out string placeBefore))
                {
                    row.Remove("place-before");
                    _rows.Insert(_rows.IndexOf(RowById(placeBefore)), row);
                }
                else
                    _rows.Add(row);   // the router appends
                return new ITikSentence[]
                {
                    new TikFakeDoneSentence(new Dictionary<string, string> { { TikSpecialProperties.Ret, id } })
                };
            });

            connection.WithResponse(rows => rows.First() == Path + "/set", rows =>
            {
                var values = ParseParameters(rows);
                var row = RowById(values[TikSpecialProperties.Id]);
                var refused = Refuse(rows) ?? RefuseBuiltin(row, "change");
                if (refused != null)
                    return refused;
                Record(rows);
                foreach (var pair in values.Where(p => p.Key != TikSpecialProperties.Id))
                    row[pair.Key] = pair.Value;
                return Done();
            });

            connection.WithResponse(rows => rows.First() == Path + "/unset", rows =>
            {
                var values = ParseParameters(rows);
                var row = RowById(values[TikSpecialProperties.Id]);
                var refused = Refuse(rows) ?? RefuseBuiltin(row, "change");
                if (refused != null)
                    return refused;
                Record(rows);
                row[values[TikSpecialProperties.UnsetValueName]] = "";
                return Done();
            });

            connection.WithResponse(rows => rows.First() == Path + "/remove", rows =>
            {
                var values = ParseParameters(rows);
                var row = RowById(values[TikSpecialProperties.Id]);
                var refused = Refuse(rows) ?? RefuseBuiltin(row, "remove");
                if (refused != null)
                    return refused;
                Record(rows);
                _rows.Remove(row);
                return Done();
            });

            connection.WithResponse(rows => rows.First() == Path + "/move", rows =>
            {
                var values = ParseParameters(rows);
                var moved = RowById(values["numbers"]);
                values.TryGetValue("destination", out string destination);
                var refused = Refuse(rows) ?? RefuseBuiltin(moved, "move")
                    ?? (destination != null ? RefuseBuiltin(RowById(destination), "move") : null);
                if (refused != null)
                    return refused;
                Record(rows);
                _rows.Remove(moved);

                if (destination != null)
                    _rows.Insert(_rows.IndexOf(RowById(destination)), moved);
                else
                    _rows.Add(moved);   // no destination → move to end
                return Done();
            });

            return connection;
        }

        private Dictionary<string, string> ToRow(TEntity entity, string id)
        {
            var row = _metadata.Properties.ToDictionary(p => p.FieldName, p => p.GetEntityValue(entity) ?? "");
            row[TikSpecialProperties.Id] = id;
            return row;
        }

        private Dictionary<string, string> RowById(string id)
        {
            var row = _rows.FirstOrDefault(r => r[TikSpecialProperties.Id] == id);
            if (row == null)
                throw new InvalidOperationException($"{Path}: no such item '{id}'.");
            return row;
        }

        private void Record(IEnumerable<string> rows) => AppliedCommands.Add(string.Join(" ", rows));

        private ITikSentence[] Refuse(IEnumerable<string> rows)
        {
            string message = BeforeWrite?.Invoke(string.Join(" ", rows));
            return message != null ? new ITikSentence[] { new TikFakeTrapSentence(message) } : null;
        }

        // RouterOS: "failure: cannot move builtin" / "cannot remove builtin" / "cannot change builtin".
        private static ITikSentence[] RefuseBuiltin(Dictionary<string, string> row, string verb)
            => row.TryGetValue("dynamic", out string dynamic) && dynamic == "true"
                ? new ITikSentence[] { new TikFakeTrapSentence("failure: cannot " + verb + " builtin") }
                : null;

        private static ITikSentence[] Done() => new ITikSentence[] { new TikFakeDoneSentence() };

        /// <summary>Splits <c>=name=value</c> command rows into a field dictionary (value may itself contain '=').</summary>
        private static Dictionary<string, string> ParseParameters(IEnumerable<string> rows)
        {
            var result = new Dictionary<string, string>();
            foreach (string row in rows.Skip(1).Where(r => r.StartsWith("=")))
            {
                int separator = row.IndexOf('=', 1);
                if (separator < 0)
                    continue;
                result[row.Substring(1, separator - 1)] = row.Substring(separator + 1);
            }
            return result;
        }
    }
}
