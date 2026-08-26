// CommandRowFilterTest.cs — the two low-level filter forms that used to be dropped before they were sent.
//
// The rows go in as a multi-line CommandText — the whole sentence, one word per line — which is where a
// caller hands these transports raw API rows and where they are parsed (TikCommandRow) into the query the
// transport then builds. Two forms carry no value and so fell through a "no '=' means nothing" rule: the
// API's bare `?name` ("this property is set") and the postfix query-stack operators `?#|` / `?#&` / `?#!`.
// Both were understood by the layers below and never reached them.
//
// A dropped filter is not a smaller answer — it is a BIGGER one, the whole table where the caller asked
// for part of it, reported as success.
//
// This is deliberately NOT CallCommandSync, which it used to be: that level takes the transport's own
// language now (CLI text on a terminal), so a test written in API rows could only run on the binary API.
// Multi-line CommandText carries the same rows through the same parser on all eleven transports.

using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;

namespace tik4net.integrationtests
{
    [TestClass]
    public class CommandRowFilterTest : TestBase
    {
        private int CountOf(params string[] rows)
            => Connection.CreateCommand(string.Join("\n", rows)).ExecuteList().Count();

        /// <summary>
        /// <c>?name</c> asks for the rows that HAVE the property. Every interface on the lab router has a
        /// name and none of them is missing one, so the shape is checked against the same read without the
        /// filter rather than against a number that depends on the router's configuration.
        /// </summary>
        [TestMethod]
        public void ABareFilterAsksWhetherThePropertyIsSet()
        {
            int all = CountOf("/interface/print");
            Assert.IsTrue(all > 0, "the lab router must have interfaces for this to mean anything");

            Assert.AreEqual(all, CountOf("/interface/print", "?name"),
                "every interface has a name");
        }

        /// <summary>
        /// The same form, on a property most rows do NOT have: a comment. It must return fewer rows than
        /// the unfiltered read — which is the half a dropped filter cannot fake.
        /// </summary>
        [TestMethod]
        public void ABareFilterOnAnUnsetPropertyReturnsFewerRows()
        {
            int all = CountOf("/interface/print");
            int commented = CountOf("/interface/print", "?comment");

            Assert.IsTrue(commented < all,
                $"a bare '?comment' returned {commented} of {all} rows — a filter that reached nothing "
                + "returns them all");
        }

        /// <summary>
        /// The query stack: two predicates and an OR. Dropping the operator left them to be ANDed, which
        /// is a different question with a different answer.
        /// </summary>
        [TestMethod]
        public void TheQueryStackOperatorIsApplied()
        {
            int either = CountOf("/interface/print", "?type=ether", "?type=loopback", "?#|");
            int ether = CountOf("/interface/print", "?type=ether");
            int loopback = CountOf("/interface/print", "?type=loopback");

            Assert.AreEqual(ether + loopback, either,
                "an OR of two disjoint predicates is the sum; an AND of them is none");
            Assert.IsTrue(either > 0, "the lab router has both kinds");
        }
    }
}
