using System;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Crypto;
using ECPoint = tik4net.Crypto.ECPoint;

namespace tik4net.unittests
{
    /// <summary>
    /// Router-free coverage of the EC-SRP5 math behind the WinBox handshake (P2.41).
    /// </summary>
    /// <remarks>
    /// The handshake fails roughly once per few hundred opens and every layer above it reports the
    /// failure as "wrong username or password", which is a diagnosis nothing checks. Before reading
    /// bytes off a live router it is worth asking whether the arithmetic itself has a rare input it
    /// gets wrong — a one-in-256 event is exactly the shape of the observed failure rate, and it is
    /// the kind of thing a fixed-size big-endian conversion classically gets wrong.
    /// <para>The protocol is symmetric enough to check offline. The client computes
    /// <c>z = (i·j + a)·(wB + v)</c>; the router, holding <c>V = i·G</c> and the secret <c>b</c> behind
    /// <c>wB = b·G − v</c>, must reach the same point as <c>z = b·(j·V + wA)</c>. Both sides are
    /// expressed here with our own primitives, so the two different orders of operations act as each
    /// other's oracle: any input where <see cref="EcSrp5.ECAdd"/>, <see cref="EcSrp5.ECDouble"/>,
    /// <see cref="EcSrp5.LiftX"/> or <see cref="EcSrp5.BigIntToBE32"/> misbehaves makes the two
    /// diverge, even though a single side computes something self-consistent.</para>
    /// <para>The iteration count is deliberately modest so the suite stays quick; set
    /// <c>TIK4NET_ECSRP5_ITERATIONS</c> to sweep far enough to see a one-in-256 event (a few thousand
    /// is a couple of minutes) when this test is the one under suspicion again.</para>
    /// </remarks>
    [TestClass]
    public class EcSrp5RoundTripTests
    {
        private const int DefaultIterations = 60;

        private static int Iterations
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("TIK4NET_ECSRP5_ITERATIONS");
                return int.TryParse(env, out int n) && n > 0 ? n : DefaultIterations;
            }
        }

        /// <summary>
        /// Client and router must derive the same shared point from every random
        /// (client key, router key, salt) triple — no input may make the two orders of operations
        /// disagree.
        /// </summary>
        [TestMethod]
        public void EcSrp5_ClientAndServerDeriveSameSecret_ForRandomInputs()
        {
            var rng = RandomNumberGenerator.Create();
            int iterations = Iterations;

            for (int i = 0; i < iterations; i++)
            {
                byte[] privA = Random32(rng);
                byte[] privB = Random32(rng);
                byte[] salt = new byte[16];
                rng.GetBytes(salt);

                string clientZ = Hex(ClientSecret(privA, privB, salt, out string serverZ));

                Assert.AreEqual(serverZ, clientZ,
                    $"EC-SRP5 secrets diverged on iteration {i}. " +
                    $"privA={Hex(privA)} privB={Hex(privB)} salt={Hex(salt)}");
            }
        }

        /// <summary>
        /// The wire form of every scalar is a fixed 32 bytes, and both sides hash those bytes.
        /// A value that happens to be small must therefore still serialize to 32 bytes, and must
        /// survive the round trip back to a number — dropping a leading zero would silently change
        /// what gets hashed on one side only.
        /// </summary>
        [TestMethod]
        public void BigIntToBE32_IsFixedWidthAndRoundTrips()
        {
            var rng = RandomNumberGenerator.Create();

            for (int i = 0; i < 4096; i++)
            {
                byte[] bytes = Random32(rng);
                // Force the top bytes to zero on part of the sample: those are the values a
                // variable-width conversion would shorten, and they are ~1-in-256 in random traffic.
                if (i % 3 == 0) bytes[0] = 0;
                if (i % 9 == 0) { bytes[0] = 0; bytes[1] = 0; }
                // Also cover the other end: a top byte with the high bit set is where BigInteger
                // appends a sign byte, so the conversion has to drop it again.
                if (i % 5 == 0) bytes[0] = 0xFF;

                byte[] round = EcSrp5.BigIntToBE32(EcSrp5.BEToBI(bytes));

                Assert.AreEqual(32, round.Length, $"width changed for {Hex(bytes)}");
                CollectionAssert.AreEqual(bytes, round, $"round trip changed {Hex(bytes)}");
            }
        }

        /// <summary>
        /// A public key travels as x plus a parity bit, so re-deriving the point from those two must
        /// give back exactly the point that was sent — on both sides of the handshake.
        /// </summary>
        [TestMethod]
        public void LiftX_RoundTripsThroughToMontgomery()
        {
            var rng = RandomNumberGenerator.Create();

            for (int i = 0; i < 200; i++)
            {
                var (x, parity) = EcSrp5.GenPublicKey(Random32(rng));
                ECPoint pt = EcSrp5.LiftX(EcSrp5.BEToBI(x), parity);

                Assert.IsFalse(pt.IsInfinity, $"lift_x lost the point for x={Hex(x)} parity={parity}");
                var (x2, parity2) = EcSrp5.ToMontgomery(pt);

                CollectionAssert.AreEqual(x, x2, $"x changed for {Hex(x)}");
                Assert.AreEqual(parity, parity2, $"parity changed for {Hex(x)}");
            }
        }

        // ── the handshake, both halves ────────────────────────────────────────

        /// <summary>
        /// Runs one exchange and returns the client's shared point, with the router's in
        /// <paramref name="serverZ"/>. The client half is a transcription of
        /// <c>WinboxM2Session.EcSrp5Auth</c>; keep it that way, or the test stops testing the code
        /// that ships.
        /// </summary>
        private static byte[] ClientSecret(byte[] privA, byte[] privB, byte[] salt, out string serverZ)
        {
            const string user = "admin";
            const string pass = "";

            var (xWA, parityA) = EcSrp5.GenPublicKey(privA);

            // ── router: holds V = i·G and the salt, picks b, publishes wB = b·G − v ──
            byte[] valPriv = EcSrp5.GenPasswordValidatorPriv(user, pass, salt);
            var (xGamma, _) = EcSrp5.GenPublicKey(valPriv);
            ECPoint v = EcSrp5.Redp1(xGamma, 1);
            ECPoint bigV = EcSrp5.ECScalarMul(EcSrp5.BEToBI(valPriv), EcSrp5.G);
            ECPoint wBpt = EcSrp5.ECAdd(EcSrp5.ECScalarMul(EcSrp5.BEToBI(privB), EcSrp5.G), Negate(v));
            var (xWB, parityB) = EcSrp5.ToMontgomery(wBpt);

            // ── client: exactly what WinboxM2Session.EcSrp5Auth does with the challenge ──
            ECPoint wB = EcSrp5.LiftX(EcSrp5.BEToBI(xWB), parityB);
            ECPoint sum = EcSrp5.ECAdd(wB, v);
            byte[] j = EcSrp5.Sha256(xWA.Concat(xWB).ToArray());
            BigInteger vh = (EcSrp5.BEToBI(valPriv) * EcSrp5.BEToBI(j) % EcSrp5.R
                             + EcSrp5.BEToBI(privA)) % EcSrp5.R;
            var (clientZ, _) = EcSrp5.ToMontgomery(EcSrp5.ECScalarMul(vh, sum));

            // ── router: z = b·(j·V + wA) ──
            ECPoint wA = EcSrp5.LiftX(EcSrp5.BEToBI(xWA), parityA);
            ECPoint srvSum = EcSrp5.ECAdd(EcSrp5.ECScalarMul(EcSrp5.BEToBI(j), bigV), wA);
            var (srvZ, _) = EcSrp5.ToMontgomery(EcSrp5.ECScalarMul(EcSrp5.BEToBI(privB), srvSum));

            serverZ = Hex(srvZ);
            return clientZ;
        }

        private static ECPoint Negate(ECPoint pt)
            => pt.IsInfinity ? pt : new ECPoint { X = pt.X, Y = (EcSrp5.P - pt.Y) % EcSrp5.P };

        private static byte[] Random32(RandomNumberGenerator rng)
        {
            byte[] bytes = new byte[32];
            rng.GetBytes(bytes);
            return bytes;
        }

        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "");
    }
}
