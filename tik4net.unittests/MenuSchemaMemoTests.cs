// Nullable-enabled on its own: TikMenuSchema's factories are annotated.
#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;
using tik4net.Testing;

namespace tik4net.unittests
{
    /// <summary>
    /// <see cref="TikMenuSchema"/> remembers what the router answered and not what failed, and describing it asks the
    /// router nothing.
    /// </summary>
    [TestClass]
    public class MenuSchemaMemoTests
    {
        private static readonly IReadOnlyCollection<string> Fields = new[] { "dst-address", "gateway" };

        // Every list counts how often it is asked; the first ask of each list fails when 'failFirst' is set.
        private static TikMenuSchema Schema(Dictionary<string, int> asked, bool failFirst)
        {
            T Ask<T>(string list, T answer)
            {
                asked[list] = asked.TryGetValue(list, out int n) ? n + 1 : 1;
                if (failFirst && asked[list] == 1)
                    throw new TimeoutException(list + " timed out");
                return answer;
            }

            return new TikMenuSchema("/ip/route", TikMenuSchemaSource.ConsoleInspect,
                () => Ask("tree", ((IReadOnlyCollection<string>)new[] { "add", "set", "get" }, (IReadOnlyCollection<string>?)null)),
                verb => Ask("arguments " + verb, Fields),
                () => Ask("readable", Fields),
                () => Ask("unset", Fields),
                (verb, argument) => Ask("values", (IReadOnlyList<string>)Array.Empty<string>()),
                verb => Ask("explanations", (IReadOnlyDictionary<string, string>)new Dictionary<string, string>()),
                (verb, argument) => Ask("grammar", (IReadOnlyList<string>)Array.Empty<string>()));
        }

        [TestMethod]
        public void AFailedReadIsAskedAgain_EveryList()
        {
            var asked = new Dictionary<string, int>();
            var schema = Schema(asked, failFirst: true);
            var reads = new (string List, Action Read)[]
            {
                ("tree", () => _ = schema.Commands),
                ("arguments add", () => _ = schema.AddArguments),
                ("readable", () => _ = schema.ReadableFields),
                ("unset", () => _ = schema.UnsetFields),
                ("values", () => _ = schema.ValuesOf("gateway")),
                ("explanations", () => _ = schema.Description("add")),
                ("grammar", () => _ = schema.ValueGrammar("gateway")),
            };

            foreach (var (list, read) in reads)
            {
                Assert.ThrowsException<TimeoutException>(read, list);
                read();   // the second read asks again, and the router answers
                read();   // and the answer is remembered
                Assert.AreEqual(2, asked[list], list);
            }
        }

        [TestMethod]
        public void ToStringAsksTheRouterNothing()
        {
            var asked = new Dictionary<string, int>();
            string text = Schema(asked, failFirst: false).ToString();

            StringAssert.Contains(text, "/ip/route");
            Assert.AreEqual(0, asked.Count, "asked: " + string.Join(", ", asked.Keys));
        }

        [TestMethod]
        public void ARememberedRefusalIsThrownAsANewException()
        {
            var cache = new TikMenuSchemaCache();
            var command = new TikFakeConnection().CreateCommand("/console/inspect");
            int described = 0;
            TikMenuSchema Refuse(string path)
            {
                described++;
                throw new TikNoSuchCommandException(command, new TikTrapSentenceResult("no such command"));
            }

            var first = Assert.ThrowsException<TikNoSuchCommandException>(() => cache.GetOrAdd("/ip/route", Refuse));
            var second = Assert.ThrowsException<TikNoSuchCommandException>(() => cache.GetOrAdd("/ip/route", Refuse));

            Assert.AreEqual(1, described, "the refusal is remembered");
            Assert.AreNotSame(first, second, "one exception object thrown twice keeps only the last stack trace");
            Assert.AreEqual(first.Message, second.Message);
            Assert.AreSame(first.Command, second.Command);
        }
    }
}
