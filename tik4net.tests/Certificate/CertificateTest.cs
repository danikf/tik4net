using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Certificate;

namespace tik4net.tests
{
    [TestClass]
    public class CertificateTest : TestBase
    {
        [TestMethod]
        public void ListCertificatesWillNotFail()
        {
            EnsureCommandAvailable("/certificate");
            var list = Connection.LoadAll<Certificate>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddCertificateWillNotFail()
        {
            EnsureCommandAvailable("/certificate");
            string marker = Guid.NewGuid().ToString("N").Substring(0, 16); // name length limit safety
            var cert = new Certificate
            {
                Name = marker,
                CommonName = marker,
                KeySize = Certificate.KeySizeType.Rsa2048,
                DaysValid = 365,
            };
            Connection.Save(cert);

            var loaded = Connection.LoadById<Certificate>(cert.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Name);

            Connection.Delete(loaded);
        }
    }
}
