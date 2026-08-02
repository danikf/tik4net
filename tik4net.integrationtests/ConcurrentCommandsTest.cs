using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface;
using tik4net.Objects.System;
// Objects.Ip is referenced fully qualified: IpAddress would otherwise read as System.Net.IPAddress at a glance.

namespace tik4net.integrationtests
{
    /// <summary>
    /// Proves that a transport which claims to multiplex actually does: several commands issued at once on
    /// one connection each get their <em>own</em> reply back.
    /// </summary>
    /// <remarks>
    /// Without this, multiplexing is untested by the suite — every other test issues one command at a time,
    /// so a channel that silently serialized, or worse handed a caller someone else's reply, would still run
    /// green. The commands are deliberately of three different shapes, so a mis-delivered reply shows up as
    /// a wrong or missing field rather than as a plausible-looking result.
    /// <para>Skipped on the CLI family — see <see cref="TestBase.SkipOnSingleCommandTransport"/> for the
    /// expectation about which transports are allowed to serialize.</para>
    /// <para><b>Correctness only, deliberately.</b> Whether the commands actually overlap is a throughput
    /// property and depends entirely on round-trip latency: measured on this lab router (24 reads, serial vs.
    /// 6 threads) WinBox-native-over-MAC goes 780 ms → 100–400 ms, the API 90 ms → 70 ms, and WinBox native
    /// over TCP is <i>slower</i> concurrently (25 ms → 55 ms) because a round trip there costs about a
    /// millisecond and thread hand-off costs more than it saves. An assertion on elapsed time would therefore
    /// have to differ per transport and would still be a stopwatch race; what must never break, on any of
    /// them, is that a caller gets its own reply.</para>
    /// <para>Those figures are also a <i>fresh</i> connection's. Keep driving one connection hard and the
    /// router slows the session by ~30× — on every transport, recovering only after seconds of idle — so
    /// this test's 180 s budget is not the safety margin it looks like. That is the router's behaviour,
    /// not the multiplexer's: <c>Docs/findings-router-session-throughput.md</c>.</para>
    /// </remarks>
    [TestClass]
    public class ConcurrentCommandsTest : TestBase
    {
        private const int Workers = 6;
        private const int RoundsPerWorker = 5;
        private const int TimeoutMs = 180000;

        /// <summary>
        /// A private connection, because <see cref="OnInitialize"/> changes a connection-wide setting and the
        /// shared one outlives this class.
        /// </summary>
        protected override bool ReuseConnectionAcrossTests => false;

        protected override void OnInitialize()
        {
            // The binary API correlates a reply to its caller by the '.tag' word, and sending it on
            // *synchronous* commands is opt-in (documented on ITikConnection as "mandatory for multi thread
            // connection usage"). Without it concurrent sync commands on one API connection genuinely
            // cross-deliver rows — measured here as '.id' and 'address' words turning up in the wrong
            // caller's sentence. It is not a transport defect, it is the default this suite has to opt out
            // of; P4.5 plans to flip it. Inert on the other transports, which correlate by their own means.
            Connection.SendTagWithSyncCommand = true;
        }

        [TestMethod]
        public void ConcurrentCommands_EachCallerGetsItsOwnReply()
        {
            SkipOnSingleCommandTransport();

            // Read once, serially, so the concurrent rounds have a known answer to check against — a reply
            // handed to the wrong caller then shows up as a wrong value, not merely as an empty result.
            string version = Connection.LoadSingle<SystemResource>().Version;
            Assert.IsFalse(string.IsNullOrEmpty(version), "precondition: the router reports its version");

            var problems = new ConcurrentQueue<string>();
            var workers = new List<Task>();

            for (int w = 0; w < Workers; w++)
            {
                int worker = w;
                workers.Add(Task.Run(() =>
                {
                    for (int round = 0; round < RoundsPerWorker; round++)
                    {
                        // Rotated per worker AND per round, so the mix in flight keeps changing rather than
                        // settling into one command type running six times over.
                        try { RunOne((worker + round) % 3, version, problems); }
                        catch (Exception ex) { problems.Enqueue($"worker {worker} round {round}: {ex.GetType().Name}: {ex.Message}"); }
                    }
                }));
            }

            Assert.IsTrue(Task.WaitAll(workers.ToArray(), TimeoutMs),
                $"{Workers} concurrent workers did not finish within {TimeoutMs} ms — the channel is not " +
                "carrying concurrent commands, it is blocking on them.");

            Assert.AreEqual(0, problems.Count,
                "concurrent commands returned wrong or failed results:" + Environment.NewLine
                + string.Join(Environment.NewLine, problems));
        }

        private void RunOne(int kind, string expectedVersion, ConcurrentQueue<string> problems)
        {
            switch (kind)
            {
                case 0:
                    // Single-row read of a known value: the strongest mis-delivery check available, because
                    // the expected answer was established before the request went out.
                    string version = Connection.LoadSingle<SystemResource>().Version;
                    if (version != expectedVersion)
                        problems.Enqueue($"system resource came back with version '{version}', expected '{expectedVersion}'");
                    break;

                case 1:
                    // Multi-row read, and the one that spans several frames — the case where a reply
                    // attributed to the wrong caller would truncate someone's list.
                    var interfaces = Connection.LoadAll<Interface>().ToList();
                    if (interfaces.Count == 0)
                        problems.Enqueue("interface list came back empty");
                    else if (interfaces.Any(i => string.IsNullOrEmpty(i.Name)))
                        problems.Enqueue("an interface row arrived without a name");
                    break;

                default:
                    // A third path with its own handler, so the mix in flight is not two commands alternating.
                    var addresses = Connection.LoadAll<Objects.Ip.IpAddress>().ToList();
                    if (addresses.Any(a => string.IsNullOrEmpty(a.Address)))
                        problems.Enqueue("an IP address row arrived without an address");
                    break;
            }
        }
    }
}
