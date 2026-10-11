// WinboxSessionLimitTest.cs — RouterOS's limit of WinBox/Dude sessions, reached on purpose, and the open's retry.
//
// RouterOS holds at most 100 WinBox/Dude sessions (7.24; /ip service winbox max-sessions does not set it) and refuses
// the next connection by closing it before sending a byte. A RoMON agent reaches it with dead relay links and frees it
// within a fraction of a second, so Open() retries that refusal (RouterLoginRetry, Docs/findings-romon.md "The WinBox
// session limit"). Here the slots are filled with live sessions, which nothing reaps, so the refusal holds until the
// test frees one.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.integrationtests
{
    [TestClass]
    [TestCategory(TestCategories.LegIndependent)]
    // Filling every WinBox slot refuses every other WinBox client of the router for the test's ~1 minute.
    [TestLock(TestLockScope.Router)]
    public class WinboxSessionLimitTest : LockedTestBase
    {
        private const int MostSlotsTried = 150;

        private static ITikConnection OpenWinbox()
            => new TikConnectionSetup(LabConfig.Get("host"), LabConfig.Get("user"), LabConfig.Get("pass") ?? "")
                .Create(TikConnectionType.WinboxNative);

        [TestMethod]
        public void Open_AtTheSessionLimit_RetriesAndFailsNamingIt_AndSucceedsOnceASlotFrees()
        {
            var held = new List<ITikConnection>();
            try
            {
                Exception refusal = null;
                while (held.Count < MostSlotsTried)
                {
                    try { held.Add(OpenWinbox()); }
                    catch (TikConnectionLoginException ex) { refusal = ex; break; }
                }
                if (refusal == null)
                    Assert.Inconclusive($"{MostSlotsTried} WinBox sessions were accepted without a refusal.");
                StringAssert.Contains(refusal.Message, "WinBox/Dude sessions");
                Console.WriteLine($"[limit] refused after {held.Count} held sessions");

                // Nothing frees a slot: every attempt is refused, and the open gives up only after its retries.
                var watch = Stopwatch.StartNew();
                var ex2 = Assert.ThrowsException<TikConnectionLoginException>(() => OpenWinbox().Dispose());
                StringAssert.Contains(ex2.Message, "WinBox/Dude sessions");
                Assert.IsTrue(watch.ElapsedMilliseconds >= 2500,
                    $"the refusal must be retried (1 s, then 2 s) before it is reported; it took {watch.ElapsedMilliseconds} ms");

                // A slot frees while the open waits to retry: the open succeeds on a later attempt.
                var freeOne = Task.Run(() => { Thread.Sleep(300); held[0].Dispose(); });
                watch.Restart();
                using (var opened = OpenWinbox())
                    Assert.IsNotNull(opened.CreateCommand("/system/identity/print").ExecuteScalar());
                freeOne.Wait();
                held.RemoveAt(0);
                Assert.IsTrue(watch.ElapsedMilliseconds >= 900,
                    $"the first attempt must have been refused and retried; the open took {watch.ElapsedMilliseconds} ms");
            }
            finally
            {
                foreach (var c in held)
                    try { c.Dispose(); } catch { /* closing: nothing left to do */ }
            }
        }
    }
}
