#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// After a receive timeout a CLI session is brought back in step — Ctrl-C, a drain, then a fence whose joined
    /// marker the echo cannot contain — and closed only when that fails.
    /// </summary>
    [TestClass]
    public class CliResyncTests
    {
        private sealed class TimingOutTerminal : CliConnectionBase
        {
            public TimingOutTerminal() { CliFieldSeparator = null; }

            public readonly List<string> Sent = new List<string>();
            public readonly List<byte[]> Raw = new List<byte[]>();
            public bool Closed { get; private set; }
            public bool TimeOutNext = true;
            public Func<string, string> AnswerFence = fence => fence;   // echo only: the router never printed it
            public Exception? DrainFails;
            public string Reply = string.Empty;

            protected override string TransportName => "Timing out";

            public void OpenScripted()
            {
                OpenWith(_ => Task.FromResult(0), SendAsync, (raw, ct) => Task.FromResult(string.Empty), () => Closed = true);
                RegisterCompletionDriver((raw, quietMs, ct) =>
                {
                    Raw.Add(raw);
                    if (DrainFails != null) throw DrainFails;
                    return Task.FromResult("t4n-late\r\n[admin@MikroTik] > ");
                });
            }

            private Task<string> SendAsync(string cliText, CancellationToken ct)
            {
                Sent.Add(cliText);
                if (cliText.StartsWith(":put (\"t4n-sync-", StringComparison.Ordinal))
                    return Task.FromResult(AnswerFence(cliText));
                if (TimeOutNext)
                {
                    TimeOutNext = false;
                    throw new TikConnectionReceiveTimeoutException(TimeSpan.FromSeconds(1), "Scripted: no prompt within 1000 ms", "t4n-early");
                }
                return Task.FromResult(CountedReadFake.Answer(cliText, Reply));
            }

            public override void Open(string host, string user, string password) => OpenScripted();
            public override void Open(string host, int port, string user, string password) => OpenScripted();
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
        }

        // What the router prints for :put ("t4n-sync-" . "<guid>"): the echo, then the joined string.
        private static string Printed(string fence)
        {
            int start = fence.IndexOf("\" . \"", StringComparison.Ordinal) + 5;
            string guid = fence.Substring(start, fence.LastIndexOf('"') - start);
            return fence + "\r\nt4n-sync-" + guid;
        }

        [TestMethod]
        public void ATimeoutIsFollowedByCtrlC_AndAFence_AndTheSessionStaysOpen()
        {
            using (var conn = new TimingOutTerminal { AnswerFence = Printed, Reply = ".id=*1;name=ether1" })
            {
                conn.OpenScripted();

                Assert.ThrowsException<TikConnectionReceiveTimeoutException>(() => conn.CreateCommand("/interface/print").ExecuteList());

                Assert.IsTrue(conn.IsOpened, "the fence came back, so the session is in step again");
                Assert.IsFalse(conn.Closed);
                CollectionAssert.AreEqual(new byte[] { 0x03 }, conn.Raw.Single(), "Ctrl-C, never Enter");
                Assert.AreEqual("ether1", conn.CreateCommand("/interface/print").ExecuteList().Single().GetResponseField("name"));
            }
        }

        [TestMethod]
        public void AFenceThatNeverComesBack_ClosesTheSession_AfterThreeTries()
        {
            using (var conn = new TimingOutTerminal())   // the echo only: the typed line, never the joined marker
            {
                conn.OpenScripted();

                Assert.ThrowsException<TikConnectionReceiveTimeoutException>(() => conn.CreateCommand("/interface/print").ExecuteList());

                Assert.AreEqual(3, conn.Sent.Count(s => s.StartsWith(":put (\"t4n-sync-", StringComparison.Ordinal)));
                Assert.IsTrue(conn.Closed, "the echo contains the fence's pieces, not the joined marker — not proof of anything");
                Assert.IsFalse(conn.IsOpened);
            }
        }

        [TestMethod]
        public void ADrainThatFails_ClosesTheSession_AndTheTimeoutStillReachesTheCaller()
        {
            using (var conn = new TimingOutTerminal { AnswerFence = Printed, DrainFails = new System.IO.IOException("socket gone") })
            {
                conn.OpenScripted();

                Assert.ThrowsException<TikConnectionReceiveTimeoutException>(() => conn.CreateCommand("/interface/print").ExecuteList());

                Assert.IsTrue(conn.Closed);
            }
        }
    }
}
