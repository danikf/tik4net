using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tik4net.unittests
{
    /// <summary>
    /// Pins the capability each transport declares, so a change to one is a deliberate act rather than a
    /// discovery made later against a live router.
    /// </summary>
    /// <remarks>
    /// The flags are what <c>Supports</c>/<c>Require</c> gate every feature on, and they are also what the
    /// documentation promises — the matrix in <c>README.md</c> and on the
    /// <i>Connection types &amp; capabilities</i> wiki page is this table in prose. Nothing else checks that
    /// the two agree: the flags are declared per transport in code, and a transport silently gaining or
    /// losing one would be found only by a capability-gated integration test turning Inconclusive, which
    /// reads as "skipped", not as "broken".
    /// <para><c>Ssh</c> is absent because it lives in the <c>tik4net.ssh</c> satellite package, which this
    /// project does not reference. It is a <c>CliConnectionBase</c> like Telnet and inherits the same flags.</para>
    /// </remarks>
    [TestClass]
    public class ConnectionCapabilityMatrixTests
    {
        // AsyncCommands, but deliberately NOT CancelInFlight (P2.2 step 3): the terminal clients await their
        // socket, so the Task-based surface is real — while the response is an unframed byte stream, so a read
        // abandoned mid-command would leave output for the next command to misread. The second flag's absence
        // here is intrinsic and permanent, not a backlog item.
        private const TikConnectionCapability Cli =
            TikConnectionCapability.Crud | TikConnectionCapability.Listen
            | TikConnectionCapability.SafeMode | TikConnectionCapability.RawCommand
            | TikConnectionCapability.AsyncCommands;

        // Listen is polled, like the CLI and native transports — RouterOS's own REST 'listen' is accepted and
        // then never flushes anything (P2.26). No Streaming: an HTTP response arrives in one lump.
        // AsyncCommands + CancelInFlight are the first transport to declare them (P2.2): HttpClient is async to
        // the bottom, and aborting one stateless request cannot desynchronize the next.
        private const TikConnectionCapability Rest =
            TikConnectionCapability.Crud | TikConnectionCapability.Listen
            | TikConnectionCapability.AsyncCommands | TikConnectionCapability.CancelInFlight;

        private const TikConnectionCapability Native =
            TikConnectionCapability.Crud | TikConnectionCapability.Listen | TikConnectionCapability.SafeMode;

        private const TikConnectionCapability Full =
            TikConnectionCapability.Crud | TikConnectionCapability.Listen
            | TikConnectionCapability.Streaming | TikConnectionCapability.RawSentences
            | TikConnectionCapability.Tagging | TikConnectionCapability.SafeMode
            | TikConnectionCapability.RawCommand;

        private static readonly Dictionary<TikConnectionType, TikConnectionCapability> Expected =
            new Dictionary<TikConnectionType, TikConnectionCapability>
            {
                [TikConnectionType.Api]             = Full,
                [TikConnectionType.ApiSsl]          = Full,
                [TikConnectionType.Rest]            = Rest,
                [TikConnectionType.RestSsl]         = Rest,
                [TikConnectionType.Telnet]          = Cli,
                [TikConnectionType.MacTelnet]       = Cli,
                [TikConnectionType.WinboxCli]       = Cli,
                [TikConnectionType.WinboxCliMac]    = Cli,
                [TikConnectionType.WinboxNative]    = Native,
                [TikConnectionType.WinboxNativeMac] = Native,
            };

        [TestMethod]
        public void EveryTransportDeclaresTheDocumentedCapabilities()
        {
            var wrong = new List<string>();
            foreach (var kv in Expected)
            {
                using (var conn = ConnectionFactory.CreateConnection(kv.Key))
                {
                    var actual = (conn as ITikConnectionCapabilities)?.Capabilities;
                    if (actual != kv.Value)
                        wrong.Add($"{kv.Key}: declared {actual?.ToString() ?? "<not ITikConnectionCapabilities>"}, documented {kv.Value}");
                }
            }

            Assert.AreEqual(0, wrong.Count,
                "the capability matrix in README.md and the 'Connection types & capabilities' wiki page no "
                + "longer matches the code — update both, or the declaration:" + Environment.NewLine
                + string.Join(Environment.NewLine, wrong));
        }

        /// <summary>
        /// Every transport this test knows about is covered. A new <see cref="TikConnectionType"/> must be
        /// added to <see cref="Expected"/> — and therefore to the docs — rather than slipping in unlisted.
        /// </summary>
        [TestMethod]
        public void EveryNonObsoleteConnectionTypeIsCovered()
        {
            var missing = Enum.GetValues(typeof(TikConnectionType))
                .Cast<TikConnectionType>()
                .Where(t => !Expected.ContainsKey(t))
                .Where(t => typeof(TikConnectionType).GetField(t.ToString())
                                .GetCustomAttributes(typeof(ObsoleteAttribute), false).Length == 0)
                // Lives in the tik4net.ssh satellite package — see the class remarks.
                .Where(t => t != TikConnectionType.Ssh)
                .ToList();

            Assert.AreEqual(0, missing.Count,
                "connection types with no documented capability set: " + string.Join(", ", missing));
        }

    }
}
