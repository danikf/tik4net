// CliQueryStackTests.cs — the filters of a read are a postfix STACK, not a list of things to AND.
//
// The API's query words '?#|', '?#&' and '?#!' combine the predicates before them. The CLI builder joined
// everything with '&&', so `?type=ether ?type=loopback ?#|` — "either kind" — went out as
// `where type=ether && type=loopback`, which no row can satisfy. The caller asked for three interfaces and
// got none, reported as success.
//
// The WinBox-native transport has always evaluated the same stack in memory (TikQueryStack); this is the
// same evaluation rendered as text, so a query means one thing on every transport.
//
// All three renderings verified against RouterOS 7.24 over telnet, on a router with two ethers and a
// loopback:
//
//   :put [/interface print as-value where (type=ether || type=loopback)]  -> ether1, ether2, lo
//   :put [/interface print as-value where !(type=ether)]                  -> lo
//   :put [/interface print as-value where type=ether]                     -> ether1, ether2

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;
using tik4net.Cli;
using tik4net.Connection;

namespace tik4net.unittests.Cli
{
    [TestClass]
    public class CliQueryStackTests
    {
        private static IList<ITikCommandParameter> Filters(params (string Name, string Value)[] items)
            => items
                .Select(i => (ITikCommandParameter)new TikCommandParameter(
                    i.Name, i.Value, TikCommandParameterFormat.Filter))
                .ToList();

        [TestMethod]
        public void TwoPredicatesAndAnOrBecomeAnOr()
        {
            string where = CliCommandBuilder.BuildWhereClause(
                Filters(("type", "ether"), ("type", "loopback"), ("#|", null)));

            Assert.AreEqual("(type=ether || type=loopback)", where);
        }

        [TestMethod]
        public void AnExplicitAndIsRenderedToo()
        {
            string where = CliCommandBuilder.BuildWhereClause(
                Filters(("type", "ether"), ("running", "true"), ("#&", null)));

            Assert.AreEqual("(type=ether && running=true)", where);
        }

        [TestMethod]
        public void NegationTakesTheOnePredicateBeforeIt()
        {
            string where = CliCommandBuilder.BuildWhereClause(
                Filters(("type", "ether"), ("#!", null)));

            Assert.AreEqual("!(type=ether)", where);
        }

        [TestMethod]
        public void TheStackNests()
        {
            // ?a ?b ?#| ?c ?#& → ((a || b) && c), the same shape the API's own stack builds.
            string where = CliCommandBuilder.BuildWhereClause(
                Filters(("type", "ether"), ("type", "loopback"), ("#|", null),
                        ("running", "true"), ("#&", null)));

            Assert.AreEqual("((type=ether || type=loopback) && running=true)", where);
        }

        [TestMethod]
        public void WithNoOperatorsAtAllEverythingIsStillAnded()
        {
            // The overwhelmingly common case, and the one that must not change: a plain filtered read.
            string where = CliCommandBuilder.BuildWhereClause(
                Filters(("type", "ether"), ("running", "true")));

            Assert.AreEqual("type=ether && running=true", where);
        }

        [TestMethod]
        public void ALeftoverOperandIsAndedOntoTheResult()
        {
            // ?a ?b ?#| ?c — the stack is left holding two things, which means the same as writing them
            // side by side.
            string where = CliCommandBuilder.BuildWhereClause(
                Filters(("type", "ether"), ("type", "loopback"), ("#|", null), ("running", "true")));

            Assert.AreEqual("(type=ether || type=loopback) && running=true", where);
        }

        [TestMethod]
        public void AnOperatorWithNothingUnderItIsRefused()
        {
            // Guessing would send the router a clause the caller did not write. The operands come first.
            foreach (string op in new[] { "#|", "#&", "#!" })
            {
                try
                {
                    CliCommandBuilder.BuildWhereClause(Filters((op, null)));
                    Assert.Fail($"'?{op}' with an empty stack was accepted");
                }
                catch (ArgumentException ex)
                {
                    StringAssert.Contains(ex.Message, op);
                }
            }
        }

        [TestMethod]
        public void AStackWordThisDoesNotImplementIsLeftAlone()
        {
            // Not turned into a predicate on a property called '#x' — the router has no such property, and
            // inventing one would answer with nothing rather than saying it did not understand.
            string where = CliCommandBuilder.BuildWhereClause(
                Filters(("type", "ether"), ("#x", null)));

            Assert.AreEqual("type=ether", where);
        }
    }
}
