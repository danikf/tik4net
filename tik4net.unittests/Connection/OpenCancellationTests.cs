using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// A <see cref="CancellationToken"/> handed to <c>OpenAsync</c> has to do something on every transport.
    /// </summary>
    /// <remarks>
    /// It used to exist only on <c>TikConnectionSetup.CreateAsync</c>/<c>OpenAsync</c>, where it was checked
    /// once and then dropped — <c>ITikConnection.OpenAsync</c> had no token parameter to pass it to. So a
    /// caller who wrote <c>setup.CreateAsync(type, cts.Token)</c> to escape a hanging connect waited out
    /// <c>ConnectTimeout</c> anyway, with nothing to tell them why.
    /// <para>
    /// The bar this class sets is deliberately the low one — <b>an already-cancelled token must never open a
    /// socket</b> — because that is the part every transport can honour and therefore the part that can be
    /// stated as a rule. How much further the token reaches genuinely differs (the API and REST transports
    /// await the whole exchange; the CLI family covers the login but not the synchronous connect ahead of
    /// it; WinBox native cannot honour it at all once the EC-SRP5 handshake starts), and that difference is
    /// documented per transport on <c>ITikConnection.OpenAsync</c> rather than asserted here as if it were
    /// uniform.
    /// </para>
    /// </remarks>
    [TestClass]
    public class OpenCancellationTests
    {
        /// <summary>
        /// Every transport in the registry, so a new one cannot join without answering this question.
        /// </summary>
        private static TikConnectionType[] AllTransports()
            => Enum.GetValues(typeof(TikConnectionType)).Cast<TikConnectionType>()
                   // Ssh lives in the satellite package and is created through the same registry, but the
                   // unit-test project deliberately exercises it via the option matrix instead.
                   .Where(t => t != TikConnectionType.Ssh)
                   .ToArray();

        [TestMethod]
        public async Task AnAlreadyCancelledTokenStopsTheOpenBeforeAnySocket()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                var setup = new TikConnectionSetup("203.0.113.1", "admin", "");
                foreach (TikConnectionType type in AllTransports())
                {
                    using ITikConnection connection = setup.CreateUnopened(type);

                    // A deliberately unroutable address: if cancellation were ignored the call would hang
                    // for ConnectTimeout instead of returning, which is the failure this test would show up
                    // as. 203.0.113.0/24 is TEST-NET-3, reserved for documentation and never routed.
                    await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                        () => connection.OpenAsync("203.0.113.1", "admin", "", cts.Token),
                        $"{type}: an already-cancelled token must stop the open");
                }
            }
        }

        /// <summary>
        /// The token must reach the connection, not stop at the setup object.
        /// </summary>
        /// <remarks>
        /// A signature check rather than a behavioural one, on purpose: this is the exact thing that was
        /// missing, and a passing behavioural test elsewhere would not have noticed, because
        /// <c>OpenCoreAsync</c> did check the token — just before calling an <c>OpenAsync</c> that had
        /// nowhere to put it.
        /// </remarks>
        [TestMethod]
        public void BothOpenAsyncOverloadsTakeACancellationToken()
        {
            MethodInfo[] overloads = typeof(ITikConnection)
                .GetMethods()
                .Where(m => m.Name == "OpenAsync")
                .ToArray();

            Assert.AreEqual(2, overloads.Length, "ITikConnection should declare exactly two OpenAsync overloads");

            foreach (MethodInfo m in overloads)
            {
                ParameterInfo last = m.GetParameters().Last();
                Assert.AreEqual(typeof(CancellationToken), last.ParameterType,
                    $"{m} must end with a CancellationToken");
                Assert.IsTrue(last.IsOptional,
                    $"{m}'s token must be optional, or every existing caller breaks for no benefit");
            }
        }
    }
}
