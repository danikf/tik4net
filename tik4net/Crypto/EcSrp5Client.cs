using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using tik4net.Winbox;

namespace tik4net.Crypto
{
    /// <summary>
    /// The client half of the EC-SRP5 login, with no I/O: the hello it sends and the answer to the router's
    /// challenge. WinBox over TCP, WinBox over MAC and MAC-Telnet all run this; they differ only in how the hello
    /// goes out and how the challenge and the confirmation come back, which stays in each carrier.
    /// </summary>
    internal sealed class EcSrp5Client
    {
        private readonly byte[] _privA, _xWA;

        /// <param name="loginName">The name sent in the hello, login options included (<c>user+r</c>).</param>
        /// <param name="privateKey">
        /// A fixed 32-byte client private key — the known-answer vectors' seam. Null draws a random one.
        /// </param>
        internal EcSrp5Client(string loginName, byte[]? privateKey = null)
        {
            _privA = privateKey ?? RandomKey();
            var (xWA, parityA) = EcSrp5.GenPublicKey(_privA);
            _xWA = xWA;
            Hello = Encoding.UTF8.GetBytes(loginName).Concat(new byte[] { 0 }).Concat(xWA)
                .Concat(new[] { (byte)parityA }).ToArray();
        }

        /// <summary><c>[login name]\0[xWA 32][parity]</c> — the same payload on all three carriers.</summary>
        internal byte[] Hello { get; }

        /// <summary>The length of the router's challenge: <c>[xWB 32][parityB][salt 16]</c>.</summary>
        internal const int ChallengeLength = 49;

        /// <summary>Answers the router's challenge.</summary>
        /// <param name="challenge">The <see cref="ChallengeLength"/>-byte challenge.</param>
        /// <param name="password">The password.</param>
        /// <param name="validatorName">
        /// Whose password the validator is built from. The WinBox carriers pass the account name alone
        /// (<see cref="EcSrp5.AccountName"/>: RouterOS leaves login options out of it, Docs/findings-romon.md "Agent
        /// login"); MAC-Telnet passes the whole login name.
        /// </param>
        internal EcSrp5Proof Answer(byte[] challenge, string password, string validatorName)
        {
            if (challenge == null || challenge.Length != ChallengeLength)
                throw new ArgumentException(
                    $"An EC-SRP5 challenge is {ChallengeLength} bytes, not {challenge?.Length.ToString() ?? "null"}.",
                    nameof(challenge));

            byte[] xWB  = challenge.Take(32).ToArray();
            int parityB = challenge[32];
            byte[] salt = challenge.Skip(33).Take(16).ToArray();

            byte[] valPriv  = EcSrp5.GenPasswordValidatorPriv(validatorName, password, salt);
            var (xGamma, _) = EcSrp5.GenPublicKey(valPriv);
            ECPoint v   = EcSrp5.Redp1(xGamma, 1);
            ECPoint wB  = EcSrp5.LiftX(EcSrp5.BEToBI(xWB), parityB);
            ECPoint sum = EcSrp5.ECAdd(wB, v);

            byte[] j = EcSrp5.Sha256(_xWA.Concat(xWB).ToArray());
            var vh   = (EcSrp5.BEToBI(valPriv) * EcSrp5.BEToBI(j) % EcSrp5.R + EcSrp5.BEToBI(_privA)) % EcSrp5.R;
            ECPoint zPt = EcSrp5.ECScalarMul(vh, sum);
            var (zMont, _) = EcSrp5.ToMontgomery(zPt);

            byte[] clientCc = EcSrp5.Sha256(j.Concat(zMont).ToArray());
            return new EcSrp5Proof(
                clientConfirmation: clientCc,
                expectedServerConfirmation: EcSrp5.Sha256(j.Concat(clientCc).Concat(zMont).ToArray()),
                secret: EcSrp5.Sha256(zMont));
        }

        private static byte[] RandomKey()
        {
            byte[] key = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(key);
            return key;
        }
    }

    /// <summary>What <see cref="EcSrp5Client.Answer"/> computed: what to send, what to expect back, and the secret.</summary>
    internal sealed class EcSrp5Proof
    {
        internal EcSrp5Proof(byte[] clientConfirmation, byte[] expectedServerConfirmation, byte[] secret)
        {
            ClientConfirmation = clientConfirmation;
            ExpectedServerConfirmation = expectedServerConfirmation;
            Secret = secret;
        }

        /// <summary>The client's confirmation — the password proof, never the password.</summary>
        internal byte[] ClientConfirmation { get; }

        /// <summary>The confirmation a router that knows the password sends back.</summary>
        internal byte[] ExpectedServerConfirmation { get; }

        /// <summary>The shared secret the stream keys are derived from (<see cref="WinboxStreamCrypto.DeriveStreamKeys"/>).</summary>
        internal byte[] Secret { get; }

        /// <summary>
        /// Checks the router's confirmation: a refusal in plain words where the digest belongs throws as the router's
        /// own account (<see cref="WinboxHandshakeReply.ThrowIfRouterMessage"/>), a digest that does not match throws
        /// <see cref="UnauthorizedAccessException"/>.
        /// </summary>
        internal void Verify(byte[] serverConfirmation, int confirmationLength)
        {
            WinboxHandshakeReply.ThrowIfRouterMessage(serverConfirmation, confirmationLength);
            if (!serverConfirmation.SequenceEqual(ExpectedServerConfirmation))
                throw new UnauthorizedAccessException("Wrong username or password");
        }
    }
}
