using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.CapsMan;

namespace tik4net.integrationtests
{
    [TestClass]
    public class CapsManProvisioningTest : TestBase
    {
        [TestMethod]
        public void ListCapsManProvisioningsWillNotFail()
        {
            EnsureCommandAvailable("/caps-man/provisioning");
            var list = Connection.LoadAll<CapsManProvisioning>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddCapsManProvisioningWillNotFail()
        {
            EnsureCommandAvailable("/caps-man/provisioning");
            string marker = Guid.NewGuid().ToString();
            var entity = new CapsManProvisioning
            {
                Comment = marker,
            };
            Connection.Save(entity);
            try
            {
                var loaded = Connection.LoadById<CapsManProvisioning>(entity.Id);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(marker, loaded.Comment);
            }
            finally
            {
                Connection.Delete(entity);
            }
        }
    }
}
