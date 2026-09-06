using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;
using tik4net.Api;
using tik4net.Connection;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// <see cref="ITikSentence.Tag"/>, <see cref="ITikTrapSentence.CategoryCode"/> and
    /// <see cref="ITikTrapSentence.CategoryDescription"/> are declared plain <c>string</c>. This pins that
    /// <b>every</b> implementation actually upholds that, on the object a caller really receives.
    /// </summary>
    /// <remarks>
    /// <see cref="FakeSentenceContractTests"/> covers the same ground for the testing package, and its
    /// remarks say why a null from behind a non-nullable signature is worse than a missing value. What that
    /// class could not do is check the production types — it asserted in prose that "every real
    /// implementation upholds that" while pinning only the fake, and
    /// <see cref="TikTrapSentenceResult"/> did not: it passed <c>null!</c> straight through, so nine of the
    /// eleven transports handed a null <c>CategoryCode</c> to the caller's error callback.
    /// <para>
    /// <see cref="EverySentenceImplementationIsCoveredHere"/> is the part that keeps this honest. Listing
    /// types by hand is how the gap appeared in the first place, so a new sentence type fails the suite
    /// until it is named here.
    /// </para>
    /// </remarks>
    [TestClass]
    public class SentenceNonNullContractTests
    {
        /// <summary>Every concrete sentence, built the way its own transport builds it.</summary>
        private static IEnumerable<(string Name, ITikSentence Sentence)> AllSentences()
        {
            // API — words as they arrive off the wire, with nothing optional supplied.
            yield return ("ApiReSentence", new ApiReSentence(new[] { "=name=ether1" }));
            yield return ("ApiDoneSentence", new ApiDoneSentence(Array.Empty<string>()));
            yield return ("ApiTrapSentence", new ApiTrapSentence(new[] { "=message=no such item" }));
            yield return ("ApiFatalSentence", new ApiFatalSentence(new[] { "connection lost" }));

            // Transport-neutral — what the CLI, REST and WinBox-native paths produce.
            yield return ("TikRecordSentence", new TikRecordSentence(new Dictionary<string, string>()));
            yield return ("TikDoneSentenceResult", new TikDoneSentenceResult());
            yield return ("TikTrapSentenceResult", new TikTrapSentenceResult("no such item"));

            // Testing package.
            yield return ("TikFakeReSentence", new TikFakeReSentence(new Dictionary<string, string>()));
            yield return ("TikFakeDoneSentence", new TikFakeDoneSentence());
            yield return ("TikFakeTrapSentence", new TikFakeTrapSentence("no such item"));
        }

        [TestMethod]
        public void NoSentenceHandsOutANullTag()
        {
            var offenders = AllSentences()
                .Where(x => x.Sentence.Tag == null)
                .Select(x => x.Name)
                .ToList();

            Assert.AreEqual(0, offenders.Count,
                "ITikSentence.Tag is declared non-nullable; these return null: " + string.Join(", ", offenders));
        }

        [TestMethod]
        public void NoTrapHandsOutANullCategory()
        {
            var offenders = new List<string>();

            foreach (var (name, sentence) in AllSentences())
            {
                if (!(sentence is ITikTrapSentence trap))
                    continue;

                if (trap.CategoryCode == null)
                    offenders.Add(name + ".CategoryCode");
                if (trap.CategoryDescription == null)
                    offenders.Add(name + ".CategoryDescription");
                if (trap.Message == null)
                    offenders.Add(name + ".Message");
            }

            Assert.AreEqual(0, offenders.Count,
                "ITikTrapSentence declares all three non-nullable; these return null: "
                + string.Join(", ", offenders));
        }

        [TestMethod]
        public void ATrapWithNoCategoryReadsTheSameOnEveryTransport()
        {
            // ApiTrapSentence defines what "the router did not say" looks like: code "-1", description
            // "category not provided". A transport that has no category at all must answer the same, or the
            // caller's error handling has to branch on which transport produced the trap - which is the one
            // thing the transport-neutral sentence model exists to avoid.
            var api = new ApiTrapSentence(new[] { "=message=no such item" });
            var neutral = new TikTrapSentenceResult("no such item");

            Assert.AreEqual(api.CategoryCode, neutral.CategoryCode, "category code for a trap with no category");
            Assert.AreEqual(api.CategoryDescription, neutral.CategoryDescription,
                "category description for a trap with no category");
        }

        [TestMethod]
        public void EverySentenceImplementationIsCoveredHere()
        {
            var covered = new HashSet<string>(AllSentences().Select(x => x.Name), StringComparer.Ordinal);

            var implementations = new[] { typeof(ITikSentence).Assembly, typeof(TikFakeReSentence).Assembly }
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && !t.IsAbstract && typeof(ITikSentence).IsAssignableFrom(t))
                .Select(t => t.Name)
                .ToList();

            var missing = implementations.Where(n => !covered.Contains(n)).OrderBy(n => n).ToList();

            Assert.AreEqual(0, missing.Count,
                "these ITikSentence implementations are not exercised by this class, so their non-null "
                + "contract is unchecked: " + string.Join(", ", missing));
        }
    }
}
