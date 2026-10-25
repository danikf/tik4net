// ReadParameterFormatTests.cs — what a parameter of a READ turns out to be when the caller did not say.
//
// A parameter carries its own format, the COMMAND carries a default, and the use case carries one more.
// The binary API resolves them in that order. The transport-neutral command used to skip the middle step
// on a read and make every unformatted parameter a filter, whatever the caller had set on the command — so
//
//     conn.CreateCommandAndParameters("/interface/ethernet/print",
//         TikCommandParameterFormat.NameValue, "detail", "")
//
// went out as `where detail=""`, which matches no row. Not an error and not a wrong value: an empty table,
// measured over Telnet against the same parameter built with CreateParameter(..., NameValue), which
// returned two rows.

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
    public class ReadParameterFormatTests
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

        private static TikCommandParameterFormat FormatOf(TikCommandDescriptor d, string name)
            => d.Parameters.Single(p => p.Name == name).ParameterFormat;

        [TestMethod]
        public void TheCommandsOwnDefaultDecidesWhatAnUnformattedParameterIs()
        {
            var conn = new RecordingConnection();
            conn.CreateCommandAndParameters("/interface/ethernet/print",
                TikCommandParameterFormat.NameValue, "detail", "").ExecuteList();

            Assert.AreEqual(TikCommandParameterFormat.NameValue, FormatOf(conn.LastPrint, "detail"),
                "the caller said NameValue on the command; a read must not overrule that with Filter");
        }

        [TestMethod]
        public void WithNothingSaidAReadsParametersAreStillFilters()
        {
            // The fallback is what makes the short form a filtered read, and it has to keep working: it
            // applies only when the caller has said nothing at all.
            var conn = new RecordingConnection();
            conn.CreateCommandAndParameters("/interface/print", "name", "ether1").ExecuteList();

            Assert.AreEqual(TikCommandParameterFormat.Filter, FormatOf(conn.LastPrint, "name"));
        }

        [TestMethod]
        public void AParameterThatNamesItsOwnFormatKeepsIt()
        {
            // Most specific wins, whatever the command says.
            var conn = new RecordingConnection();
            var cmd = conn.CreateCommand("/interface/print",
                conn.CreateParameter("name", "ether1", TikCommandParameterFormat.Filter),
                conn.CreateParameter("detail", "", TikCommandParameterFormat.NameValue));
            cmd.DefaultParameterFormat = TikCommandParameterFormat.NameValue;
            cmd.ExecuteList();

            Assert.AreEqual(TikCommandParameterFormat.Filter, FormatOf(conn.LastPrint, "name"));
            Assert.AreEqual(TikCommandParameterFormat.NameValue, FormatOf(conn.LastPrint, "detail"));
        }

        [DataTestMethod]
        [DataRow(TikCommandParameterFormat.NameValue)]
        [DataRow(TikCommandParameterFormat.Filter)]
        public void TheApiSentenceMarkersHaveTheirOwnFormatWhateverTheCommandDefault(TikCommandParameterFormat commandDefault)
        {
            // '.proplist' is the field list and '.tag' a tag, whatever the command is defaulted to: a command default
            // of Filter must not turn the field list into a filter on a property named '.proplist' (X13).
            var conn = new RecordingConnection();
            conn.CreateCommandAndParameters("/interface/print", commandDefault,
                TikSpecialProperties.Proplist, "name", TikSpecialProperties.Tag, "7").ExecuteList();

            Assert.AreEqual(TikCommandParameterFormat.NameValue,
                FormatOf(conn.LastPrint, TikSpecialProperties.Proplist));
            Assert.AreEqual(TikCommandParameterFormat.Tag,
                FormatOf(conn.LastPrint, TikSpecialProperties.Tag));
        }
    }
}
