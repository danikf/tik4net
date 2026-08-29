// EmptyResponseScalarTests.cs — P2.34: "the command returned nothing" must not be reported as
// "no such item".
//
// A successful write prints nothing: over the CLI a set/unset/remove/enable/comment echoes an empty
// line, and over the binary API it answers a bare !done with no =ret=. Asking such a command for a
// scalar used to raise TikNoSuchItemException — a fabricated router error, on a command that had in
// fact succeeded, pointing the caller at a record that was never missing. (It misled a live
// investigation: several verbs looked broken while the router had applied every one of them.)
//
// These tests pin the corrected behaviour on both command implementations — TikGenericCommand
// (CLI / REST / WinBox-native) and ApiCommand (binary API) — because the fabricated error was
// identical on both and so is the fix.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;
using tik4net.Connection;
using tik4net.unittests.Api;

namespace tik4net.unittests
{
    [TestClass]
    public class EmptyResponseScalarTests
    {
        private const string TestUser = "admin";
        private const string TestPassword = "";

        /// <summary>
        /// Minimal <see cref="TikCommandConnectionBase"/> whose CRUD hooks answer exactly what a silent
        /// router does: no raw text, no new .id, no rows.
        /// </summary>
        private sealed class SilentConnection : TikCommandConnectionBase
        {
            public string RawTextToReturn { get; set; }
            public string AddIdToReturn { get; set; }

            public override TikConnectionCapability Capabilities
                => TikConnectionCapability.Crud | TikConnectionCapability.RawCommand;

            protected override string RunRawText(TikCommandDescriptor descriptor) => RawTextToReturn;
            protected override string RunAdd(TikCommandDescriptor descriptor) => AddIdToReturn;
            protected override IList<TikRecordSentence> RunPrint(TikCommandDescriptor descriptor) => new List<TikRecordSentence>();
            protected override void RunNonQuery(TikCommandDescriptor descriptor) { }

            public override void Open(string host, string user, string password) { }
            public override void Open(string host, int port, string user, string password) { }
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public override void Close() { }
        }

        // ── Raw pass-through (CLI transports) ──────────────────────────────────

        [TestMethod]
        public void RawScalar_WithNoOutput_ReportsAnEmptyResponseNotAMissingItem()
        {
            var conn = new SilentConnection { RawTextToReturn = "" };

            var ex = Assert.ThrowsException<TikCommandEmptyResponseException>(
                () => conn.CreateRawCommand("/ip/firewall/filter set .id=*1 comment=x").ExecuteScalar());

            Assert.IsNotInstanceOfType(ex, typeof(TikNoSuchItemException),
                "the item exists and the command succeeded — 'no such item' would send the caller after a record that is not missing");
            StringAssert.Contains(ex.Message, "no output");
            StringAssert.Contains(ex.Message, "ExecuteNonQuery",
                "the message has to name the call that is actually wanted here");
        }

        [TestMethod]
        public void RawScalarOrDefault_WithNoOutput_ReturnsTheDefault()
        {
            var conn = new SilentConnection { RawTextToReturn = "" };

            Assert.IsNull(conn.CreateRawCommand("/ip/firewall/filter set .id=*1 comment=x").ExecuteScalarOrDefault());
            Assert.AreEqual("fallback",
                conn.CreateRawCommand("/ip/firewall/filter set .id=*1 comment=x").ExecuteScalarOrDefault("fallback"));
        }

        [TestMethod]
        public void RawScalar_WithOutput_StillReturnsIt()
        {
            var conn = new SilentConnection { RawTextToReturn = "/ip firewall filter\nadd chain=forward\n" };

            StringAssert.Contains(conn.CreateRawCommand("/export").ExecuteScalar(), "chain=forward");
        }

        // ── add that answers without the new .id ───────────────────────────────

        [TestMethod]
        public void AddScalar_WhenTheRouterDoesNotReturnTheId_SaysSoInsteadOfNoSuchItem()
        {
            // A failing add raises a trap inside RunAdd; reaching this branch means the row was accepted
            // and only the .id is missing from the answer — the opposite of "no such item".
            var conn = new SilentConnection { AddIdToReturn = null };
            var cmd = conn.CreateCommand("/ip/firewall/filter/add");
            cmd.AddParameterAndValues("chain", "forward");

            var ex = Assert.ThrowsException<TikCommandEmptyResponseException>(() => cmd.ExecuteScalar());

            Assert.IsNotInstanceOfType(ex, typeof(TikNoSuchItemException));
            StringAssert.Contains(ex.Message, ".id");
        }

        [TestMethod]
        public void AddScalarOrDefault_WhenTheRouterDoesNotReturnTheId_ReturnsTheDefault()
        {
            var conn = new SilentConnection { AddIdToReturn = null };
            var cmd = conn.CreateCommand("/ip/firewall/filter/add");
            cmd.AddParameterAndValues("chain", "forward");

            Assert.IsNull(cmd.ExecuteScalarOrDefault());
        }

        // ── A read that matches nothing is unchanged: that IS 'no such item' ────
        //
        // An empty answer means two different things depending on the verb, and the response alone cannot
        // tell them apart — only the verb can. These two tests are the pair that keeps that distinction
        // honest: widen the write case and the read case starts lying instead.

        [TestMethod]
        public void ReadScalar_MatchingNoRow_StillThrowsNoSuchItem()
        {
            var conn = new SilentConnection();
            var cmd = conn.CreateCommand("/ip/firewall/filter/print");
            cmd.AddParameterAndValues(".id", "*DEADBEEF");

            Assert.ThrowsException<TikNoSuchItemException>(() => cmd.ExecuteScalar());
        }

        [TestMethod]
        public void WriteScalar_PrintingNothing_ReportsAnEmptyResponse()
        {
            var conn = new SilentConnection();
            var cmd = conn.CreateCommand("/ip/firewall/filter/set");
            cmd.AddParameterAndValues(".id", "*1");
            cmd.AddParameterAndValues("comment", "x");

            var ex = Assert.ThrowsException<TikCommandEmptyResponseException>(() => cmd.ExecuteScalar());

            Assert.IsNotInstanceOfType(ex, typeof(TikNoSuchItemException));
            StringAssert.Contains(ex.Message, "'set'");
        }

        // ── Binary API: bare !done without =ret= ───────────────────────────────

        [TestMethod]
        public void ApiScalar_OnBareDoneWithoutRet_ReportsAnEmptyResponseNotAMissingItem()
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();              // login
                server.WriteSentence("!done");

                server.ReadSentence();              // /ip/firewall/filter/set
                server.WriteSentence("!done");      // succeeded, nothing to return

                server.ReadSentence();              // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                var cmd = connection.CreateCommand("/ip/firewall/filter/set");
                cmd.AddParameterAndValues(".id", "*1");
                cmd.AddParameterAndValues("comment", "x");

                var ex = Assert.ThrowsException<TikCommandEmptyResponseException>(() => cmd.ExecuteScalar());

                Assert.IsNotInstanceOfType(ex, typeof(TikNoSuchItemException));
                StringAssert.Contains(ex.Message, "=ret=");
                StringAssert.Contains(ex.Message, "ExecuteNonQuery");

                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        [TestMethod]
        public void ApiScalarOrDefault_OnBareDoneWithoutRet_ReturnsTheDefault()
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();              // login
                server.WriteSentence("!done");

                server.ReadSentence();              // the command under test
                server.WriteSentence("!done");

                server.ReadSentence();              // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                Assert.AreEqual("fallback",
                    connection.CreateCommand("/ip/firewall/filter/set").ExecuteScalarOrDefault("fallback"));

                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        [TestMethod]
        public void ApiReadScalar_OnBareDoneWithoutRet_StillThrowsNoSuchItem()
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();              // login
                server.WriteSentence("!done");

                server.ReadSentence();              // /ip/address/print ?.id=-NoID-
                server.WriteSentence("!done");      // nothing matched

                server.ReadSentence();              // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                var cmd = connection.CreateCommandAndParameters("/ip/address/print",
                    TikCommandParameterFormat.Filter, TikSpecialProperties.Id, "-NoID-");

                Assert.ThrowsException<TikNoSuchItemException>(() => cmd.ExecuteScalar(TikSpecialProperties.Id));

                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        // The whole hierarchy stays catchable by the documented roots.
        [TestMethod]
        public void EmptyResponseException_IsACommandException()
        {
            var ex = new TikCommandEmptyResponseException(null, "nothing came back");

            Assert.IsInstanceOfType(ex, typeof(TikCommandException));
            Assert.IsInstanceOfType(ex, typeof(TikConnectionException));
            StringAssert.Contains(ex.Message, "nothing came back");
        }
    }
}
