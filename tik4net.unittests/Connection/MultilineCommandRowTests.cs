// MultilineCommandRowTests.cs — what a multi-line CommandText's rows turn into on a command transport.
//
// ITikCommand.CommandText may carry the whole sentence, one word per line:
//
//     conn.CreateCommand("/ip/address/print\n?address=10.0.0.1/24").ExecuteList()
//
// The binary API puts those words on the wire and lets the ROUTER judge them, so a typo comes back as a
// trap. Every other transport has to understand the row before it can build CLI text, a REST body or an M2
// field — and understanding it is where a row that makes no sense used to be dropped in silence, which is
// not an error and not a wrong value but a command missing a filter, answering with the whole table.
//
// TikCommandRow exists for exactly that reason and throws on a row it cannot parse. TikGenericCommand's
// multi-line handling was a second, lenient copy of the same parsing that did not.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;
using tik4net.Connection;

namespace tik4net.unittests.Connection
{
    [TestClass]
    public class MultilineCommandRowTests
    {
        /// <summary>A connection that answers no rows and remembers what it was asked.</summary>
        private sealed class RecordingConnection : TikCommandConnectionBase
        {
            public TikCommandDescriptor LastPrint;

            public override TikConnectionCapability Capabilities => TikConnectionCapability.Crud;

            protected override IList<TikRecordSentence> RunPrint(TikCommandDescriptor descriptor)
            {
                LastPrint = descriptor;
                return new List<TikRecordSentence>();
            }

            protected override string RunRawText(TikCommandDescriptor descriptor) => "";
            protected override string RunAdd(TikCommandDescriptor descriptor) => "*1";
            protected override void RunNonQuery(TikCommandDescriptor descriptor) { }

            public override void Open(string host, string user, string password) { }
            public override void Open(string host, int port, string user, string password) { }
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public override void Close() { }
        }

        private static TikCommandDescriptor Read(string commandText)
        {
            var conn = new RecordingConnection();
            conn.CreateCommand(commandText).ExecuteList();
            Assert.IsNotNull(conn.LastPrint);
            return conn.LastPrint;
        }

        // ── The rows that are fine keep working ───────────────────────────────

        [TestMethod]
        public void FilterAndNameValueRowsBecomeParameters()
        {
            var d = Read("/ip/address/print\n?address=10.0.0.1/24\n=comment=x");

            Assert.AreEqual("/ip/address/print", d.CommandText);

            var filter = d.Parameters.Single(p => p.Name == "address");
            Assert.AreEqual("10.0.0.1/24", filter.Value);
            Assert.AreEqual(TikCommandParameterFormat.Filter, filter.ParameterFormat);

            var nv = d.Parameters.Single(p => p.Name == "comment");
            Assert.AreEqual("x", nv.Value);
            Assert.AreEqual(TikCommandParameterFormat.NameValue, nv.ParameterFormat);
        }

        [TestMethod]
        public void ApiSentenceMarkersAreIgnoredWithoutComplaint()
        {
            // '.tag' and '.proplist' are words of the API's own protocol. These transports genuinely cannot
            // express them, and skipping them is the documented behaviour — not a failure to understand.
            var d = Read("/ip/address/print\n.tag=7\n.proplist=address\n?address=10.0.0.1/24");

            Assert.AreEqual(1, d.Parameters.Count, "only the filter survives");
            Assert.AreEqual("address", d.Parameters[0].Name);
        }

        [TestMethod]
        public void BlankRowsAreNotWords()
        {
            var d = Read("/ip/address/print\n\n  \n?address=10.0.0.1/24");
            Assert.AreEqual(1, d.Parameters.Count);
        }

        // ── The redundant filter form the API also accepts ────────────────────

        [TestMethod]
        public void TheRedundantFilterFormIsUnderstood()
        {
            // '?=name=value' is a spelling the binary API accepts alongside '?name=value'. The lenient
            // parser did not strip the extra '=', so it produced a parameter with an EMPTY NAME and the
            // whole of 'address=10.0.0.1/24' as its value — worse than dropping the row, because that goes
            // to the router as a real (nonsense) word rather than as nothing.
            var d = Read("/ip/address/print\n?=address=10.0.0.1/24");

            var p = d.Parameters.Single();
            Assert.AreEqual("address", p.Name);
            Assert.AreEqual("10.0.0.1/24", p.Value);
            Assert.AreEqual(TikCommandParameterFormat.Filter, p.ParameterFormat);
        }

        [TestMethod]
        public void ABareFilterKeepsItsHasValueMeaning()
        {
            // '?running' is the API's "this property is set", which has no value at all — distinct from
            // "equals the empty string". The lenient parser gave it Value = "", turning a presence test into
            // an equality test against nothing.
            var d = Read("/interface/print\n?running");

            var p = d.Parameters.Single();
            Assert.AreEqual("running", p.Name);
            Assert.IsNull(p.Value, "a bare filter has no value; \"\" would mean 'equals empty'");
        }

        // ── The rows that make no sense are refused, not dropped ──────────────

        [TestMethod]
        public void ARowWithNoPrefixIsRefused()
        {
            // One leading '=' short. This is the shape that used to send an add with no fields at all and
            // report success.
            var ex = Assert.ThrowsException<ArgumentException>(
                () => Read("/ip/address/add\naddress=10.0.0.1/24"));

            StringAssert.Contains(ex.Message, "address=10.0.0.1/24",
                "the error must name the row, or it cannot be found in a long command");
        }

        [TestMethod]
        public void ANameValueWithNoValueIsRefused()
        {
            // '=comment' is not 'set comment to empty' — that is '=comment='. Dropped in silence before.
            Assert.ThrowsException<ArgumentException>(
                () => Read("/ip/address/set\n=.id=*1\n=comment"));
        }

        [TestMethod]
        public void AFilterWithNoNameAtAllIsRefused()
        {
            // '?=' has nothing left after the redundant '=' is stripped, and '?==x' leaves an empty name
            // before the separator. Both are a filter with no property to filter on.
            Assert.ThrowsException<ArgumentException>(() => Read("/ip/address/print\n?="));
            Assert.ThrowsException<ArgumentException>(() => Read("/ip/address/print\n?==value"));
        }

        [TestMethod]
        public void TheRedundantFormOfABareFilterIsStillABareFilter()
        {
            // '?=running' is '?running' with the optional '=' the API also accepts — a presence test on
            // 'running', not an empty name. Worth pinning because it looks like the malformed case above
            // and is one character away from it.
            var d = Read("/interface/print\n?=running");

            var p = d.Parameters.Single();
            Assert.AreEqual("running", p.Name);
            Assert.IsNull(p.Value);
        }

        [TestMethod]
        public void AnExplicitlyEmptyValueIsStillAccepted()
        {
            // The counterpart to the two above: '=comment=' IS how you clear a field, and must not be
            // caught by the stricter parsing.
            var d = Read("/ip/address/set\n=.id=*1\n=comment=");

            var p = d.Parameters.Single(x => x.Name == "comment");
            Assert.AreEqual("", p.Value);
        }
    }
}
