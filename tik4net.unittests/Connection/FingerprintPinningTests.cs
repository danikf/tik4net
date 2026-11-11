#nullable enable
using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;
using tik4net.Connection;
using tik4net.Rest;
using tik4net.Ssh;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// <see cref="TikConnectionSetup.CertificateFingerprint"/> and <see cref="TikConnectionSetup.SshHostKeyFingerprint"/>:
    /// the forms they take, the rule they apply, and — for TLS — the whole handshake against a loopback server with a
    /// self-signed certificate. The SSH host-key check runs against the lab (<c>FingerprintPinningTest</c>).
    /// </summary>
    [TestClass]
    public class FingerprintPinningTests
    {
        // ── The forms ─────────────────────────────────────────────────────────

        private const string Hex = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        [DataTestMethod]
        [DataRow(Hex)]
        [DataRow("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855")]
        [DataRow("e3:b0:c4:42:98:fc:1c:14:9a:fb:f4:c8:99:6f:b9:24:27:ae:41:e4:64:9b:93:4c:a4:95:99:1b:78:52:b8:55")]
        [DataRow("  " + Hex + " ")]
        public void ACertificatePinIsHexWithOptionalColons_AnyCase(string pin)
            => Assert.AreEqual(Hex, TikFingerprint.NormalizeCertificate(pin, "CertificateFingerprint"));

        [DataTestMethod]
        [DataRow("")]
        [DataRow("e3b0c442")]                                         // too short
        [DataRow(Hex + "00")]                                         // too long
        [DataRow("g3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]  // not hex
        [DataRow("SHA256:47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU")]  // the SSH form
        public void ACertificatePinNotInThatFormIsRefused_NamingTheOption(string pin)
        {
            var ex = Assert.ThrowsException<ArgumentException>(() => new TikConnectionSetup("192.0.2.1", "u", "") { CertificateFingerprint = pin });
            StringAssert.Contains(ex.Message, "CertificateFingerprint");
        }

        private const string Ssh = "SHA256:47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU";

        [DataTestMethod]
        [DataRow(Ssh)]
        [DataRow("sha256:47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU")]
        [DataRow("47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU")]        // the prefix left out
        [DataRow("SHA256:47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=")] // padded
        public void AnSshPinIsSha256Base64_ThePrefixOptional(string pin)
            => Assert.AreEqual(Ssh, TikFingerprint.NormalizeSshHostKey(pin, "SshHostKeyFingerprint"));

        [DataTestMethod]
        [DataRow("")]
        [DataRow("SHA256:")]
        [DataRow("SHA256:47DEQpj8HBSa")]                               // not 32 bytes
        [DataRow("MD5:97:70:33:82:fd:29:3a:73:39:af:6a:07:ad:f8:80:49")]
        [DataRow(Hex)]                                                 // the certificate form
        public void AnSshPinNotInThatFormIsRefused_NamingTheOption(string pin)
        {
            var ex = Assert.ThrowsException<ArgumentException>(() => new TikConnectionSetup("192.0.2.1", "u", "") { SshHostKeyFingerprint = pin });
            StringAssert.Contains(ex.Message, "SshHostKeyFingerprint");
        }

        [TestMethod]
        public void TheSshFingerprintOfAKeyIsSha256OfItsBlob()
        {
            // SHA-256 of nothing — the same value ssh-keygen would print for an empty blob.
            Assert.AreEqual(Ssh, TikFingerprint.OfSshHostKey(new byte[0]));
        }

        [TestMethod]
        public void TheCertificateFingerprintIsSha256OfTheDer()
        {
            using (var cert = SelfSigned())
            using (var sha = SHA256.Create())
                Assert.AreEqual(BitConverter.ToString(sha.ComputeHash(cert.RawData)).Replace("-", "").ToLowerInvariant(),
                    TikFingerprint.OfCertificate(cert));
        }

        // ── The rule ──────────────────────────────────────────────────────────

        [TestMethod]
        public void APinAcceptsItsCertificateDespiteChainAndNameErrors()
        {
            using (var cert = SelfSigned())
            {
                var validate = TikTlsValidation.For(new RestConnection(useSsl: true) { CertificateFingerprint = TikFingerprint.OfCertificate(cert) });
                Assert.IsTrue(validate!(null!, cert, null,
                    SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch));
            }
        }

        [TestMethod]
        public void APinRefusesAnotherCertificate_EvenWithAllowInvalidCertificate_AndReportsWhatItSaw()
        {
            using (var cert = SelfSigned())
            {
                string? seen = null;
                var validate = TikTlsValidation.For(
                    new RestConnection(useSsl: true) { AllowInvalidCertificate = true, CertificateFingerprint = Hex },
                    presented => seen = presented);
                Assert.IsFalse(validate!(null!, cert, null, SslPolicyErrors.None), "a valid chain does not excuse another certificate");
                Assert.AreEqual(TikFingerprint.OfCertificate(cert), seen);
            }
        }

        [TestMethod]
        public void TheCallersCallbackWinsOverAPin()
        {
            RemoteCertificateValidationCallback callback = (s, c, ch, e) => true;
            Assert.AreSame(callback, TikTlsValidation.For(new ApiConnection(isSsl: true) { CertificateFingerprint = Hex, CertificateValidationCallback = callback }));
        }

        [TestMethod]
        public void ABadPinOnTheConnectionFailsTheOpenBeforeAnythingConnects()
        {
            // 192.0.2.1 is TEST-NET: a connect attempt would time out rather than fail at once.
            var api = new ApiConnection(isSsl: true) { CertificateFingerprint = "nope", ConnectTimeout = TimeSpan.FromSeconds(30) };
            var ex = Assert.ThrowsException<ArgumentException>(() => api.Open("192.0.2.1", 8729, "u", ""));
            StringAssert.Contains(ex.Message, "CertificateFingerprint");

            var ssh = new TikConnectionSetup("192.0.2.1", "u", "") { ConnectTimeout = TimeSpan.FromSeconds(30) }.CreateUnopened(TikConnectionType.Ssh);
            ((ITikSshKeyConnection)ssh).SshHostKeyFingerprint = "nope";
            var sshEx = Assert.ThrowsException<ArgumentException>(() => ssh.Open("192.0.2.1", "u", ""));
            StringAssert.Contains(sshEx.Message, "SshHostKeyFingerprint");
        }

        [ClassInitialize]
        public static void RegisterSsh(TestContext context) => Tik4NetSsh.Register();

        // ── The handshake, against a loopback TLS server ──────────────────────

        [TestMethod]
        public void ApiSsl_TheRightPinPassesTheHandshake_AndAWrongOneIsAMismatch()
        {
            using (var cert = SelfSigned())
            {
                // Right pin, self-signed certificate, AllowInvalidCertificate off: the handshake is accepted, and what fails
                // afterwards is the login the loopback server never answers — not TLS.
                var right = OpenAgainstTls(cert, TikConnectionType.ApiSsl, TikFingerprint.OfCertificate(cert));
                Assert.IsNotInstanceOfType(right, typeof(TikConnectionSSLErrorException), right?.ToString());
                Assert.IsNotInstanceOfType(right, typeof(TikConnectionHostKeyMismatchException), right?.ToString());

                var wrong = OpenAgainstTls(cert, TikConnectionType.ApiSsl, Hex);
                Assert.IsInstanceOfType(wrong, typeof(TikConnectionHostKeyMismatchException), wrong?.ToString());
                var mismatch = (TikConnectionHostKeyMismatchException)wrong!;
                Assert.AreEqual("API-SSL", mismatch.Transport);
                Assert.AreEqual(Hex, mismatch.ExpectedFingerprint);
                Assert.AreEqual(TikFingerprint.OfCertificate(cert), mismatch.ActualFingerprint);

                // No pin: the self-signed certificate fails the platform's validation, as before.
                var none = OpenAgainstTls(cert, TikConnectionType.ApiSsl, null);
                Assert.IsInstanceOfType(none, typeof(TikConnectionSSLErrorException), none?.ToString());
            }
        }

        [TestMethod]
        public void RestSsl_AWrongPinIsAMismatch_AndTheRightOneIsNot()
        {
            using (var cert = SelfSigned())
            {
                var wrong = OpenAgainstTls(cert, TikConnectionType.RestSsl, Hex);
                Assert.IsInstanceOfType(wrong, typeof(TikConnectionHostKeyMismatchException), wrong?.ToString());
                Assert.AreEqual("REST-SSL", ((TikConnectionHostKeyMismatchException)wrong!).Transport);

                var right = OpenAgainstTls(cert, TikConnectionType.RestSsl, TikFingerprint.OfCertificate(cert));
                Assert.IsNotInstanceOfType(right, typeof(TikConnectionHostKeyMismatchException), right?.ToString());
            }
        }

        // Opens the transport against a loopback server that completes the TLS handshake with the certificate and then
        // closes; returns what the open threw.
        private static Exception? OpenAgainstTls(X509Certificate2 cert, TikConnectionType type, string? pin)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(async () =>
            {
                try
                {
                    using (var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false))
                    using (var ssl = new SslStream(client.GetStream(), false))
                    {
                        await ssl.AuthenticateAsServerAsync(cert, false, System.Security.Authentication.SslProtocols.None, false).ConfigureAwait(false);
                        await Task.Delay(200).ConfigureAwait(false);
                    }
                }
                catch (Exception) { /* the client refused the certificate: expected in half the cases */ }
            });
            try
            {
                var setup = new TikConnectionSetup("127.0.0.1", "u", "") { Port = port, ConnectTimeout = TimeSpan.FromSeconds(5),
                    ReceiveTimeout = TimeSpan.FromSeconds(5), CertificateFingerprint = pin };
                try
                {
                    setup.Create(type).Dispose();
                    return null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            }
            finally
            {
                listener.Stop();
                server.Wait(TimeSpan.FromSeconds(5));
            }
        }

        // A self-signed server certificate for CN=localhost, with its key importable for SslStream on every platform.
        private static X509Certificate2 SelfSigned()
        {
            using (var rsa = RSA.Create(2048))
            {
                var request = new CertificateRequest("CN=tik4net-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
                using (var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)))
#pragma warning disable SYSLIB0057 // the PFX round trip is what gives SslStream on Windows a usable key, on both frameworks
                    return new X509Certificate2(cert.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.Exportable);
#pragma warning restore SYSLIB0057
            }
        }
    }
}
