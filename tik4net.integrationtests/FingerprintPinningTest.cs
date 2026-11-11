using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Renci.SshNet;
using tik4net.Ssh;

namespace tik4net.integrationtests
{
    /// <summary>
    /// <see cref="TikConnectionSetup.SshHostKeyFingerprint"/> and <see cref="TikConnectionSetup.CertificateFingerprint"/>
    /// against the lab router, with the router's own fingerprints read at test time (<c>/ip ssh</c>
    /// <c>host-key-fingerprint</c>, <c>/certificate</c> <c>fingerprint</c>) and one digit changed for the wrong pin.
    /// </summary>
    /// <remarks>
    /// The TLS half runs with <see cref="TikConnectionSetup.AllowInvalidCertificate"/> off: the lab's certificate is signed
    /// by its own CA, so the right pin opening at all is the proof that a pin replaces the chain check. Reads only.
    /// </remarks>
    [TestClass]
    [TestCategory(TestCategories.LegIndependent)]
    [TestCategory(TestCategories.AnyRouter)]
    [SafeInParallelLegs]
    public class FingerprintPinningTest : LockedTestBase
    {
        [ClassInitialize]
        public static void RegisterSsh(TestContext context) => Tik4NetSsh.Register();

        private static string Host => LabConfig.Get("host");

        private static TikConnectionSetup Setup(Action<TikConnectionSetup> pin)
        {
            var setup = new TikConnectionSetup(Host, LabConfig.Get("user"), LabConfig.Get("pass") ?? "")
            {
                ConnectTimeout = TimeSpan.FromSeconds(10),
            };
            pin(setup);
            return setup;
        }

        private static ITikConnection Admin()
        {
            var admin = ConnectionFactory.CreateConnection(TikConnectionType.Api);
            admin.Open(Host, LabConfig.Get("user"), LabConfig.Get("pass"));
            return admin;
        }

        // A different value of the same form: the last hex digit, or the first base64 character, changed.
        private static string Wrong(string fingerprint, string alphabet)
        {
            int at = fingerprint.Length - 1;
            char c = fingerprint[at];
            return fingerprint.Substring(0, at) + alphabet[(alphabet.IndexOf(char.ToLowerInvariant(c)) + 1) % alphabet.Length];
        }

        [TestMethod]
        public void Ssh_TheRouterHostKeyFingerprintOpens_AndAnotherIsRefusedBeforeTheLogin()
        {
            string fingerprint = RouterSshHostKeyFingerprint();
            string wrong = "SHA256:" + Wrong(fingerprint.Substring("SHA256:".Length), "abcdefghijklmnopqrstuvwxyz");

            using (var conn = Setup(s => s.SshHostKeyFingerprint = fingerprint).Create(TikConnectionType.Ssh))
                Assert.IsFalse(string.IsNullOrEmpty(conn.CreateCommand("/system/identity/print").ExecuteScalar("name")));

            // A wrong user as well: a refusal in the key exchange never gets as far as a login to fail.
            var setup = new TikConnectionSetup(Host, "t4n-no-such-user", "x") { SshHostKeyFingerprint = wrong, ConnectTimeout = TimeSpan.FromSeconds(10) };
            var ex = Assert.ThrowsException<TikConnectionHostKeyMismatchException>(() => setup.Create(TikConnectionType.Ssh).Dispose());
            Assert.AreEqual("SSH", ex.Transport);
            Assert.AreEqual(fingerprint, ex.ActualFingerprint, "the exception names the key the router presented");
        }

        [TestMethod]
        public void ApiSsl_TheCertificateFingerprintOpensWithoutAllowInvalidCertificate_AndAnotherIsRefused()
            => TlsPinning(TikConnectionType.ApiSsl, "api-ssl", null, "API-SSL");

        [TestMethod]
        public void RestSsl_TheCertificateFingerprintOpensWithoutAllowInvalidCertificate_AndAnotherIsRefused()
            => TlsPinning(TikConnectionType.RestSsl, "www-ssl", LabConfig.Get("restSslPort"), "REST-SSL");

        private static void TlsPinning(TikConnectionType type, string service, string port, string transport)
        {
            string fingerprint = RouterCertificateFingerprint(service, type);
            string wrong = Wrong(fingerprint, "0123456789abcdef");
            int? portNumber = string.IsNullOrEmpty(port) ? (int?)null : int.Parse(port);

            using (var conn = Setup(s => { s.CertificateFingerprint = fingerprint; s.Port = portNumber; }).Create(type))
                Assert.IsFalse(string.IsNullOrEmpty(conn.CreateCommand("/system/identity/print").ExecuteScalar("name")));

            // AllowInvalidCertificate does not excuse another certificate once a pin is set.
            var ex = Assert.ThrowsException<TikConnectionHostKeyMismatchException>(() =>
                Setup(s => { s.CertificateFingerprint = wrong; s.AllowInvalidCertificate = true; s.Port = portNumber; }).Create(type).Dispose());
            Assert.AreEqual(transport, ex.Transport);
            Assert.AreEqual(fingerprint, ex.ActualFingerprint, "the fingerprint the router shows is the SHA-256 of the DER certificate it presents");
        }

        // RouterOS 7 shows it on /ip ssh; RouterOS 6 does not, and there it is taken from the key exchange itself.
        private static string RouterSshHostKeyFingerprint()
        {
            using (var admin = Admin())
            {
                var row = admin.CreateCommand("/ip/ssh/print").ExecuteSingleRow();
                if (row.TryGetResponseField("host-key-fingerprint", out string shown) && !string.IsNullOrEmpty(shown))
                    return shown;
            }
            string seen = null;
            using (var probe = new SshClient(Host, LabConfig.Get("user"), LabConfig.Get("pass") ?? ""))
            {
                probe.HostKeyReceived += (s, e) => { seen = "SHA256:" + e.FingerPrintSHA256; e.CanTrust = false; };
                try { probe.Connect(); } catch (Exception) { /* refused on purpose: only the key was wanted */ }
            }
            return seen ?? throw new AssertInconclusiveException("The router presented no SSH host key.");
        }

        private static string RouterCertificateFingerprint(string service, TikConnectionType type)
        {
            using (var admin = Admin())
            {
                if (type == TikConnectionType.RestSsl
                    && admin.CreateCommand("/system/resource/print").ExecuteScalar("version").StartsWith("6.", StringComparison.Ordinal))
                    throw new AssertInconclusiveException("REST needs RouterOS 7.1 or later.");
                string certificate = admin.CreateCommandAndParameters("/ip/service/print", TikCommandParameterFormat.Filter, "name", service)
                    .ExecuteList().Single().GetResponseField("certificate");
                if (string.IsNullOrEmpty(certificate) || certificate == "none")
                    throw new AssertInconclusiveException($"/ip service {service} has no certificate, so {type} cannot open.");
                return admin.CreateCommandAndParameters("/certificate/print", TikCommandParameterFormat.Filter, "name", certificate)
                    .ExecuteList().Single().GetResponseField("fingerprint");
            }
        }
    }
}
