// ApiSentenceWordSplitTests.cs — how ApiSentence splits an API word into its key and value.
//
// A word is `=key=value` (or `.tag=1`, `key=value`): the key is everything after an optional leading `=` up to
// the next `=`, and the value is EVERYTHING after that, byte for byte — further `=`, line breaks, an empty
// value, a whole script. The split used to be a regex (`^=?(?<KEY>[^=]+)=(?<VALUE>.*)$`, Singleline); it is now
// an IndexOf. The regex is kept here as the oracle, and every input below must split the same way under both,
// including the ones that do not split at all (`==x`, `=`, `x`): a word that fails the split is dropped.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    [TestClass]
    public class ApiSentenceWordSplitTests
    {
        // The split as it was before the IndexOf, unchanged: the oracle.
        private static readonly Regex Oracle =
            new Regex("^=?(?<KEY>[^=]+)=(?<VALUE>.*)$", RegexOptions.Singleline);

        private static KeyValuePair<string, string>? OracleSplit(string word)
        {
            Match m = Oracle.Match(word);
            return m.Success
                ? new KeyValuePair<string, string>(m.Groups["KEY"].Value, m.Groups["VALUE"].Value)
                : (KeyValuePair<string, string>?)null;
        }

        private static IReadOnlyDictionary<string, string> Split(params string[] words)
            => new ApiReSentence(words).Words;

        private static void AssertSameAsOracle(string word)
        {
            var expected = OracleSplit(word);
            var actual = Split(word);
            if (expected == null)
            {
                Assert.AreEqual(0, actual.Count, "the oracle does not split " + Show(word) + ", so it must be dropped");
                return;
            }
            Assert.AreEqual(1, actual.Count, "one word, one entry: " + Show(word));
            var entry = actual.Single();
            Assert.AreEqual(expected.Value.Key, entry.Key, "key of " + Show(word));
            Assert.AreEqual(expected.Value.Value, entry.Value, "value of " + Show(word));
        }

        private static string Show(string word)
            => "\"" + word.Replace("\r", "\\r").Replace("\n", "\\n") + "\"";

        // ---- the review's cases, with the expected split spelled out ----

        [DataTestMethod]
        [DataRow("=a=b", "a", "b")]
        [DataRow("=a==b", "a", "=b")]
        [DataRow("=a=", "a", "")]
        [DataRow(".tag=1", ".tag", "1")]
        [DataRow("a=b", "a", "b")]
        [DataRow("=comment=line one\nline two", "comment", "line one\nline two")]
        public void AWordSplitsAtTheFirstEqualsAfterTheKey(string word, string key, string value)
        {
            var words = Split(word);
            Assert.AreEqual(1, words.Count);
            Assert.AreEqual(value, words[key]);
            AssertSameAsOracle(word);
        }

        [DataTestMethod]
        [DataRow("==x")]
        [DataRow("=")]
        [DataRow("==")]
        [DataRow("x")]
        [DataRow("")]
        [DataRow("=a")]
        [DataRow("!re")]
        public void AWordWithNoKeyIsDropped(string word)
        {
            Assert.AreEqual(0, Split(word).Count, Show(word));
            AssertSameAsOracle(word);
        }

        [TestMethod]
        public void ARepeatedKeyStillGetsTheNumberedName()
        {
            var words = Split("=trusted=true", "=trusted=false", "=trusted=");
            Assert.AreEqual("true", words["trusted"]);
            Assert.AreEqual("false", words["trusted2"]);
            Assert.AreEqual("", words["trusted3"]);
        }

        // ---- formatted values: the value comes back exactly as sent ----

        public static IEnumerable<object[]> FormattedValues()
        {
            yield return new object[] { "multi-line CRLF", "first line\r\nsecond line\r\n\r\nfourth" };
            yield return new object[] { "lone CR and LF", "a\rb\nc\r" };
            yield return new object[] { "value ending in LF", "ends with a newline\n" };
            yield return new object[] { "value is only LF", "\n" };
            yield return new object[] { "value is only CRLF", "\r\n" };
            yield return new object[] { "equals inside", "x=1 y=2" };
            yield return new object[] { "double equals inside", "a==b" };
            yield return new object[] { "starts with double equals", "==tail" };
            yield return new object[] { "only equals", "===" };
            yield return new object[] { "empty", "" };
            yield return new object[] { "spaces", "   " };
            yield return new object[] { "non-ASCII", "Příliš žluťoučký kůň úpěl ďábelské ódy — ünïcødé ✓ 日本語" };
            yield return new object[] { "surrogate pair", "emoji \U0001F600 here" };
            yield return new object[] { "tab and NUL", "a\tb\0c" };
            yield return new object[] { "escaped quotes and backslashes", "say \\\"hi\\\" \\\\ $var" };
            yield return new object[] { "RouterOS script", Script };
            yield return new object[] { "CRLF script", Script.Replace("\n", "\r\n") };
            yield return new object[] { "comment with markup", ";;; disabled by =admin= on 2026-11-10\r\n=== section ===" };
        }

        private const string Script =
            ":local name \"wan\"\n" +
            ":if ([/interface get [find name=$name] running] = true) do={\n" +
            "    :log info (\"up: \" . $name)\n" +
            "} else={ /ip route set [find comment=\"=backup=\"] disabled=no }\n" +
            ":foreach i in=[/ip address find] do={ :put [/ip address get $i address] }\n" +
            "# key=value lines: a=b==c\n";

        [DataTestMethod]
        [DynamicData(nameof(FormattedValues), DynamicDataSourceType.Method)]
        public void AFormattedValueComesBackByteForByte(string label, string value)
        {
            foreach (string key in new[] { "comment", "source", ".tag", "on-event", "x" })
            {
                foreach (string word in new[] { "=" + key + "=" + value, key + "=" + value })
                {
                    var words = Split(word);
                    Assert.AreEqual(1, words.Count, label + ": " + Show(word));
                    Assert.AreEqual(value, words[key], label + ": " + Show(word));
                    AssertSameAsOracle(word);
                }
            }
        }

        [TestMethod]
        public void AKeyNeverHoldsAnEquals()
        {
            // A key with a line break or non-ASCII in it is not something the router sends, but the split has
            // no business treating it differently from the regex either.
            foreach (string word in new[] { "=a\nb=c", "=ž=ý", "= =x", "=\r\n=\r\n", "\n=x", "=a b=c=d" })
                AssertSameAsOracle(word);
        }

        [TestMethod]
        public void EveryShortWordOverTheAwkwardAlphabetSplitsLikeTheRegex()
        {
            // Exhaustive up to length 5 over the characters that matter: '=', a key character, CR, LF, a
            // non-ASCII letter. 5^1 + … + 5^5 = 3905 words, including every arrangement of leading '='s.
            char[] alphabet = { '=', 'a', '\r', '\n', 'ř' };
            var sb = new StringBuilder();
            int checkedWords = 0;
            for (int length = 0; length <= 5; length++)
            {
                int combinations = (int)Math.Pow(alphabet.Length, length);
                for (int n = 0; n < combinations; n++)
                {
                    sb.Clear();
                    int rest = n;
                    for (int i = 0; i < length; i++)
                    {
                        sb.Append(alphabet[rest % alphabet.Length]);
                        rest /= alphabet.Length;
                    }
                    AssertSameAsOracle(sb.ToString());
                    checkedWords++;
                }
            }
            Assert.AreEqual(3906, checkedWords);
        }

        [TestMethod]
        public void RandomLongWordsSplitLikeTheRegex()
        {
            var random = new Random(20261110);
            string pool = "==aZ.-_\r\n \t\"\\$;:čřž✓";
            for (int i = 0; i < 20000; i++)
            {
                int length = random.Next(0, 64);
                var chars = new char[length];
                for (int j = 0; j < length; j++) chars[j] = pool[random.Next(pool.Length)];
                AssertSameAsOracle(new string(chars));
            }
        }
    }
}
