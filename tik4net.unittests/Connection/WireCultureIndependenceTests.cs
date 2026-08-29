using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// RouterOS speaks one fixed wire format. Nothing this library reads from it or writes to it may depend
    /// on the regional settings of the machine the caller happens to run on.
    /// </summary>
    /// <remarks>
    /// <b>Why a second culture test.</b> <c>Objects.CultureIndependenceTests</c> already covers the O/R
    /// mapper's property accessor, and it is why that layer is invariant throughout. But it exercises one
    /// layer and one axis — the <i>negative sign</i>, via sv-SE — and a green result there says nothing
    /// about the layer below it. <see cref="TikQueryStack"/> sat under that test comparing numbers with the
    /// thread's culture for as long as it existed.
    /// <para>
    /// So this class covers the wire layer, and covers the <b>decimal separator</b> (cs-CZ) and <b>casing</b>
    /// (tr-TR) axes that the mapper test does not. Three axes, because each breaks different code: the sign
    /// breaks formatting, the separator breaks number parsing, and the Turkish dotless i breaks any
    /// <c>ToLower</c> used to normalise a protocol token.
    /// </para>
    /// </remarks>
    [TestClass]
    public class WireCultureIndependenceTests
    {
        /// <summary>Runs <paramref name="body"/> under <paramref name="cultureName"/>, always restoring.</summary>
        private static void InCulture(string cultureName, Action body)
        {
            CultureInfo previousCulture = Thread.CurrentThread.CurrentCulture;
            CultureInfo previousUi = Thread.CurrentThread.CurrentUICulture;
            try
            {
                var culture = CultureInfo.GetCultureInfo(cultureName);
                Thread.CurrentThread.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
                body();
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previousCulture;
                Thread.CurrentThread.CurrentUICulture = previousUi;
            }
        }

        /// <summary>The cultures that actually break things, rather than a list that looks thorough.</summary>
        private static readonly string[] HostileCultures = { "cs-CZ", "de-DE", "tr-TR", "sv-SE" };

        private static TikRecordSentence Row(params (string Name, string Value)[] fields)
            => new TikRecordSentence(fields.ToDictionary(f => f.Name, f => f.Value, StringComparer.Ordinal));

        private static IReadOnlyList<ITikCommandParameter> Filters(params (string Name, string Value)[] items)
            => items
                .Select(i => (ITikCommandParameter)new TikCommandParameter(
                    i.Name, i.Value, TikCommandParameterFormat.Filter))
                .ToList();

        /// <summary>
        /// The regression this class was written for.
        /// </summary>
        /// <remarks>
        /// <c>?&gt;rate=9.5</c> against a row holding <c>10.5</c> is true — 10.5 is the larger number. Parsed
        /// with the current culture, both <c>double.TryParse</c> calls fail on a machine whose decimal
        /// separator is a comma, the evaluator falls through to its ordinal string comparison, and
        /// <c>"10.5"</c> sorts <i>before</i> <c>"9.5"</c> because '1' is below '9'. The same query against
        /// the same router then returns a different set of rows in Prague than in London, with no error
        /// anywhere.
        /// <para>
        /// The fractional value matters: an integer parses under every culture, so a test using one would
        /// pass against the unfixed code and prove nothing.
        /// </para>
        /// </remarks>
        [TestMethod]
        public void AGreaterThanQueryComparesNumbersTheSameWayInEveryCulture()
        {
            foreach (string cultureName in HostileCultures)
            {
                InCulture(cultureName, () =>
                {
                    Assert.IsTrue(
                        TikQueryStack.Matches(Row(("rate", "10.5")), Filters((">rate", "9.5"))),
                        "10.5 > 9.5 under " + cultureName + " — a decimal separator is not a regional "
                        + "preference when it arrives from a router");

                    Assert.IsFalse(
                        TikQueryStack.Matches(Row(("rate", "9.5")), Filters((">rate", "10.5"))),
                        "9.5 > 10.5 must stay false under " + cultureName);
                });
            }
        }

        /// <summary>An equality filter is ordinal by design and must not acquire a collation.</summary>
        [TestMethod]
        public void AnEqualityQueryStaysOrdinalInEveryCulture()
        {
            foreach (string cultureName in HostileCultures)
            {
                InCulture(cultureName, () =>
                {
                    Assert.IsTrue(TikQueryStack.Matches(Row(("name", "ether1")), Filters(("name", "ether1"))));
                    Assert.IsFalse(TikQueryStack.Matches(Row(("name", "ETHER1")), Filters(("name", "ether1"))),
                        "RouterOS field values are case-sensitive; " + cultureName
                        + " must not make them otherwise");
                });
            }
        }

        /// <summary>
        /// A duration is a wire value in both directions.
        /// </summary>
        /// <remarks>
        /// <b>This one passes against the unfixed code, and the honest reading is that it had to.</b>
        /// <see cref="TikTimeHelper"/> did format its numbers through the current culture and normalise its
        /// input with a culture-sensitive <c>ToLower()</c> — but a duration's alphabet is <c>w d h m s</c>
        /// and its digits are 0-9, so neither the Turkish dotless i nor a comma separator has anything to
        /// bite on. Those calls were made invariant because a fixed wire format has no business reading the
        /// caller's regional settings at all, not because a user was hitting it.
        /// <para>
        /// Kept as a pin: it costs nothing and it fails if someone later widens the format to a token where
        /// the difference does show.
        /// </para>
        /// </remarks>
        [TestMethod]
        public void ADurationRoundTripsIdenticallyInEveryCulture()
        {
            const int Seconds = 90061;   // 1d 1h 1m 1s
            string expected = TikTimeHelper.ToTikTime(Seconds);

            foreach (string cultureName in HostileCultures)
            {
                InCulture(cultureName, () =>
                {
                    Assert.AreEqual(expected, TikTimeHelper.ToTikTime(Seconds),
                        "the wire spelling of a duration changed under " + cultureName);
                    Assert.AreEqual(Seconds, TikTimeHelper.FromTikTimeToSeconds(expected),
                        "a duration the router sent did not read back under " + cultureName);
                    Assert.AreEqual(Seconds, TikTimeHelper.FromTikTimeToSeconds(expected.ToUpperInvariant()),
                        "an upper-cased duration did not read back under " + cultureName
                        + " (tr-TR lower-cases the ASCII capital I to a dotless small i)");
                });
            }
        }

        /// <summary>
        /// A pin, not evidence: the value types were written invariant from the start and pass against the
        /// unfixed code too. Stated so, because a test whose result could not have differed proves nothing
        /// about the fix — it only stops the next change from undoing what is already right.
        /// </summary>
        [TestMethod]
        public void TheValueTypesParseAndFormatIdenticallyInEveryCulture()
        {
            foreach (string cultureName in HostileCultures)
            {
                InCulture(cultureName, () =>
                {
                    Assert.IsTrue(TikDataRate.TryParse("1.5M", out TikDataRate rate));
                    Assert.AreEqual(1500000L, rate.Value);
                    Assert.AreEqual("1500000", rate.ToString());

                    Assert.IsTrue(TikDuration.TryParse("1d1h1m1s", out TikDuration duration));
                    Assert.AreEqual("1d1h1m1s", duration.ToString());
                });
            }
        }
    }
}
