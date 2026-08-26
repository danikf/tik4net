using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// Pins what <see cref="TikConnectionCapability.RawCommand"/> means and who has it:
    /// <b>a command written in the transport's own language</b>, sent without translation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ITikRawSentenceConnection.CallCommandSync(string[])"/> is documented as taking a command
    /// "in connection-specific format", and that is the whole point of the low-level path: it exists to reach
    /// what the O/R mapper cannot express. On the binary API the connection-specific format is API sentences.
    /// On a terminal it is RouterOS CLI text — so a CLI connection must send the line as typed, not rewrite it.
    /// </para>
    /// <para>
    /// The flag therefore tracks "does this transport have a language a caller can write", which is the same
    /// question <see cref="TikConnectionCapability.RawCommand"/> answers at the <c>ITikCommand</c> level, and
    /// the two have the same distribution: the binary API and the CLI family have it, REST and native WinBox
    /// do not — neither has a command language of its own, only a request shape. A base class implementing
    /// the interface for every transport by translating API rows would look like support and behave like the
    /// mapper, which is what these tests exist to prevent coming back.
    /// </para>
    /// </remarks>
    [TestClass]
    public class RawSentenceCapabilityTests
    {
        // ── The terminal double ───────────────────────────────────────────────

        /// <summary>
        /// A <see cref="CliConnectionBase"/> whose transport is a recording delegate, so a test can read the
        /// exact CLI text the connection put on the wire.
        /// </summary>
        private sealed class RecordingCliConnection : CliConnectionBase
        {
            public readonly List<string> Sent = new List<string>();
            public string Reply = string.Empty;

            protected override string TransportName => "Recording";

            public void OpenScripted()
                => OpenWith(_ => Task.FromResult(0),
                    (cliText, ct) => { Sent.Add(cliText); return Task.FromResult(Reply); },
                    (rawBytes, ct) => Task.FromResult(string.Empty),
                    () => { });

            public override void Open(string host, string user, string password) => OpenScripted();
            public override void Open(string host, int port, string user, string password) => OpenScripted();
            public override Task OpenAsync(string host, string user, string password) { OpenScripted(); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password) { OpenScripted(); return Task.FromResult(0); }
        }

        private static RecordingCliConnection OpenConnection(string reply = "")
        {
            var conn = new RecordingCliConnection { Reply = reply };
            conn.OpenScripted();
            return conn;
        }

        // ── The CLI dialect is CLI text, sent verbatim ────────────────────────

        [TestMethod]
        public void CliRawCall_SendsTheLineExactlyAsTyped()
        {
            using (var conn = OpenConnection(".id=*1;name=ether1;mtu=1500"))
            {
                conn.CallCommandSync(":put [/interface print as-value]").ToList();

                Assert.AreEqual(1, conn.Sent.Count);
                Assert.AreEqual(":put [/interface print as-value]", conn.Sent[0],
                    "raw means verbatim — no path rewriting, no where-building, no proplist");
            }
        }

        [TestMethod]
        public void CliRawCall_DoesNotAcceptApiSentenceSyntax()
        {
            // The defining property of the native contract: an API path is NOT translated into CLI. If this
            // ever starts passing '/interface print' to the router, the raw path has silently become the
            // mapper again and the caller's own syntax is no longer what runs.
            using (var conn = OpenConnection())
            {
                conn.CallCommandSync("/interface/print").ToList();

                Assert.AreEqual("/interface/print", conn.Sent[0],
                    "the row is sent as written; tik4net does not rewrite it into CLI form");
            }
        }

        [TestMethod]
        public void CliRawCall_JoinsRowsIntoOneCommandLine()
        {
            using (var conn = OpenConnection())
            {
                conn.CallCommandSync(":put [/interface print", "as-value", "where name=ether1]").ToList();

                Assert.AreEqual(":put [/interface print as-value where name=ether1]", conn.Sent[0],
                    "several rows are one command line joined by a space, so a caller may split it or not");
            }
        }

        // ── …and the reply still arrives as sentences ─────────────────────────

        [TestMethod]
        public void CliRawCall_ParsesAsValueOutputIntoSentences()
        {
            using (var conn = OpenConnection(".id=*1;name=ether1;mtu=1500"))
            {
                var sentences = conn.CallCommandSync(":put [/interface print as-value]").ToList();

                var re = sentences.OfType<ITikReSentence>().ToList();
                Assert.AreEqual(1, re.Count);
                Assert.AreEqual("ether1", re[0].GetResponseField("name"));
                Assert.AreEqual("1500", re[0].GetResponseField("mtu"));
                Assert.AreEqual(1, sentences.OfType<ITikDoneSentence>().Count(),
                    "a record read ends with !done, the same shape the binary API returns");
            }
        }

        [TestMethod]
        public void CliRawCall_ReturnsNonRecordOutputAsText()
        {
            // /export, :put of a scalar, and anything else without as-value structure. Inventing rows from
            // free text would be worse than handing it back — the caller asked for this exact command.
            using (var conn = OpenConnection("MikroTik"))
            {
                var sentences = conn.CallCommandSync(":put [/system identity get name]").ToList();

                Assert.AreEqual(0, sentences.OfType<ITikReSentence>().Count());
                var done = sentences.OfType<ITikDoneSentence>().Single();
                Assert.AreEqual("MikroTik", done.GetResponseWord());
            }
        }

        [TestMethod]
        public void CliRawCall_RaisesRouterErrorsAsTraps()
        {
            using (var conn = OpenConnection("expected end of command (line 1 column 20)"))
            {
                // The concrete type is whatever CliErrorParser classifies the text as (here
                // TikNoSuchCommandException); what matters is that it is a trap and not a successful ret word.
                var ex = Assert.ThrowsException<TikNoSuchCommandException>(
                    () => conn.CallCommandSync("/interface prnt").ToList(),
                    "a router error in raw output must throw, not be returned as a successful ret word");
                Assert.IsInstanceOfType(ex, typeof(TikCommandTrapException));
            }
        }

        [TestMethod]
        public void CliRawCall_RejectsAnEmptyCommand()
        {
            using (var conn = OpenConnection())
            {
                Assert.ThrowsException<ArgumentException>(() => conn.CallCommandSync().ToList());
                Assert.ThrowsException<ArgumentException>(() => conn.CallCommandSync("   ").ToList());
            }
        }

        [TestMethod]
        public void CliRawCall_RequiresAnOpenConnection()
        {
            var conn = new RecordingCliConnection();
            Assert.ThrowsException<TikConnectionNotOpenException>(
                () => conn.CallCommandSync(":put [/system identity get name]").ToList());
        }

        // ── Flag and interface answer the same question, everywhere ───────────

        [TestMethod]
        public void RawSentencesIsDeclaredExactlyWhereTheInterfaceIsImplemented()
        {
            // Whatever the answer is for a given transport, Supports() and the interface must agree — a flag
            // that says less than the interface offers makes the documented guard skip working calls, and one
            // that says more makes it attempt calls that throw.
            foreach (TikConnectionType type in Enum.GetValues(typeof(TikConnectionType)))
            {
                if (type == TikConnectionType.Ssh) continue; // satellite package, not referenced here

                using (var conn = ConnectionFactory.CreateConnection(type))
                {
                    bool flag = conn.Supports(TikConnectionCapability.RawCommand);
                    bool iface = conn is ITikRawSentenceConnection;

                    Assert.AreEqual(iface, flag,
                        $"{type}: declares RawSentences={flag} but implements ITikRawSentenceConnection={iface}");
                }
            }
        }

        [TestMethod]
        public void TransportsWithoutACommandLanguageDoNotOfferRawSentences()
        {
            // REST speaks HTTP requests and native WinBox speaks numeric M2 fields. Neither has a command
            // language a caller could write, so neither claims the capability — rather than accepting API rows
            // and translating them, which would be the mapper wearing the raw path's name.
            foreach (var type in new[] { TikConnectionType.Rest, TikConnectionType.RestSsl,
                                         TikConnectionType.WinboxNative, TikConnectionType.WinboxNativeMac })
            {
                using (var conn = ConnectionFactory.CreateConnection(type))
                {
                    Assert.IsFalse(conn.Supports(TikConnectionCapability.RawCommand), type.ToString());
                    Assert.IsFalse(conn is ITikRawSentenceConnection, type.ToString());
                    Assert.ThrowsException<TikConnectionCapabilityNotSupportedException>(
                        () => conn.CallCommandSync("/interface/print"), type.ToString());
                }
            }
        }

        [TestMethod]
        public void RawSentencesIsTheSameFlagAsRawCommand()
        {
            // They were two flags for the two levels a raw command can be issued at, always set together,
            // which is two chances to check the wrong one. RawSentences is an obsolete alias of RawCommand
            // now — the same bit — so any code still asking the old question gets the same answer.
#pragma warning disable CS0618 // deliberately referencing the obsolete alias
            Assert.AreEqual(TikConnectionCapability.RawCommand, TikConnectionCapability.RawSentences);
#pragma warning restore CS0618
        }

        [TestMethod]
        public void TheFlagGatesBothRawLevelsTogether()
        {
            // The merge is only honest if the flag really does answer for both levels: a transport that
            // declares it must offer CreateRawCommand AND CallCommandSync, and one that does not must offer
            // neither. That is the property the two separate flags were trying and failing to express.
            foreach (TikConnectionType type in Enum.GetValues(typeof(TikConnectionType)))
            {
                if (type == TikConnectionType.Ssh) continue; // satellite package, not referenced here

                using (var conn = ConnectionFactory.CreateConnection(type))
                {
                    bool flag = conn.Supports(TikConnectionCapability.RawCommand);

                    Assert.AreEqual(flag, conn is ITikRawSentenceConnection,
                        $"{type}: the low-level half (CallCommandSync) must match the flag");

                    if (flag)
                        conn.CreateRawCommand("/system/identity/print");   // must not throw on capability
                    else
                        Assert.ThrowsException<TikConnectionCapabilityNotSupportedException>(
                            () => conn.CreateRawCommand("/system/identity/print"),
                            $"{type}: the ADO-level half must refuse for the same reason");
                }
            }
        }
    }
}
