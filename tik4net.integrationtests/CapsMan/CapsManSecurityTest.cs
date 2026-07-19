using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.CapsMan;

namespace tik4net.integrationtests
{
    [TestClass]
    public class CapsManSecurityTest : TestBase
    {
        [TestMethod]
        public void ListCapsManSecurityProfilesWillNotFail()
        {
            EnsureCommandAvailable("/caps-man/security");
            var list = Connection.LoadAll<CapsManSecurity>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddCapsManSecurityWillNotFail()
        {
            EnsureCommandAvailable("/caps-man/security");
            string marker = Guid.NewGuid().ToString();
            var entity = new CapsManSecurity
            {
                Name = "tik4net-test-" + marker.Substring(0, 8),
                Comment = marker,
            };
            Connection.Save(entity);

            try
            {
                var loaded = Connection.LoadById<CapsManSecurity>(entity.Id);
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
