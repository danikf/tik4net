using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.CapsMan;

namespace tik4net.integrationtests
{
    [TestClass]
    public class CapsManConfigurationTest : TestBase
    {
        [TestMethod]
        public void ListCapsManConfigurationsWillNotFail()
        {
            EnsureCommandAvailable("/caps-man/configuration");
            var list = Connection.LoadAll<CapsManConfiguration>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddCapsManConfigurationWillNotFail()
        {
            EnsureCommandAvailable("/caps-man/configuration");
            string marker = Guid.NewGuid().ToString();
            var entity = new CapsManConfiguration
            {
                Name = "tik4net-test-" + marker.Substring(0, 8),
                Comment = marker,
            };
            Connection.Save(entity);

            try
            {
                var loaded = Connection.LoadById<CapsManConfiguration>(entity.Id);
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
