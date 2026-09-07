using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    /// <summary>
    /// The <c>.tag</c> is the only thing tying a reply to the caller that asked for it, so what matters about
    /// it is that no two commands can ever share one.
    /// </summary>
    [TestClass]
    public class TagSequenceTests
    {
        /// <summary>
        /// The sequence is shared by every connection in the process, so concurrency is the interesting case:
        /// two sessions drawing at the same moment must not be handed the same value. A duplicate here would
        /// let one caller dequeue another's sentences on a shared connection — silently, and as wrong data
        /// rather than as an error.
        /// </summary>
        [TestMethod]
        public void ConcurrentCallersNeverDrawTheSameTag()
        {
            const int threads = 8;
            const int perThread = 2000;
            var seen = new ConcurrentBag<string>();

            Parallel.For(0, threads, _ =>
            {
                for (int i = 0; i < perThread; i++)
                    seen.Add(TagSequence.NextTag());
            });

            var all = seen.ToList();
            Assert.AreEqual(threads * perThread, all.Count);
            Assert.AreEqual(all.Count, all.Distinct().Count(),
                "two commands sharing a tag is a data-corruption bug, not a collision to retry");
        }

        /// <summary>
        /// A tag has to survive the round trip as an opaque router word and stay readable in a log line, so it
        /// carries nothing that needs escaping or quoting.
        /// </summary>
        [TestMethod]
        public void ATagIsPlainTextSafeToPutOnTheWire()
        {
            string tag = TagSequence.NextTag();

            Assert.IsFalse(string.IsNullOrEmpty(tag));
            Assert.IsTrue(tag.All(c => char.IsLetterOrDigit(c) || c == '-'),
                "the tag travels as a word and is read by people in router logs: " + tag);
            Assert.IsTrue(tag.Length <= 32,
                "it is sent with every command, so width is not free: " + tag.Length + " chars in " + tag);
        }

        /// <summary>
        /// The prefix names the program in the router's own log. Restored afterwards: the sequence is
        /// process-wide, and a test that left it set would rename every later test's tags.
        /// </summary>
        [TestMethod]
        public void ThePrefixNamesTheProgramInEveryTag()
        {
            string original = TagSequence.Prefix;
            try
            {
                TagSequence.Prefix = "mcp";
                StringAssert.StartsWith(TagSequence.NextTag(), "mcp");

                // Only letters and digits survive: a tag containing a separator, a space or an '=' would be
                // ambiguous in the very log line the prefix exists to disambiguate.
                TagSequence.Prefix = "m c=p!";
                StringAssert.StartsWith(TagSequence.NextTag(), "mcp");

                // An empty prefix falls back rather than dropping the field: a tag whose shape changed
                // with configuration could not be read back field by field.
                TagSequence.Prefix = string.Empty;
                StringAssert.StartsWith(TagSequence.NextTag(), TagSequence.DefaultPrefix + "-");
            }
            finally
            {
                TagSequence.Prefix = original;
            }
        }

        /// <summary>
        /// The shape is <c>prefix-pid-stamp-counter</c>: four fields, only the last of which varies within a
        /// run. Pinned because each of the other three rules out a specific collision, and a field silently
        /// going missing would restore it.
        /// </summary>
        [TestMethod]
        public void ATagIsFourFieldsOfWhichOnlyTheCounterVaries()
        {
            string first = TagSequence.NextTag();
            string second = TagSequence.NextTag();

            var firstFields = first.Split('-');
            Assert.AreEqual(4, firstFields.Length, "expected prefix-pid-stamp-counter, got " + first);
            Assert.IsTrue(firstFields.All(f => f.Length > 0), "no field may be empty: " + first);

            var secondFields = second.Split('-');
            CollectionAssert.AreEqual(firstFields.Take(3).ToArray(), secondFields.Take(3).ToArray(),
                "prefix, pid and stamp identify the run and must be stable across its tags");
            Assert.AreNotEqual(firstFields[3], secondFields[3]);
        }

        /// <summary>
        /// The process id is what makes "the MCP server and a running program cannot collide" a guarantee
        /// instead of a hope: concurrent processes on one host never share one. The startup stamp cannot carry
        /// that on its own, because DateTime.UtcNow is granular to about a millisecond on Windows and two
        /// processes launched by one script genuinely read the same value.
        /// </summary>
        /// <remarks>
        /// A second process cannot be started from here, so this asserts the field is present and is really
        /// this process's id — the part a regression would remove.
        /// </remarks>
        [TestMethod]
        public void TheTagCarriesThisProcessId()
        {
            using (var self = System.Diagnostics.Process.GetCurrentProcess())
            {
                string expected = ToBase36(self.Id);
                Assert.AreEqual(expected, TagSequence.NextTag().Split('-')[1],
                    "without the pid field, two programs talking to one router issue identical tags");
            }
        }

        private static string ToBase36(long value)
        {
            const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
            string result = "";
            while (value > 0)
            {
                result = digits[(int)(value % 36)] + result;
                value /= 36;
            }
            return result.Length == 0 ? "0" : result;
        }
    }
}
