// RawCommandErrorTests.cs — what a raw pass-through does when the router answers with an error.
//
// CreateRawCommand and CallCommandSync are the ADO-level and low-level halves of one promise: a command in
// the transport's own language, sent unchanged. They disagreed about failure. Measured over Telnet against
// a live router, one bad command gave three answers:
//
//     CallCommandSync("/interface prnt")                 -> TikNoSuchCommandException
//     CreateRawCommand("/interface prnt").ExecuteList()  -> no throw, 0 rows
//     CreateRawCommand("/interface prnt").ExecuteScalar() -> no throw, value = the error text
//
// The last is the worst shape there is: "bad command name prnt (line 1 column 12)" handed back as a
// successful return value, to be assigned to a variable and used. The middle one reports an empty table for
// a command that never ran.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    [TestClass]
    public class RawCommandErrorTests
    {
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

        // The router's actual wording, from a live Telnet session.
        private const string RouterError = "bad command name prnt (line 1 column 12)";

        private static RecordingCliConnection OpenConnection(string reply)
        {
            var conn = new RecordingCliConnection { Reply = reply };
            conn.OpenScripted();
            return conn;
        }

        // ── A raw command that failed says so, on every Execute shape ─────────

        [TestMethod]
        public void RawExecuteScalarRaisesTheRouterError()
        {
            using (var conn = OpenConnection(RouterError))
            {
                Assert.ThrowsException<TikNoSuchCommandException>(
                    () => conn.CreateRawCommand("/interface prnt").ExecuteScalar(),
                    "the error text must not become the return value");
            }
        }

        [TestMethod]
        public void RawExecuteListRaisesTheRouterError()
        {
            using (var conn = OpenConnection(RouterError))
            {
                Assert.ThrowsException<TikNoSuchCommandException>(
                    () => conn.CreateRawCommand("/interface prnt").ExecuteList().ToList(),
                    "an empty list is indistinguishable from an empty table; the command never ran");
            }
        }

        [TestMethod]
        public void RawExecuteNonQueryRaisesTheRouterError()
        {
            using (var conn = OpenConnection(RouterError))
            {
                Assert.ThrowsException<TikNoSuchCommandException>(
                    () => conn.CreateRawCommand("/interface prnt").ExecuteNonQuery());
            }
        }

        [TestMethod]
        public void TheLowLevelCallAgrees()
        {
            // The other half of the same promise, asserted side by side so the two cannot drift apart again.
            using (var conn = OpenConnection(RouterError))
            {
                Assert.ThrowsException<TikNoSuchCommandException>(
                    () => conn.CallCommandSync("/interface prnt").ToList());
            }
        }

        // ── …and a raw command that worked is left alone ──────────────────────

        [TestMethod]
        public void RawOutputThatIsNotAnErrorIsReturnedUnchanged()
        {
            using (var conn = OpenConnection("MikroTik"))
            {
                Assert.AreEqual("MikroTik",
                    conn.CreateRawCommand(":put [/system identity get name]").ExecuteScalar());
            }
        }

        [TestMethod]
        public void RawAsValueOutputStillParsesIntoRecords()
        {
            using (var conn = OpenConnection(".id=*1;name=ether1;mtu=1500"))
            {
                var rows = conn.CreateRawCommand(":put [/interface print as-value]").ExecuteList().ToList();

                Assert.AreEqual(1, rows.Count);
                Assert.AreEqual("ether1", rows[0].GetResponseField("name"));
            }
        }

        [TestMethod]
        public void EmptyOutputIsNotAnError()
        {
            // A command that legitimately prints nothing — the common case for a write — must not be read as
            // a failure just because there is no output to inspect.
            using (var conn = OpenConnection(""))
            {
                conn.CreateRawCommand("/ip/address/set numbers=0 comment=x").ExecuteNonQuery();
                Assert.AreEqual(0, conn.CreateRawCommand(":put [/interface print as-value]")
                                       .ExecuteList().Count());
            }
        }

        [TestMethod]
        public void OutputThatMerelyMentionsAnErrorWordIsNotAnError()
        {
            // The argument against parsing errors out of arbitrary raw output is false positives, and this is
            // the shape that would produce one: a legitimate value containing the word. It must survive.
            using (var conn = OpenConnection("failure-count"))
            {
                Assert.AreEqual("failure-count",
                    conn.CreateRawCommand(":put [/interface get 0 name]").ExecuteScalar());
            }
        }
    }
}
