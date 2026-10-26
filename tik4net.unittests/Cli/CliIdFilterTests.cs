// CliIdFilterTests.cs — a '?.id=' filter whose value is not an id matches no row, as on the binary API.
//
// An item id is '*' and hex digits. The API compares '.id' with the value as given, so '?.id=ether1' answers
// nothing on every RouterOS version. The CLI resolves a NAME there from 7.25 on: over telnet,
//
//   :put [:len [/interface find where .id=ether1]]   -> 1 on 7.25, 0 on 7.21.5 and 6.49.13
//
// so a CLI read sending the value through answered with the row the API does not. The builder renders such a
// filter as the literal 'false', which matches no row on all three versions, and composes like any other
// predicate (measured, same three routers):
//
//   :put [:len [/interface find where false]]                      -> 0
//   :put [:len [/interface find where !(false)]]                   -> every interface
//   :put [:len [/interface find where (false || name="ether1")]]   -> 1

using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Connection;

namespace tik4net.unittests.Cli
{
    [TestClass]
    public class CliIdFilterTests
    {
        private static IList<ITikCommandParameter> Filters(params (string Name, string Value)[] items)
            => items
                .Select(i => (ITikCommandParameter)new TikCommandParameter(
                    i.Name, i.Value, TikCommandParameterFormat.Filter))
                .ToList();

        [TestMethod]
        public void ANameInAnIdFilter_MatchesNoRow()
        {
            Assert.AreEqual("false", CliCommandBuilder.BuildWhereClause(Filters((".id", "ether1"))));
        }

        [TestMethod]
        public void AnEmptyIdFilter_MatchesNoRow()
        {
            // '?.id=' asks for the row whose id is the empty string; no row has one.
            Assert.AreEqual("false", CliCommandBuilder.BuildWhereClause(Filters((".id", ""))));
        }

        [TestMethod]
        public void AnIdShapedValue_IsSentAsTheId()
        {
            Assert.AreEqual(".id=" + CliCommandBuilder.QuoteForWhere("*1A"),
                CliCommandBuilder.BuildWhereClause(Filters((".id", "*1A"))));
            Assert.AreEqual(".id=" + CliCommandBuilder.QuoteForWhere("*ff"),
                CliCommandBuilder.BuildWhereClause(Filters((".id", "*ff"))));
        }

        [TestMethod]
        public void AStarWithoutHexDigits_IsNotAnId()
        {
            Assert.AreEqual("false", CliCommandBuilder.BuildWhereClause(Filters((".id", "*"))));
            Assert.AreEqual("false", CliCommandBuilder.BuildWhereClause(Filters((".id", "*ether1"))));
        }

        [TestMethod]
        public void ANegatedNameFilter_MatchesEveryRow_AsOnTheApi()
        {
            Assert.AreEqual("!(false)",
                CliCommandBuilder.BuildWhereClause(Filters((".id", "ether1"), ("#!", null))));
        }

        [TestMethod]
        public void ANameFilterInsideAnOr_LeavesTheOtherPredicate()
        {
            Assert.AreEqual("(false || name=ether1)",
                CliCommandBuilder.BuildWhereClause(Filters((".id", "ether1"), ("name", "ether1"), ("#|", null))));
        }

        [TestMethod]
        public void AnIdFilterWithNoValue_StillAsksForAnyId()
        {
            // '?.id' is "the property has a value", not an equality; it is not touched.
            Assert.AreEqual(".id~\".\"", CliCommandBuilder.BuildWhereClause(Filters((".id", null))));
        }
    }
}
