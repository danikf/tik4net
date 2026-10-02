// CliEndMarkerTests.cs — a write on RouterOS 7 ends on its own marker line, not on a prompt that stayed silent.
//
// A write is silent on success, so its read used to end only once the prompt had been quiet for the settle window —
// about half of what such a command cost (SSH, 800 writes of a full suite leg, 2026-10-01). The library appends
// '; :put ("#e" . "=<nonce>")' to a one-line write it built, on a router known to be RouterOS 7; the read is over at
// that line. A refused write stops the line before the marker and settles as before.

#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    [TestClass]
    public class CliEndMarkerTests
    {
        private const string Prompt = "[admin@MikroTik] > ";

        // ── The marker itself ─────────────────────────────────────────────────

        [TestMethod]
        public void TheMarkerLineIsComputed_SoTheEchoNeverCarriesIt()
        {
            string sent = CliOutputHelper.WithEndMarker("/ip address remove numbers=*5", "abc123");

            Assert.AreEqual("/ip address remove numbers=*5; :put (\"#e\" . \"=abc123\")", sent);
            Assert.AreEqual("#e=abc123", CliOutputHelper.EndMarkerLineOf(sent));
            StringAssert.DoesNotMatch(sent, new System.Text.RegularExpressions.Regex("#e=abc123"));
            Assert.IsNull(CliOutputHelper.EndMarkerLineOf("/ip address remove numbers=*5"));
        }

        [TestMethod]
        public void AWriteIsCompleteAtItsMarkerLineAndThePrompt()
        {
            string sent = CliOutputHelper.WithEndMarker("/ip address remove numbers=*5", "abc123");
            Assert.AreEqual(CliOutputHelper.PromptVerdict.Complete,
                CliOutputHelper.JudgePrompt(sent + "\r\n\r#e=abc123\r\n\r\r\r" + Prompt, sent));
            Assert.AreEqual(CliOutputHelper.PromptVerdict.Complete,
                CliOutputHelper.JudgePrompt(":put [/ip address add address=1.2.3.4/32 interface=ether1]; :put (\"#e\" . \"=n1\")"
                    + "\r\n\r*1A\r\n#e=n1\r\n\r\r\r" + Prompt, ":put [/ip address add address=1.2.3.4/32 interface=ether1]; :put (\"#e\" . \"=n1\")"));
        }

        [TestMethod]
        public void ARefusedWriteSettlesAsBefore_AndAPromptBeforeAnythingIsNotItsEnd()
        {
            string sent = CliOutputHelper.WithEndMarker("/ip address remove numbers=*5", "abc123");
            Assert.AreEqual(CliOutputHelper.PromptVerdict.Settle,
                CliOutputHelper.JudgePrompt(sent + "\r\n\rno such item (4)\r\n\r\r\r" + Prompt, sent));
            Assert.AreEqual(CliOutputHelper.PromptVerdict.NotYet,
                CliOutputHelper.JudgePrompt(sent + "\r\n\r\r\r" + Prompt, sent));
            Assert.AreEqual(CliOutputHelper.PromptVerdict.Settle,
                CliOutputHelper.JudgePrompt(sent + "\r\n\r#e=other\r\n\r\r\r" + Prompt, sent), "another command's marker is not this one's");
        }

        [TestMethod]
        public void TheMarkerLineIsTakenOffWhatTheCommandPrinted()
        {
            Assert.AreEqual("", CliOutputHelper.WithoutEndMarker("#e=n1", "#e=n1"));
            Assert.AreEqual("*1A", CliOutputHelper.WithoutEndMarker("*1A\n#e=n1", "#e=n1"));
            Assert.AreEqual("no such item (4)", CliOutputHelper.WithoutEndMarker("no such item (4)", "#e=n1"));
            Assert.AreEqual("x#e=n1", CliOutputHelper.WithoutEndMarker("x#e=n1", "#e=n1"), "not a line of its own");
        }

        // ── Through a CLI connection ───────────────────────────────────────────

        /// <summary>A router that runs a marked write and prints the marker, unless the write is refused.</summary>
        private sealed class WriteRouter : CliConnectionBase
        {
            public readonly List<string> Sent = new List<string>();
            public bool RefusesSerialize;   // RouterOS 6
            public bool RefusesTheWrite;

            public WriteRouter() { }

            protected override string TransportName => "Write";

            public void OpenScripted()
                => OpenWith(_ => Task.FromResult(0), SendAsync, (raw, ct) => Task.FromResult(string.Empty), () => { });

            private Task<string> SendAsync(string cliText, CancellationToken ct)
            {
                Sent.Add(cliText);
                if (cliText == CliCommandBuilder.BuildDsvProbe(TikConnectionSetup.DefaultCliFieldSeparator))
                    return Task.FromResult(RefusesSerialize ? "bad command name serialize (line 1 column 17)" : "str");
                if (cliText.Contains(":serialize to=dsv"))
                    return Task.FromResult(".id~^~address\r\n*5~^~192.0.2.1/32\r\n#n=1/num");
                if (cliText.Contains(" print "))
                    return Task.FromResult(CountedReadFake.Answer(cliText, ".id=*5;address=192.0.2.1/32"));
                if (RefusesTheWrite)
                    return Task.FromResult("no such item (4)");   // the router stops the line before the marker
                string? marker = CliOutputHelper.EndMarkerLineOf(cliText);
                return Task.FromResult(marker ?? string.Empty);
            }

            public override void Open(string host, string user, string password) => OpenScripted();
            public override void Open(string host, int port, string user, string password) => OpenScripted();
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
        }

        private static WriteRouter Open(bool routerOs6 = false)
        {
            var router = new WriteRouter { RefusesSerialize = routerOs6 };
            router.OpenScripted();
            router.CreateCommand("/ip/address/print").ExecuteList();   // a read: the router's version becomes known
            return router;
        }

        private static void Remove(ITikConnection c)
            => c.CreateCommandAndParameters("/ip/address/remove", TikSpecialProperties.Id, "*5").ExecuteNonQuery();

        [TestMethod]
        public void OnRouterOs7_AWriteCarriesTheMarker_AndSucceeds()
        {
            using (var router = Open())
            {
                Remove(router);
                string write = router.Sent.Last();
                Assert.IsNotNull(CliOutputHelper.EndMarkerLineOf(write), write);
                StringAssert.StartsWith(write, "/ip address remove");
            }
        }

        [TestMethod]
        public void ARefusedWriteIsStillTheRoutersError()
        {
            using (var router = Open())
            {
                router.RefusesTheWrite = true;
                Assert.ThrowsException<TikNoSuchItemException>(() => Remove(router));
            }
        }

        [TestMethod]
        public void OnRouterOs6_NoMarkerIsSent()
        {
            using (var router = Open(routerOs6: true))
            {
                Remove(router);
                Assert.IsNull(CliOutputHelper.EndMarkerLineOf(router.Sent.Last()), router.Sent.Last());
            }
        }

        [TestMethod]
        public void BeforeTheVersionIsKnown_NoMarkerIsSent()
        {
            using (var router = new WriteRouter())
            {
                router.OpenScripted();
                Remove(router);
                Assert.IsNull(CliOutputHelper.EndMarkerLineOf(router.Sent.Last()), router.Sent.Last());
            }
        }

        [TestMethod]
        public void ARawCommandIsSentAsWritten()
        {
            using (var router = Open())
            {
                router.CallCommandSync("/ip address remove numbers=*5");
                Assert.AreEqual("/ip address remove numbers=*5", router.Sent.Last());
            }
        }

        [TestMethod]
        public void ACommandThatMayAskAQuestion_GetsNoMarker()
        {
            using (var router = Open())
            {
                router.CreateCommand("/system/reboot").ExecuteNonQuery();
                Assert.IsNull(CliOutputHelper.EndMarkerLineOf(router.Sent.Last()), router.Sent.Last());
            }
        }
    }
}
