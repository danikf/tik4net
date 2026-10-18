using System.Net.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;
using tik4net.Rest;

namespace tik4net.unittests.Connection
{
    /// <summary>The certificate rule API-SSL and REST-SSL share: the caller's callback, else accept-all, else the platform's.</summary>
    [TestClass]
    public class TikTlsValidationTests
    {
        [TestMethod]
        public void NothingSet_LeavesValidationToThePlatform()
        {
            Assert.IsNull(TikTlsValidation.For(new RestConnection(useSsl: true)));
        }

        [TestMethod]
        public void AllowInvalidCertificate_AcceptsAPolicyError()
        {
            var validate = TikTlsValidation.For(new RestConnection(useSsl: true) { AllowInvalidCertificate = true });

            Assert.IsNotNull(validate);
            Assert.IsTrue(validate(null!, null, null, SslPolicyErrors.RemoteCertificateChainErrors));
        }

        [TestMethod]
        public void TheCallersCallback_WinsOverAllowInvalidCertificate()
        {
            RemoteCertificateValidationCallback callback = (s, c, ch, e) => false;
            var connection = new RestConnection(useSsl: true) { AllowInvalidCertificate = true, CertificateValidationCallback = callback };

            Assert.AreSame(callback, TikTlsValidation.For(connection));
        }
    }
}
