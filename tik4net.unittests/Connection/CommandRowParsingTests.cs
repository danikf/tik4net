// CommandRowParsingTests.cs — a command row that makes no sense is an error, not a dropped parameter.
//
// The binary API puts a sentence row on the wire verbatim and lets the ROUTER judge it, so a malformed one
// comes back as a trap. Every other transport has to understand the row before it can build CLI text, a
// REST body or an M2 field — and understanding it was where a row that made no sense used to vanish:
//
//     conn.CallCommandSync("/ip/address/add", "address=10.0.0.1/24")   // one leading '=' short
//
// sent an add with no fields at all and reported success. One typo, loud on one transport and invisible on
// the other ten.
//
// Two rows were being dropped that are not typos at all: the API's bare `?name` ("this property is set")
// and the query-stack operators `?#|` / `?#&` / `?#!`, both of which the layers below already understand.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;
using tik4net.Connection;

namespace tik4net.unittests.Connection
{
    [TestClass]
    public class CommandRowParsingTests
    {
        private static ITikCommandParameter One(string row)
        {
            var parsed = TikCommandRow.ParseParameters(new List<string> { row }, 0);
            Assert.AreEqual(1, parsed.Count, "expected exactly one parameter from " + row);
            return parsed[0];
        }

        // ── the forms that exist ──────────────────────────────────────────────

        [TestMethod]
        public void ANameValueRowIsANameValueParameter()
        {
            var p = One("=address=10.0.0.1/24");
            Assert.AreEqual("address", p.Name);
            Assert.AreEqual("10.0.0.1/24", p.Value);
            Assert.AreEqual(TikCommandParameterFormat.NameValue, p.ParameterFormat);
        }

        [TestMethod]
        public void AnEmptyValueIsAValueAndNotAMissingOne()
        {
            // '=name=' is how the API clears a field, and it must stay distinguishable from '=name'.
            var p = One("=comment=");
            Assert.AreEqual("comment", p.Name);
            Assert.AreEqual("", p.Value);
        }

        [TestMethod]
        public void AFilterRowIsAFilterParameter()
        {
            var p = One("?disabled=yes");
            Assert.AreEqual("disabled", p.Name);
            Assert.AreEqual("yes", p.Value);
            Assert.AreEqual(TikCommandParameterFormat.Filter, p.ParameterFormat);
        }

        [TestMethod]
        public void TheRedundantFilterFormIsAcceptedToo()
        {
            // '?=name=value' is a form the binary API also accepts; it means the same thing.
            var p = One("?=disabled=yes");
            Assert.AreEqual("disabled", p.Name);
            Assert.AreEqual("yes", p.Value);

            // And it composes with the bare form: '?=name' is '?name'.
            var bare = One("?=comment");
            Assert.AreEqual("comment", bare.Name);
            Assert.IsNull(bare.Value);
        }

        [TestMethod]
        public void ABareFilterIsThePropertyIsSetPredicate()
        {
            // The API's '?name' with no value — "this property is set". It has no value BY DESIGN, and the
            // layers below already read a null value that way: the CLI spells it as the bare field name in
            // a where-clause, and the native in-memory query stack tests existence. Dropping it turned a
            // real filter into no filter and answered with the whole table.
            var p = One("?comment");
            Assert.AreEqual("comment", p.Name);
            Assert.IsNull(p.Value);
            Assert.AreEqual(TikCommandParameterFormat.Filter, p.ParameterFormat);
        }

        [TestMethod]
        public void TheQueryStackOperatorsSurvive()
        {
            // '?#|', '?#&', '?#!' are the API's postfix query stack. They carry no value, so the old
            // "no '=' means nothing" rule threw all three away — leaving an OR of two predicates to be
            // evaluated as an AND by whatever came after.
            var parsed = TikCommandRow.ParseParameters(
                new List<string> { "?disabled=yes", "?comment=x", "?#|" }, 0);

            Assert.AreEqual(3, parsed.Count);
            Assert.AreEqual("#|", parsed[2].Name);
            Assert.IsNull(parsed[2].Value);
        }

        // ── the markers that are skipped, and everything else that is not ─────

        [TestMethod]
        public void AnApiSentenceMarkerIsSkippedSilently()
        {
            // '.tag' and '.proplist' are words of the API's own protocol which these transports cannot
            // express, and are documented as ignored — not a row anyone got wrong.
            foreach (string marker in new[] { ".tag=7", ".proplist=name,comment", ".cli-stats=" })
                Assert.AreEqual(0, TikCommandRow.ParseParameters(new List<string> { marker }, 0).Count, marker);
        }

        [TestMethod]
        public void ARowThatIsNeitherIsRefused()
        {
            foreach (string row in new[]
            {
                "address=10.0.0.1/24",   // the leading '=' forgotten — the case this is all about
                "=address",              // a name-value with no value
                "==value",               // a name-value with no name
                "?",                     // a filter with no property
                "?=",                    // the same in the redundant form
                "",                      // not a word at all
            })
            {
                try
                {
                    TikCommandRow.ParseParameters(new List<string> { row }, 0);
                    Assert.Fail($"'{row}' was accepted; a row nothing can act on must not be dropped");
                }
                catch (ArgumentException ex)
                {
                    StringAssert.Contains(ex.Message, "'" + row + "'",
                        "the message must name the row the caller actually wrote");
                }
            }
        }

        [TestMethod]
        public void TheCommandTextItselfIsNotParsedAsARow()
        {
            // startIndex 1 is the ordinary call shape: row 0 is '/ip/address/add', which is not a
            // parameter and must not be judged as one.
            var parsed = TikCommandRow.ParseParameters(
                new List<string> { "/ip/address/add", "=address=10.0.0.1/24" }, 1);

            Assert.AreEqual(1, parsed.Count);
            Assert.AreEqual("address", parsed[0].Name);
        }
    }
}
