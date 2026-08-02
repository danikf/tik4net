// WinboxHandshakeLoopProbeTest.cs — what actually fails when the WinBox handshake fails? (P2.41)
//
// The WinBox EC-SRP5 handshake fails roughly once per few hundred opens, and until this probe every
// layer above it reported the failure as "Cannot log in. Wrong username or password" — a diagnosis
// nothing in the code had checked. It was seen on three different entry points (WinboxCli, WinboxTcp,
// WinboxCliMac), so it is the shared handshake and not a quirk of one transport.
//
// A failure that rare cannot be caught by staring at the code, and it cannot be caught by a normal test
// run either: it lands somewhere in a 400-test suite, once, with no trace. So this opens one channel at
// a time in a tight loop with a wire trace running into a rolling buffer, and dumps that buffer only for
// the opens that fail. The interesting output is therefore the last few frames before a failure — which
// side stopped talking, and after which step.
//
// ── What this found, 2026-07-30 on RouterOS 7.23.2 ───────────────────────────────────────────────────
//
// The router refuses about 0.5-1% of WinBox logins that use correct credentials. Where the 32-byte
// EC-SRP5 confirmation digest belongs it puts 33 bytes of ASCII — "invalid user name or password (6)" —
// and writes a matching "login failure ... via winbox" to its own log. So the message was never
// fabricated; it was the router's own words, and nobody had read them.
//
// It is not our request that is at fault. Probe_WinboxHandshake_SameKeyRetry replays the IDENTICAL
// client key straight after a refusal and it is accepted, 9 times out of 9 — the only thing that differs
// between the refusal and the acceptance is the router's own ephemeral key. Two tempting hypotheses died
// here and are kept as methods so they are not chased again: a leading zero byte in the client public
// key (1-in-256, which matched the observed rate almost exactly, and was a coincidence), and attempt
// frequency (no trend across 0 / 250 / 1000 ms gaps).
//
// Since a genuinely wrong password produces the same message, content cannot separate the two and only
// persistence can — hence Winbox.RouterLoginRetry. Verification runs through Probe_WinboxHandshake_-
// RepeatedOpens, which counts the refusals the retry absorbed: a green run alone would be worthless,
// because "the router never refused" and "we swallowed it" look identical from the outside.
//
// Cost: TIK4NET_HANDSHAKE_CYCLES opens per channel, serial. WinboxNative/WinboxNativeMac ~0.5 s each,
// WinboxCli ~1.4 s, WinboxCliMac ~11 s. Seeing the ~1% event needs a couple of hundred per channel.
// Nothing is left on the router — every channel is disposed, including on the failure path.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using tik4net.Diagnostics;
using tik4net.Winbox;

namespace tik4net.integrationtests
{
    // MSTest skips [Ignore] even under --filter, so comment it out to run these.
    [Ignore("Ad-hoc handshake-reliability probe against a live router — comment out to run.")]
    [TestClass]
    public class WinboxHandshakeLoopProbeTest
    {
        private const int DefaultCycles = 25;

        /// <summary>How many trace events to keep. Only the tail before a failure is ever read.</summary>
        private const int TraceRingSize = 60;

        private static readonly TikConnectionType[] WinboxTransports =
        {
            TikConnectionType.WinboxCli,
            TikConnectionType.WinboxNative,
            TikConnectionType.WinboxCliMac,
            TikConnectionType.WinboxNativeMac,
        };

        private static void Log(string line)
        {
            Console.WriteLine(line);
            string path = Environment.GetEnvironmentVariable("TIK4NET_PROBE_LOG");
            if (!string.IsNullOrEmpty(path))
                try { System.IO.File.AppendAllText(path, line + Environment.NewLine); } catch { }
        }

        /// <summary>
        /// Keeps the last <see cref="TraceRingSize"/> events. A full trace of several hundred opens is
        /// both enormous and useless — what matters is the handful of frames leading into a failure.
        /// </summary>
        private sealed class RingSink : ITikWireTraceSink
        {
            private readonly Queue<string> _events = new Queue<string>();
            private readonly object _lock = new object();

            /// <summary>Refusals the retry absorbed — see <see cref="RetriesAbsorbed"/>.</summary>
            private int _retries;

            /// <summary>
            /// How many router refusals were swallowed by the login retry. Without this a green run is
            /// ambiguous: it could mean the router never refused, or that it refused and the retry
            /// covered it — and only the second one shows the fix doing work.
            /// </summary>
            public int RetriesAbsorbed { get { lock (_lock) return _retries; } }

            public void Emit(string channel, TikWireDir dir, byte[] data, int offset, int count, string note)
            {
                string line = $"  {DateTime.Now:HH:mm:ss.fff} {channel,-14} {dir,-4} " +
                              $"{count,4}B {note}" +
                              (count > 0 && count <= 64 ? "  " + TikWireTrace.Escape(data, offset, count) : "");
                lock (_lock)
                {
                    if (note != null && note.IndexOf("login refused", StringComparison.Ordinal) >= 0)
                    {
                        _retries++;
                        Log($"  {DateTime.Now:HH:mm:ss} retry absorbed a refusal: {note}");
                    }
                    _events.Enqueue(line);
                    while (_events.Count > TraceRingSize) _events.Dequeue();
                }
            }

            public IEnumerable<string> Drain()
            {
                lock (_lock)
                {
                    var copy = _events.ToArray();
                    _events.Clear();
                    return copy;
                }
            }
        }

        private static ITikConnection OpenOne(TikConnectionType type)
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

        /// <summary>Every exception in the chain, so the real cause is not hidden behind the wrapper.</summary>
        private static string Describe(Exception ex)
        {
            var parts = new List<string>();
            for (Exception e = ex; e != null; e = e.InnerException)
                parts.Add(e.GetType().Name + ": " + e.Message.Split('\n')[0].Trim());
            return string.Join("  <- ", parts);
        }

        [TestMethod]
        public void Probe_WinboxHandshake_RepeatedOpens()
        {
            int cycles = int.TryParse(
                Environment.GetEnvironmentVariable("TIK4NET_HANDSHAKE_CYCLES"), out int n) && n > 0
                ? n : DefaultCycles;

            string only = Environment.GetEnvironmentVariable("TIK4NET_PROBE_TRANSPORTS");
            var selected = string.IsNullOrEmpty(only)
                ? WinboxTransports
                : WinboxTransports.Where(t => only.Split(',').Any(
                    s => string.Equals(s.Trim(), t.ToString(), StringComparison.OrdinalIgnoreCase))).ToArray();

            var sink = new RingSink();
            var failuresByKind = new Dictionary<string, int>(StringComparer.Ordinal);
            int totalOpens = 0, totalFailures = 0;

            using (TikWireTrace.Capture(sink))
            {
                foreach (var type in selected)
                {
                    int ok = 0;
                    var sw = System.Diagnostics.Stopwatch.StartNew();

                    for (int i = 0; i < cycles; i++)
                    {
                        totalOpens++;
                        try
                        {
                            using (var conn = OpenOne(type))
                                conn.CallCommandSync("/system/identity/print").Count();
                            ok++;
                            sink.Drain();   // a successful open teaches nothing; keep the ring for the next
                        }
                        catch (Exception ex)
                        {
                            totalFailures++;
                            string kind = InnermostMessage(ex);
                            failuresByKind[kind] = failuresByKind.TryGetValue(kind, out int c) ? c + 1 : 1;

                            Log($"{DateTime.Now:HH:mm:ss}  FAIL {type} open #{i + 1}");
                            Log($"    {Describe(ex)}");
                            Log("    last wire events before the failure:");
                            foreach (string line in sink.Drain()) Log(line);
                            Log("");
                        }
                    }

                    sw.Stop();
                    Log($"{type,-16}: {ok}/{cycles} opens OK in {sw.Elapsed.TotalSeconds:F0}s " +
                        $"({sw.ElapsedMilliseconds / Math.Max(1, cycles)} ms each)");
                }
            }

            Log("");
            Log($"total {totalOpens} opens, {totalFailures} failures, " +
                $"{sink.RetriesAbsorbed} router refusal(s) absorbed by the login retry");
            foreach (var kv in failuresByKind.OrderByDescending(k => k.Value))
                Log($"    {kv.Value,4}x  {kv.Key}");

            // Deliberately not an assert on zero failures of every kind: the probe exists to characterise
            // rare behaviour, and catching an unusual one is a result rather than a regression. What must
            // not survive is a credential verdict against a router whose credentials are fixed and known
            // good — the retry is supposed to have absorbed the transient refusals, so one reaching the
            // caller means either the retry stopped working or something new is producing a wrong secret
            // (EcSrp5RoundTripTests covers the arithmetic side of that offline).
            Assert.IsFalse(
                failuresByKind.Keys.Any(k =>
                    k.IndexOf("Wrong username or password", StringComparison.Ordinal) >= 0 ||
                    k.IndexOf("refused the WinBox login", StringComparison.Ordinal) >= 0),
                "A login against known-good credentials was reported as a credential failure, after " +
                $"{RouterLoginRetry.MaxAttempts} attempts.");
        }

        private static string InnermostMessage(Exception ex)
        {
            Exception e = ex;
            while (e.InnerException != null) e = e.InnerException;
            return e.GetType().Name + ": " + e.Message.Split('\n')[0].Trim();
        }

        // ── The trigger, forced ───────────────────────────────────────────────

        /// <summary>
        /// Tests whether a zero first byte in the client public key triggers the rejection.
        /// <b>It does not</b> — kept so the hypothesis is not chased again.
        /// </summary>
        /// <remarks>
        /// The first captured failure happened to carry a client public key beginning with 0x00, which
        /// occurs in one random key in 256 and so matched the observed failure rate almost exactly. The
        /// correlation was a coincidence: forcing the property gives 4 successes in 5, and forcing its
        /// absence still produced rejections. What the experiment did establish is far more useful —
        /// the over-long reply is not a mangled digest at all but 33 bytes of ASCII,
        /// <c>"invalid user name or password (6)"</c>. The router is stating a refusal in its own words,
        /// so the failure is a genuine router-side rejection of credentials that are correct, not a
        /// desynchronized frame.
        /// </remarks>
        [TestMethod]
        public void Probe_WinboxHandshake_LeadingZeroPublicKey()
        {
            Log("arm A: client public keys WITHOUT a leading zero byte");
            for (int i = 0; i < 5; i++) Log("  " + RawTcpHandshake(FindKey(leadingZero: false)));

            Log("");
            Log("arm B: client public keys WITH a leading zero byte");
            for (int i = 0; i < 5; i++) Log("  " + RawTcpHandshake(FindKey(leadingZero: true)));
        }

        /// <summary>
        /// Measures how the rejection rate responds to the gap between login attempts.
        /// </summary>
        /// <remarks>
        /// The rejection is the router's own, with correct credentials, so the question becomes what
        /// makes the router refuse a login it accepts moments later. Attempt frequency is the first
        /// thing to rule in or out: a tight probe loop produced roughly one rejection in ten, while a
        /// full test suite produces about one in four hundred, and that is a large enough gap to be a
        /// signal rather than sampling noise. If the rate falls away as the gap grows, the router is
        /// throttling and the fix is ours to make in how we open connections; if it does not move, the
        /// cause is elsewhere and this arm has cost a few minutes.
        /// </remarks>
        [TestMethod]
        public void Probe_WinboxHandshake_RejectionRateVsAttemptGap()
        {
            const int attempts = 40;
            foreach (int gapMs in new[] { 0, 250, 1000 })
            {
                int rejected = 0, errors = 0;
                var firstFailures = new List<int>();
                var sw = System.Diagnostics.Stopwatch.StartNew();

                for (int i = 0; i < attempts; i++)
                {
                    string result;
                    try { result = RawTcpHandshake(FindKey(leadingZero: false, search: false)); }
                    catch (Exception ex) { errors++; result = "EXCEPTION " + ex.Message.Split('\n')[0]; }

                    if (result.IndexOf("digest NOT found", StringComparison.Ordinal) >= 0
                        || result.IndexOf("EXCEPTION", StringComparison.Ordinal) >= 0)
                    {
                        rejected++;
                        if (firstFailures.Count < 6) firstFailures.Add(i + 1);
                    }
                    if (gapMs > 0) System.Threading.Thread.Sleep(gapMs);
                }
                sw.Stop();

                Log($"gap {gapMs,4} ms: {rejected}/{attempts} rejected ({errors} threw) in " +
                    $"{sw.Elapsed.TotalSeconds:F0}s" +
                    (firstFailures.Count > 0 ? "  at attempts " + string.Join(",", firstFailures) : ""));
            }
        }

        /// <summary>
        /// Searches for a private key whose public x-coordinate starts with (or avoids) 0x00. With
        /// <paramref name="search"/> false it just returns a random key — the search costs ~256 scalar
        /// multiplications, which would pace any loop that is trying to measure timing.
        /// </summary>
        private static byte[] FindKey(bool leadingZero, bool search = true)
        {
            var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            byte[] priv = new byte[32];
            while (true)
            {
                rng.GetBytes(priv);
                if (!search) return (byte[])priv.Clone();
                var (x, _) = tik4net.Crypto.EcSrp5.GenPublicKey(priv);
                if ((x[0] == 0) == leadingZero) return (byte[])priv.Clone();
            }
        }

        /// <summary>
        /// One EC-SRP5 exchange over a bare TCP socket with a caller-chosen private key, reporting the
        /// server's confirmation frame verbatim. Deliberately not routed through WinboxM2Session: the
        /// question here is what the router puts on the wire, so nothing should be able to normalise it
        /// on the way.
        /// </summary>
        private static string RawTcpHandshake(byte[] privA)
        {
            var (xWA, parityA) = tik4net.Crypto.EcSrp5.GenPublicKey(privA);
            var transport = new tik4net.Winbox.WinboxTcpTransport();
            transport.Connect(ConfigurationManager.AppSettings["host"], 8291, 15000, 15000);
            try
            {
                string user = ConfigurationManager.AppSettings["user"];
                string pass = ConfigurationManager.AppSettings["pass"] ?? "";

                byte[] hello = System.Text.Encoding.UTF8.GetBytes(user)
                    .Concat(new byte[] { 0 }).Concat(xWA).Concat(new byte[] { (byte)parityA }).ToArray();
                WriteFrame(transport, hello);

                byte[] chHdr = transport.ReadExact(2);
                if (chHdr[1] != 0x06) return $"xWA[0]=0x{xWA[0]:x2}  challenge tag=0x{chHdr[1]:x2} (unexpected)";
                byte[] challenge = transport.ReadExact(chHdr[0]);
                if (challenge.Length != 49)
                    return $"xWA[0]=0x{xWA[0]:x2}  challenge len={challenge.Length} (expected 49)";

                byte[] xWB = challenge.Take(32).ToArray();
                int parityB = challenge[32];
                byte[] salt = challenge.Skip(33).Take(16).ToArray();

                byte[] valPriv = tik4net.Crypto.EcSrp5.GenPasswordValidatorPriv(user, pass, salt);
                var (xGamma, _) = tik4net.Crypto.EcSrp5.GenPublicKey(valPriv);
                var v = tik4net.Crypto.EcSrp5.Redp1(xGamma, 1);
                var wB = tik4net.Crypto.EcSrp5.LiftX(tik4net.Crypto.EcSrp5.BEToBI(xWB), parityB);
                var sum = tik4net.Crypto.EcSrp5.ECAdd(wB, v);

                byte[] j = tik4net.Crypto.EcSrp5.Sha256(xWA.Concat(xWB).ToArray());
                var vh = (tik4net.Crypto.EcSrp5.BEToBI(valPriv) * tik4net.Crypto.EcSrp5.BEToBI(j)
                          % tik4net.Crypto.EcSrp5.R + tik4net.Crypto.EcSrp5.BEToBI(privA))
                         % tik4net.Crypto.EcSrp5.R;
                var (zMont, _) = tik4net.Crypto.EcSrp5.ToMontgomery(
                    tik4net.Crypto.EcSrp5.ECScalarMul(vh, sum));

                byte[] clientCc = tik4net.Crypto.EcSrp5.Sha256(j.Concat(zMont).ToArray());
                WriteFrame(transport, clientCc);

                byte[] hdr = transport.ReadExact(2);
                byte[] reply = transport.ReadExact(hdr[0]);
                byte[] expected = tik4net.Crypto.EcSrp5.Sha256(
                    j.Concat(clientCc).Concat(zMont).ToArray());

                // Where does the expected digest sit in an over-long reply — at the front (trailing
                // junk), or shifted by one (a prefix byte)? The answer says whether the digests agree
                // at all, i.e. whether this is a framing problem or a cryptographic one.
                string placement =
                    reply.Take(32).SequenceEqual(expected) ? "expected digest at offset 0" :
                    reply.Skip(1).Take(32).SequenceEqual(expected) ? "expected digest at offset 1" :
                    reply.SequenceEqual(expected) ? "exact match" : "digest NOT found in reply";

                return $"xWA[0]=0x{xWA[0]:x2}  xWB[0]=0x{xWB[0]:x2}  reply len={hdr[0]} tag=0x{hdr[1]:x2}  " +
                       $"{placement}  bytes={BitConverter.ToString(reply)}";
            }
            finally { transport.Dispose(); }
        }

        /// <summary>
        /// Asks whether a rejected login is rejected again a moment later — over the binary API, which
        /// shares no authentication code with WinBox at all.
        /// </summary>
        /// <remarks>
        /// The router's own log settles who is refusing: it records
        /// <c>login failure for user admin ... via winbox</c> for every one of these, matching the probe
        /// failures one for one. So the credentials really are being rejected by the router — our
        /// message was not invented, only unexplained — and the useful question becomes whether the
        /// refusal is durable. A refusal that clears on the very next attempt is transient router state
        /// and can be ridden out; one that persists is a real lockout that must be reported and never
        /// retried. The two call for opposite behaviour, so nothing can be fixed before this is known.
        /// <para>Both transports are measured because the log also carries a single <c>via api</c>
        /// failure. One sample proves nothing — it could not be attributed to any client of ours — so
        /// the API arm is here to settle it rather than to be assumed either way.</para>
        /// </remarks>
        [TestMethod]
        public void Probe_Login_RejectionIsTransient()
        {
            RetryArm("API", 400, () => TryApiLogin(out string _));
            RetryArm("WinBox EC-SRP5", 400,
                () => RawTcpHandshake(FindKey(leadingZero: false, search: false))
                          .IndexOf("digest NOT found", StringComparison.Ordinal) < 0);
        }

        /// <summary>
        /// The discriminator: after a refusal, replays the handshake with the <b>same</b> client key.
        /// </summary>
        /// <remarks>
        /// Everything so far is consistent with two opposite stories — our client occasionally sends a
        /// key the router is right to refuse, or the router occasionally refuses a key it should accept.
        /// The earlier retry test cannot tell them apart, because it retried with a fresh random key and
        /// so changed the one variable under suspicion. Replaying the identical key separates them
        /// cleanly: if the same bytes are accepted a moment later, the bytes were never the problem.
        /// </remarks>
        [TestMethod]
        public void Probe_WinboxHandshake_SameKeyRetry()
        {
            const int maxAttempts = 600;
            int found = 0;

            for (int i = 0; i < maxAttempts && found < 3; i++)
            {
                byte[] key = FindKey(leadingZero: false, search: false);
                string first = RawTcpHandshake(key);
                if (first.IndexOf("digest NOT found", StringComparison.Ordinal) < 0) continue;

                found++;
                Log($"rejection at attempt {i + 1}; replaying the SAME client key:");
                Log($"    original : {first}");
                for (int r = 1; r <= 3; r++)
                {
                    System.Threading.Thread.Sleep(50);
                    Log($"    replay {r} : {RawTcpHandshake(key)}");
                }
                Log("");
            }

            Log(found == 0
                ? $"no rejection in {maxAttempts} attempts — inconclusive, run again"
                : $"{found} rejection(s) replayed");
        }

        /// <summary>Repeats <paramref name="login"/>, and on each refusal retries at once to see if it clears.</summary>
        private static void RetryArm(string name, int attempts, Func<bool> login)
        {
            const int maxRetries = 10;
            int rejected = 0, unrecovered = 0;
            var retriesNeeded = new List<int>();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            for (int i = 0; i < attempts; i++)
            {
                bool ok;
                try { ok = login(); }
                catch (Exception ex) { ok = false; Log($"  attempt {i + 1} threw: {InnermostMessage(ex)}"); }
                if (ok) continue;

                rejected++;
                int retries = 0;
                while (retries < maxRetries)
                {
                    retries++;
                    System.Threading.Thread.Sleep(50);
                    bool recovered;
                    try { recovered = login(); } catch { recovered = false; }
                    if (recovered) break;
                }
                if (retries >= maxRetries) unrecovered++;
                retriesNeeded.Add(retries);
                Log($"  {name} attempt {i + 1}: rejected, cleared after {retries} " +
                    $"retr{(retries == 1 ? "y" : "ies")}");
            }
            sw.Stop();

            Log($"{name}: {attempts} logins in {sw.Elapsed.TotalSeconds:F0}s — {rejected} rejected " +
                $"({100.0 * rejected / attempts:F1}%), {unrecovered} never cleared within {maxRetries} retries");
            if (retriesNeeded.Count > 0)
                Log($"    retries needed: {string.Join(",", retriesNeeded)}");
            Log("");
        }

        /// <summary>One API open/close. Returns false with the router's wording on a login refusal.</summary>
        private static bool TryApiLogin(out string error)
        {
            error = null;
            try
            {
                using (var conn = ConnectionFactory.CreateConnection(TikConnectionType.Api))
                {
                    conn.Open(ConfigurationManager.AppSettings["host"],
                              ConfigurationManager.AppSettings["user"],
                              ConfigurationManager.AppSettings["pass"] ?? "");
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = InnermostMessage(ex);
                return false;
            }
        }

        private static void WriteFrame(tik4net.Winbox.WinboxTcpTransport transport, byte[] payload)
        {
            byte[] frame = new byte[] { (byte)payload.Length, 0x06 }.Concat(payload).ToArray();
            transport.Stream.Write(frame, 0, frame.Length);
        }
    }
}
