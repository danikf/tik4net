using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.CapsMan;

namespace tik4net.integrationtests
{
    [TestClass]
    public class CapsManAccessListTest : TestBase
    {
        [TestMethod]
        public void ListCapsManAccessListsWillNotFail()
        {
            EnsureCommandAvailable("/caps-man/access-list");
            var list = Connection.LoadAll<CapsManAccessList>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddCapsManAccessListWillNotFail()
        {
            EnsureCommandAvailable("/caps-man/access-list");
            string marker = Guid.NewGuid().ToString();
            var entity = new CapsManAccessList
            {
                Comment = marker,
            };
            Connection.Save(entity);
            try
            {
                var loaded = Connection.LoadById<CapsManAccessList>(entity.Id);
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
