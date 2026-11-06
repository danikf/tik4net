using System;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tik4net.integrationtests
{
    /// <summary>
    /// RouterOS logs an idle MAC-Telnet console out after about 30 s. The connection then rebuilds the session
    /// under the caller (new client, new EC-SRP5 login, through <c>TikOpenRetry</c>) and re-sends the command,
    /// unless Safe Mode is held: there the dropped session is the rollback the caller asked for, and the read throws.
    /// </summary>
    /// <remarks>
    /// A refused re-login cannot be provoked on demand, so the retry itself rests on <c>TikOpenRetry</c>'s unit
    /// tests; this class covers the rebuild path it sits on.
    /// </remarks>
    [TestClass]
    [TestLock(TestLockScope.Router)]
    public class MacTelnetIdleRebuildTest : TestBase
    {
        /// <summary>Past RouterOS's idle logout of a MAC console (~30 s).</summary>
        private static readonly TimeSpan Idle = TimeSpan.FromSeconds(40);

        // Each test idles its own session out; a shared one would carry the drop into the next test.
        protected override bool ReuseConnectionAcrossTests => false;

        protected override void OnInitialize()
        {
            if (ResolveConnectionType() != TikConnectionType.MacTelnet)
                Assert.Inconclusive("MAC-Telnet's idle rebuild only; the transport under test is " + ResolveConnectionType() + ".");
        }

        private string ReadIdentity()
            => Connection.CreateCommand("/system/identity/print").ExecuteSingleRow().GetResponseField("name");

        [TestMethod]
        public void AReadAfterTheIdleLogout_SucceedsThroughARebuiltSession()
        {
            string before = ReadIdentity();
            Thread.Sleep(Idle);
            Assert.AreEqual(before, ReadIdentity());
        }

        [TestMethod]
        public void AReadAfterTheIdleLogout_WithSafeModeHeld_ThrowsSessionClosed()
        {
            EnsureMinRouterOsVersion(7, "the idle logout of a Safe Mode MAC console (RouterOS 6.49 keeps one "
                + "alive: it sends ESC[c about every 0.77 s, which tik4net leaves unanswered on purpose, so the "
                + "session never idles)");
            ReadIdentity();
            SafeModeConnection.SafeModeTake();
            try
            {
                Thread.Sleep(Idle);
                try
                {
                    ReadIdentity();
                    Assert.Fail("The read after the idle logout succeeded although Safe Mode was held.");
                }
                catch (TikConnectionSessionClosedException ex)
                {
                    Console.WriteLine("expected: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            finally
            {
                // The dropped session rolled Safe Mode back on the router. Release any hold that outlives it.
                try
                {
                    using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api,
                        LabConfig.Get("host"), LabConfig.Get("user"), LabConfig.Get("pass") ?? ""))
                        api.CreateCommand("/safe-mode/release").ExecuteNonQuery();
                }
                catch { /* nothing held */ }
            }
        }
    }
}
