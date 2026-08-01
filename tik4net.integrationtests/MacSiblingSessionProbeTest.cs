// MacSiblingSessionProbeTest.cs — does a second MAC session's teardown kill the first? (P2.55)
//
// P2.54 made the WinBox-CLI-MAC wedge survivable (reopen + retry) without explaining it. Three traced
// full runs pin the shape of the thing precisely: exactly THREE sessions are dropped per suite run, in
// the SAME three tests every time (Create_IpAddress_With_LowLevel_API, ListRadiusServersWillNotFail,
// SearchByName_Interface_WillWork). It is deterministic, not a background rate the retry now hides.
//
// ── The one reproduction found so far, 2026-08-01 on RouterOS 7.23.2 ────────────────────────────────
//
// Three MSTest methods, 10 s, reliably wedge one of the three (down from a 340-test suite run):
//
//     ListRoutingTablesWillNotFail            establishes the shared WinboxCliMac session
//     SafeMode_DisconnectWithoutRelease_RollsBack   own connection: SafeModeTake, add, Close
//     SearchByName_Interface_WillWork         the shared session's next command is never acknowledged
//
// Order matters: run the Safe Mode step FIRST, before the held session exists, and nothing happens.
//
// BUT Probe_SafeModeRollbackOnASibling, which performs exactly that sequence in library terms, does
// NOT reproduce it — twice, once with a read-only Safe Mode section and once with a real change to roll
// back (223/224 ms, healthy). So "a sibling's Safe Mode rollback kills the held session" is NOT the
// mechanism, tempting as the test sequence makes it look. Something the test path has and the probe
// does not is still missing — the test also recreates its own connection afterwards and polls for up
// to 30 s for the rollback to land, while the shared session sits untouched.
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
//     MAC-Telnet and Api siblings, zero wedges.
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
// Env: TIK4NET_SIBLING_CYCLES (default 3 rounds of each disturbance).
// Cost: roughly 15 s per cycle. Nothing is left on the router — reads only, everything disposed.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Configuration;
using System.Diagnostics;
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
        /// held session exists, and nothing happens — which is why
        /// <see cref="Probe_SiblingSessionTeardown_DoesNotKillTheHeldSession"/> found nothing. It tears a
        /// sibling down, but a plain sibling, and a plain sibling is harmless. What is not harmless is a
        /// rollback, which is a router-wide operation and evidently reaches other consoles.
        /// </para>
        /// <para>This accounts for ONE of the three wedges in a suite run. The other two involve no Safe
        /// Mode at all and are still unexplained.</para>
        /// </summary>
        [TestMethod]
        public void Probe_SafeModeRollbackOnASibling_KillsTheHeldSession()
        {
            Log("=== P2.55 minimal repro: Safe Mode rollback on a sibling ===");
            using (var held = Open(TikConnectionType.WinboxCliMac))
            {
                Poke(held);
                Poke(held);   // give the session some history, as the suite's shared connection has

                // Mirrors SafeModeTest.SafeMode_DisconnectWithoutRelease_RollsBack. The change inside Safe
                // Mode is not decoration: a first attempt that only READ inside Safe Mode did not reproduce
                // (224 ms, healthy), because a rollback with nothing to roll back evidently costs the router
                // nothing. What the held session cannot survive is the rollback actually doing work.
                string name = "p255-probe-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                var sibling = Open(TikConnectionType.WinboxCliMac);
                try
                {
                    sibling.SafeModeTake();
                    sibling.CreateCommandAndParameters("/ppp/secret/add", "name", name).ExecuteNonQuery();
                    sibling.Close();        // dropped while still holding Safe Mode -> RouterOS rolls back
                }
                finally { try { sibling.Dispose(); } catch { } }

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
