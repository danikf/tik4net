// MacSiblingSessionProbeTest.cs — does a second MAC session's teardown kill the first? (P2.55)
//
// P2.54 made the WinBox-CLI-MAC wedge survivable (reopen + retry) without explaining it. Three traced
// full runs pin the shape of the thing precisely: exactly THREE sessions are dropped per suite run, in
// the SAME three tests every time (Create_IpAddress_With_LowLevel_API, ListRadiusServersWillNotFail,
// SearchByName_Interface_WillWork). It is deterministic, not a background rate the retry now hides.
//
// ── ROOT CAUSE of one of the three, 2026-08-02 on RouterOS 7.23.2 ──────────────────────────────────
//
// A Safe Mode ROLLBACK triggered by another connection kills a concurrent WinBox-CLI-MAC console.
// Probe_SafeModeRollbackOnASibling reproduces it 3 times out of 3:
//
//     held WinboxCliMac   rollback landed after 2150 / 2136 / 2141 ms -> held answered in ~4.3 s (WEDGED)
//     held WinboxCli/TCP  rollback landed after 3198 / 2142 ms        -> held answered in ~0.4 s (fine)
//
// So it is specific to the MAC carrier, not to the CLI engine: the same terminal over TCP 8291 is
// untouched. In suite terms: ListRoutingTablesWillNotFail establishes the shared session,
// SafeMode_DisconnectWithoutRelease_RollsBack takes Safe Mode on its OWN connection and closes without
// releasing, and SearchByName_Interface_WillWork is the next thing to touch the shared session.
//
// THE ROLLBACK IS ASYNCHRONOUS, and that is what two earlier versions of this probe got wrong. RouterOS
// keeps the Safe Mode owner alive after the connection goes away, until a connection-tracking timeout,
// so the rollback lands ~2 s later — which is exactly why SafeModeTest polls up to 30 s for it. Poking
// the held session immediately after the sibling closes therefore asks at the wrong instant and reports
// a healthy 223/224 ms, which is how "Safe Mode is not the cause" got written down twice before the
// timing was understood. It also explains why the victim is always the FIRST test of the NEXT class:
// the rollback lands after the class that caused it has finished.
//
// ── Ruled out, each with its disproof (do not chase these again) ─────────────────────────────────────
//
//   * session-key or local-port collision — 27 sessions in a run, no key and no port repeated. (The key
//     is a 16-bit random drawn per open, so a collision would also move the failure between runs, and
//     the failure does not move.)
//   * our own send flood — before each wedge ~24 packets / 2.4 KB pile up past an unacknowledged head,
//     because the pull loop fires 8/s regardless. That is real and worth fixing on its own (we have no
//     send window at all), but it is the CONSEQUENCE: it starts after the command that goes unanswered.
//   * a plain sibling session's teardown — Probe_SiblingSessionTeardown, 20 cycles across WinBox-MAC,
//     MAC-Telnet and Api siblings, zero wedges. (A sibling holding SAFE MODE is a different matter — see
//     the root cause above. A plain one really is harmless.)
//   * traffic volume / a boundary in the byte stream — Probe_LongLivedSession ran 400 commands and
//     101 099 outbound bytes on ONE session with no stall, past two of the three offsets that wedge.
//   * an idle logout — per-session tracing shows the held session receiving packets right up to the
//     doomed command; the per-session idle gap before it is 0.0 s.
//   * RouterOS echoing a log line into the terminal (the P2.47 family) — exactly one such echo in a
//     whole run, so it can account for at most one of the three.
//   * the command itself — the three run green in isolation, in 3 s, with no drop.
//   * a bad ACK on our side — the last inbound packet before each wedge is correctly acknowledged
//     (counter + length), and a duplicate is correctly re-ACKed at the high-water mark.
//   * anything the router admits to — /log records NOTHING at any of the three wedges.
//
// Env: TIK4NET_SIBLING_CYCLES (default 3), TIK4NET_SAFEMODE_CYCLES (default 2), TIK4NET_SOAK_COMMANDS.
// Cost: roughly 15 s per cycle. Nothing is left on the router — reads only, everything disposed.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using tik4net;

namespace tik4net.integrationtests
{
    // MSTest skips [Ignore] even under --filter, so comment it out to run these.
    [Ignore("Ad-hoc MAC-session-interference probe against a live router — comment out to run.")]
    [TestClass]
    public class MacSiblingSessionProbeTest
    {
        private const int DefaultCycles = 3;

        private static void Log(string line)
        {
            Console.WriteLine(line);
            string path = Environment.GetEnvironmentVariable("TIK4NET_PROBE_LOG");
            if (!string.IsNullOrEmpty(path))
                try { System.IO.File.AppendAllText(path, line + Environment.NewLine); } catch { }
        }

        private static ITikConnection Open(TikConnectionType type)
        {
            var conn = ConnectionFactory.CreateConnection(type);
            string mac = ConfigurationManager.AppSettings["routerMac"];
            if (!string.IsNullOrEmpty(mac))
            {
                switch (conn)
                {
                    case tik4net.MacTelnet.MacTelnetConnection mt: mt.RouterMac = mac; break;
                    case tik4net.WinboxCliMac.WinboxCliMacConnection wm: wm.RouterMac = mac; break;
                    case tik4net.WinboxNativeMac.WinboxNativeMacConnection nm: nm.RouterMac = mac; break;
                }
            }
            conn.Open(ConfigurationManager.AppSettings["host"],
                      ConfigurationManager.AppSettings["user"],
                      ConfigurationManager.AppSettings["pass"] ?? "");
            return conn;
        }

        /// <summary>A cheap read used to ask "is this connection still alive?".</summary>
        private static void Poke(ITikConnection conn)
            => conn.CreateCommandAndParameters("/system/identity/print").ExecuteList();

        private static string Describe(Exception ex)
        {
            var parts = new System.Collections.Generic.List<string>();
            for (Exception e = ex; e != null; e = e.InnerException)
                parts.Add(e.GetType().Name + ": " + e.Message.Split('\n')[0].Trim());
            return string.Join("  <- ", parts);
        }

        /// <summary>
        /// Runs one disturbance against a freshly opened WinBox-CLI-MAC connection and reports how long
        /// the poke afterwards took and whether it threw. A wedge shows up as either an exception or a
        /// poke several seconds long (the P2.54 retry absorbing it).
        /// </summary>
        private static bool RunCycle(string label, Action disturb)
        {
            ITikConnection held = null;
            try
            {
                held = Open(TikConnectionType.WinboxCliMac);
                Poke(held);                     // prove it works before the disturbance

                disturb();

                var sw = Stopwatch.StartNew();
                Poke(held);
                sw.Stop();

                bool wedged = sw.ElapsedMilliseconds > 2000;
                Log(string.Format("  {0,-34} poke {1,6} ms  {2}", label, sw.ElapsedMilliseconds,
                    wedged ? "<== WEDGED (recovered)" : "ok"));
                return wedged;
            }
            catch (Exception ex)
            {
                Log(string.Format("  {0,-34} THREW  {1}", label, Describe(ex)));
                return true;
            }
            finally
            {
                try { held?.Dispose(); } catch { }
            }
        }

        private static void OpenAndClose(TikConnectionType type)
        {
            var sibling = Open(type);
            Poke(sibling);
            sibling.Dispose();
        }

        [TestMethod]
        public void Probe_SiblingSessionTeardown_DoesNotKillTheHeldSession()
        {
            int cycles = int.TryParse(
                Environment.GetEnvironmentVariable("TIK4NET_SIBLING_CYCLES"), out int n) && n > 0
                ? n : DefaultCycles;

            // Ordered cheapest-hypothesis-first. "idle only" is the control: if it wedges as often as the
            // others, the sibling is innocent and the real variable is elapsed time.
            var cases = new (string Label, Action Disturb)[]
            {
                ("idle only (control)",            () => Thread.Sleep(3000)),
                ("sibling WinboxCliMac open+close", () => OpenAndClose(TikConnectionType.WinboxCliMac)),
                ("sibling MacTelnet open+close",    () => OpenAndClose(TikConnectionType.MacTelnet)),
                ("sibling Api open+close",          () => OpenAndClose(TikConnectionType.Api)),
            };

            Log("=== P2.55 sibling-session probe, " + cycles + " cycle(s) each ===");
            var wedges = new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);

            for (int i = 0; i < cycles; i++)
            {
                Log("-- cycle " + (i + 1));
                foreach (var c in cases)
                {
                    if (!wedges.ContainsKey(c.Label)) wedges[c.Label] = 0;
                    if (RunCycle(c.Label, c.Disturb)) wedges[c.Label]++;
                }
            }

            Log("=== wedges per disturbance (out of " + cycles + ") ===");
            foreach (var kv in wedges)
                Log(string.Format("  {0,-34} {1}", kv.Key, kv.Value));

            // Deliberately not an assertion on the router's behaviour — this is a probe, and a run that
            // reproduces nothing is a result too. It fails only if it could not do its job at all.
            Assert.AreEqual(cases.Length, wedges.Count, "not every disturbance was exercised");
        }

        /// <summary>
        /// The minimal reproduction of one of the three wedges, in library terms rather than test terms
        /// (P2.55). Reduced from a 340-test suite run to this:
        /// <list type="number">
        /// <item>a WinBox-CLI-MAC session is open and has done some work;</item>
        /// <item>a SECOND connection takes Safe Mode and is disposed <b>without releasing it</b>, so
        /// RouterOS rolls the changes back;</item>
        /// <item>the first session's next command is never acknowledged.</item>
        /// </list>
        /// <para>
        /// The ordering is the part that took longest to see: run the Safe Mode step FIRST, before the
        /// held session exists, and nothing happens.
        /// </para>
        /// <para>
        /// <b>The rollback is asynchronous</b>, and that is what two earlier versions of this probe got
        /// wrong. RouterOS keeps the Safe Mode owner alive after the connection goes away — until a
        /// connection-tracking timeout — so the rollback lands seconds later, which is exactly why
        /// <c>SafeModeTest</c> polls for up to 30 s for it. Poking the held session immediately after the
        /// sibling closes therefore tests the wrong instant, and reported a healthy 223/224 ms twice. This
        /// version waits for the rollback to actually land before asking the held session anything.
        /// </para>
        /// <para>This accounts for at most ONE of the three wedges in a suite run. The other two involve no
        /// Safe Mode at all and are still unexplained.</para>
        /// </summary>
        [TestMethod]
        public void Probe_SafeModeRollbackOnASibling_KillsTheHeldSession()
        {
            int cycles = int.TryParse(
                Environment.GetEnvironmentVariable("TIK4NET_SAFEMODE_CYCLES"), out int n) && n > 0
                ? n : 2;

            Log("=== P2.55 repro: Safe Mode rollback on a sibling ===");
            // WinboxCli (TCP) runs the SAME CLI engine over a different carrier, so it separates "the
            // rollback disturbs any concurrent console" from "it disturbs the MAC carrier specifically".
            foreach (var heldTransport in new[] { TikConnectionType.WinboxCliMac, TikConnectionType.WinboxCli })
                for (int c = 0; c < cycles; c++)
                    RunSafeModeCycle(heldTransport);
        }

        private static void RunSafeModeCycle(TikConnectionType heldTransport)
        {
            Log("-- held=" + heldTransport);
            using (var held = Open(heldTransport))
            {
                Poke(held);
                Poke(held);   // give the session some history, as the suite's shared connection has

                // Mirrors SafeModeTest.SafeMode_DisconnectWithoutRelease_RollsBack. The change inside Safe
                // Mode is not decoration: the rollback has to have something to undo.
                string name = "p255-probe-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                var sibling = Open(TikConnectionType.WinboxCliMac);
                try
                {
                    sibling.SafeModeTake();
                    sibling.CreateCommandAndParameters("/ppp/secret/add", "name", name).ExecuteNonQuery();
                    sibling.Close();        // dropped while still holding Safe Mode -> RouterOS rolls back
                }
                finally { try { sibling.Dispose(); } catch { } }

                // Wait for the rollback to LAND, on a third connection, exactly as the test does. The held
                // session is deliberately not touched while we wait — in the suite it sits idle here too.
                var rollback = Stopwatch.StartNew();
                bool landed = false;
                using (var watcher = Open(TikConnectionType.Api))
                {
                    for (int i = 0; i < 30 && !landed; i++)
                    {
                        int count = watcher.CreateCommandAndParameters("/ppp/secret/print", "?name", name)
                                           .ExecuteList().Count();
                        if (count == 0) landed = true;
                        else Thread.Sleep(1000);
                    }
                }
                rollback.Stop();
                Log("  rollback " + (landed ? "landed after " + rollback.ElapsedMilliseconds + " ms"
                                            : "NEVER landed within 30 s — cleaning up"));
                if (!landed)
                    using (var cleanup = Open(TikConnectionType.Api))
                        foreach (var row in cleanup.CreateCommandAndParameters("/ppp/secret/print", "?name", name).ExecuteList())
                            cleanup.CreateCommandAndParameters("/ppp/secret/remove", ".id", row.GetId()).ExecuteNonQuery();

                var sw = Stopwatch.StartNew();
                try
                {
                    Poke(held);
                    sw.Stop();
                    Log("  held session answered in " + sw.ElapsedMilliseconds + " ms  "
                        + (sw.ElapsedMilliseconds > 2000 ? "<== WEDGED (P2.54 recovered it)" : "ok - not reproduced"));
                }
                catch (Exception ex)
                {
                    Log("  held session THREW after " + sw.ElapsedMilliseconds + " ms  " + Describe(ex));
                }
            }
        }

        /// <summary>
        /// Does a session wedge after a certain amount of traffic rather than because of a sibling?
        /// <para>
        /// The sibling probe above found nothing, and the difference between it and the suite is history:
        /// the connections that wedge in a suite run have been working for minutes. Two of the three
        /// wedges in the traced run sat at outbound offsets 15 491 and 15 475 — 16 bytes apart, which
        /// looks far more like a boundary than the "not a byte boundary" the earlier write-up recorded.
        /// So this holds ONE connection and works it until something breaks, reporting the command index
        /// and elapsed time of the first stall. Run it with TIK4NET_WIRETRACE set to read the byte offset
        /// the stall lands on.
        /// </para>
        /// </summary>
        [TestMethod]
        public void Probe_LongLivedSession_HowMuchTrafficBeforeItWedges()
        {
            int commands = int.TryParse(
                Environment.GetEnvironmentVariable("TIK4NET_SOAK_COMMANDS"), out int n) && n > 0
                ? n : 400;

            Log("=== P2.55 soak: one connection, " + commands + " commands ===");
            using (var conn = Open(TikConnectionType.WinboxCliMac))
            {
                var total = Stopwatch.StartNew();
                int stalls = 0;
                for (int i = 1; i <= commands; i++)
                {
                    var sw = Stopwatch.StartNew();
                    try { Poke(conn); }
                    catch (Exception ex)
                    {
                        Log("  #" + i + " @" + total.Elapsed + "  THREW  " + Describe(ex));
                        return;
                    }
                    sw.Stop();
                    if (sw.ElapsedMilliseconds > 2000)
                    {
                        stalls++;
                        Log("  #" + i + " @" + total.Elapsed + "  STALL " + sw.ElapsedMilliseconds + " ms");
                    }
                }
                Log("=== survived " + commands + " commands in " + total.Elapsed + ", stalls=" + stalls + " ===");
            }
        }
    }
}
