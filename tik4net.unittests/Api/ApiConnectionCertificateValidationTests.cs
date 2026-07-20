using System.Net.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    // Certificate validation precedence for API-SSL (P1.1 in ARCHITECTUREIMPROVEMENTPLAN.md):
    // CertificateValidationCallback (if set) wins outright; otherwise AllowInvalidCertificate
    // decides between accept-all and standard SslPolicyErrors-based validation.
    [TestClass]
    public class ApiConnectionCertificateValidationTests
    {
        [TestMethod]
        public void AllowInvalidCertificate_DefaultTrue_AcceptsEvenWithPolicyErrors()
        {
            var connection = new ApiConnection(isSsl: true);

            bool result = connection.ValidateServerCertificate(
                null, null, null, SslPolicyErrors.RemoteCertificateChainErrors);

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void AllowInvalidCertificate_False_RejectsOnPolicyErrors()
        {
            var connection = new ApiConnection(isSsl: true) { AllowInvalidCertificate = false };

            bool result = connection.ValidateServerCertificate(
                null, null, null, SslPolicyErrors.RemoteCertificateChainErrors);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void AllowInvalidCertificate_False_AcceptsWhenNoPolicyErrors()
        {
            var connection = new ApiConnection(isSsl: true) { AllowInvalidCertificate = false };

            bool result = connection.ValidateServerCertificate(
                null, null, null, SslPolicyErrors.None);

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void CertificateValidationCallback_TakesPrecedenceOverAllowInvalidCertificate()
        {
            var connection = new ApiConnection(isSsl: true)
            {
                AllowInvalidCertificate = true,
                CertificateValidationCallback = (sender, certificate, chain, errors) => false,
            };

            bool result = connection.ValidateServerCertificate(
                null, null, null, SslPolicyErrors.None);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void CertificateValidationCallback_ReceivesTheActualPolicyErrors()
        {
            SslPolicyErrors? observedErrors = null;
            var connection = new ApiConnection(isSsl: true)
            {
                CertificateValidationCallback = (sender, certificate, chain, errors) =>
                {
                    observedErrors = errors;
                    return true;
                },
            };

            connection.ValidateServerCertificate(
                null, null, null, SslPolicyErrors.RemoteCertificateNameMismatch);

            Assert.AreEqual(SslPolicyErrors.RemoteCertificateNameMismatch, observedErrors);
        }
    }
}
