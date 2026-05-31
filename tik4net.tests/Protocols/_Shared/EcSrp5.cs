// EcSrp5.cs — Canonical Curve25519 Weierstrass + EC-SRP5 math
// One copy shared by all protocol clients. Extracted from WinboxM2CatalogTest.cs
// and MacLayerTest.cs (both held identical copies).
//
// Reference: subixonfire/winbox-terminal-protocol (MIT)

using System;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace tik4net.tests
{
    // Affine Weierstrass point on Curve25519.
    internal struct ECPoint
    {
        public static readonly ECPoint Infinity = new ECPoint { IsInfinity = true };
        public BigInteger X, Y;
        public bool IsInfinity;
    }

    /// <summary>
    /// Curve25519 Weierstrass arithmetic + EC-SRP5 key-derivation helpers.
    /// All methods are static and side-effect-free.
    /// </summary>
    internal static class EcSrp5
    {
        // p = 2^255 - 19
        internal static readonly BigInteger P = HexToBI(
            "7fffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffed");
        // group order r
        internal static readonly BigInteger R = HexToBI(
            "1000000000000000000000000000000014def9dea2f79cd65812631a5cf5d3ed");
        // Weierstrass A
        internal static readonly BigInteger AW = HexToBI(
            "2aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa984914a144");
        // Montgomery A
        private static readonly BigInteger MONT_A = new BigInteger(486662);
        // A_SHIFT = 486662/3 mod P
        internal static readonly BigInteger A_SHIFT;
        // Generator in Weierstrass form (Curve25519 base point u=9, parity=0)
        internal static readonly ECPoint G;

        static EcSrp5()
        {
            A_SHIFT = MONT_A * ModInverse(3, P) % P;
            G = LiftX(9, 0);
        }

        // lift_x: given Montgomery x + parity, return Weierstrass ECPoint
        internal static ECPoint LiftX(BigInteger xMont, int parity)
        {
            xMont = ((xMont % P) + P) % P;
            BigInteger ySq = (BigInteger.ModPow(xMont, 3, P)
                + MONT_A * BigInteger.ModPow(xMont, 2, P) % P
                + xMont) % P;
            BigInteger xW = (xMont + A_SHIFT) % P;
            BigInteger[] ys = ModSqrt(ySq);
            if (ys == null) return ECPoint.Infinity;
            BigInteger y0 = ys[0], y1 = ys[1];
            BigInteger y = (parity == 0)
                ? ((y0 % 2 == 0) ? y0 : y1)
                : ((y0 % 2 == 1) ? y0 : y1);
            return new ECPoint { X = xW, Y = y };
        }

        // Convert Weierstrass point → Montgomery x (32 B BE) + parity
        internal static (byte[] xMontBytes, int parity) ToMontgomery(ECPoint pt)
        {
            BigInteger xM = ((pt.X - A_SHIFT) % P + P) % P;
            return (BigIntToBE32(xM), (int)(pt.Y % 2));
        }

        internal static (byte[] xMontBytes, int parity) GenPublicKey(byte[] privBytes)
        {
            BigInteger priv = BEToBI(privBytes);
            ECPoint pt = ECScalarMul(priv, G);
            return ToMontgomery(pt);
        }

        // redp1: deterministic point from 32-byte seed via double-SHA256 loop
        internal static ECPoint Redp1(byte[] xBytes, int parity)
        {
            byte[] x = Sha256(xBytes);
            while (true)
            {
                byte[] x2 = Sha256(x);
                ECPoint pt = LiftX(BEToBI(x2), parity);
                if (!pt.IsInfinity) return pt;
                x = BigIntToBE32(BEToBI(x) + 1);
            }
        }

        internal static ECPoint ECAdd(ECPoint p1, ECPoint p2)
        {
            if (p1.IsInfinity) return p2;
            if (p2.IsInfinity) return p1;
            if (p1.X == p2.X)
                return p1.Y == p2.Y ? ECDouble(p1) : ECPoint.Infinity;
            BigInteger dx = ((p2.X - p1.X) % P + P) % P;
            BigInteger dy = ((p2.Y - p1.Y) % P + P) % P;
            BigInteger lam = dy * ModInverse(dx, P) % P;
            BigInteger x3 = ((lam * lam - p1.X - p2.X) % P + P) % P;
            BigInteger y3 = ((lam * ((p1.X - x3 + P) % P) - p1.Y) % P + P) % P;
            return new ECPoint { X = x3, Y = y3 };
        }

        internal static ECPoint ECDouble(ECPoint pt)
        {
            if (pt.IsInfinity || pt.Y == 0) return ECPoint.Infinity;
            BigInteger num = (3 * BigInteger.ModPow(pt.X, 2, P) + AW) % P;
            BigInteger den = 2 * pt.Y % P;
            BigInteger lam = num * ModInverse(den, P) % P;
            BigInteger x3 = ((lam * lam - 2 * pt.X) % P + P) % P;
            BigInteger y3 = ((lam * ((pt.X - x3 + P) % P) - pt.Y) % P + P) % P;
            return new ECPoint { X = x3, Y = y3 };
        }

        internal static ECPoint ECScalarMul(BigInteger k, ECPoint pt)
        {
            ECPoint result = ECPoint.Infinity;
            ECPoint addend = pt;
            k = ((k % R) + R) % R;
            while (k > 0)
            {
                if ((k & 1) == 1) result = ECAdd(result, addend);
                addend = ECDouble(addend);
                k >>= 1;
            }
            return result;
        }

        // Tonelli-Shanks modular square root; returns [r, P-r] or null if not QR
        internal static BigInteger[] ModSqrt(BigInteger a)
        {
            a = ((a % P) + P) % P;
            if (a == 0) return new[] { BigInteger.Zero, BigInteger.Zero };
            if (BigInteger.ModPow(a, (P - 1) / 2, P) != 1) return null;

            int s = 0;
            BigInteger Q = P - 1;
            while (Q % 2 == 0) { Q /= 2; s++; }

            BigInteger z = 2;
            while (BigInteger.ModPow(z, (P - 1) / 2, P) != P - 1) z++;

            int M = s;
            BigInteger c = BigInteger.ModPow(z, Q, P);
            BigInteger t = BigInteger.ModPow(a, Q, P);
            BigInteger r = BigInteger.ModPow(a, (Q + 1) / 2, P);

            while (true)
            {
                if (t == 1) return new[] { r, P - r };
                int i = 1;
                BigInteger tmp = t * t % P;
                while (tmp != 1) { tmp = tmp * tmp % P; i++; }
                BigInteger b = BigInteger.ModPow(c, BigInteger.Pow(2, M - i - 1), P);
                M = i;
                c = b * b % P;
                t = t * c % P;
                r = r * b % P;
            }
        }

        internal static BigInteger ModInverse(BigInteger a, BigInteger p)
        {
            BigInteger r = ((a % p) + p) % p, r2 = p, s = 1, s2 = 0;
            while (r2 != 0)
            {
                BigInteger q = r / r2;
                BigInteger rt = r2; r2 = r - q * r2; r = rt;
                BigInteger st = s2; s2 = s - q * s2; s = st;
            }
            if (r != 1) throw new ArithmeticException("Inverse does not exist");
            return ((s % p) + p) % p;
        }

        // ── BigInteger / byte helpers ──────────────────────────────────────────

        internal static BigInteger BEToBI(byte[] be)
        {
            byte[] le = be.Reverse().Concat(new byte[] { 0 }).ToArray();
            return new BigInteger(le);
        }

        internal static byte[] BigIntToBE32(BigInteger n)
        {
            byte[] le = n.ToByteArray();
            if (le.Length > 32 && le[le.Length - 1] == 0)
                le = le.Take(le.Length - 1).ToArray();
            byte[] be = le.Reverse().ToArray();
            if (be.Length >= 32) return be.Take(32).ToArray();
            byte[] padded = new byte[32];
            Buffer.BlockCopy(be, 0, padded, 32 - be.Length, be.Length);
            return padded;
        }

        internal static BigInteger HexToBI(string hex)
        {
            byte[] be = Enumerable.Range(0, hex.Length / 2)
                .Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
            return BEToBI(be);
        }

        // ── Hash helpers ───────────────────────────────────────────────────────

        internal static byte[] Sha256(byte[] data)
        {
            using (var h = SHA256.Create()) return h.ComputeHash(data);
        }

        internal static byte[] Sha1(byte[] data)
        {
            using (var h = SHA1.Create()) return h.ComputeHash(data);
        }

        // ── EC-SRP5 password validator ─────────────────────────────────────────

        internal static byte[] GenPasswordValidatorPriv(string user, string pass, byte[] salt)
        {
            byte[] inner = Sha256(Encoding.UTF8.GetBytes(user + ":" + pass));
            return Sha256(salt.Concat(inner).ToArray());
        }
    }
}
