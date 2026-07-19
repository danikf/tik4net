using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.CapsMan;

namespace tik4net.integrationtests
{
    [TestClass]
    public class CapsManDatapathTest : TestBase
    {
        [TestMethod]
        public void ListCapsManDatapathsWillNotFail()
        {
            EnsureCommandAvailable("/caps-man/datapath");
            var list = Connection.LoadAll<CapsManDatapath>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddCapsManDatapathWillNotFail()
        {
            EnsureCommandAvailable("/caps-man/datapath");
            string marker = Guid.NewGuid().ToString();
            var entity = new CapsManDatapath
            {
                Name = "tik4net-test-" + marker.Substring(0, 8),
                Comment = marker,
            };
            Connection.Save(entity);

            try
            {
                var loaded = Connection.LoadById<CapsManDatapath>(entity.Id);
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
