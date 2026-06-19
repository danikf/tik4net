using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.CapsMan;

namespace tik4net.tests
{
    [TestClass]
    public class CapsManChannelTest : TestBase
    {
        [TestMethod]
        public void ListCapsManChannelsWillNotFail()
        {
            EnsureCommandAvailable("/caps-man/channel");
            var list = Connection.LoadAll<CapsManChannel>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddCapsManChannelWillNotFail()
        {
            EnsureCommandAvailable("/caps-man/channel");
            string marker = Guid.NewGuid().ToString();
            var entity = new CapsManChannel
            {
                Name = "tik4net-test-" + marker.Substring(0, 8),
                Comment = marker,
            };
            Connection.Save(entity);

            try
            {
                var loaded = Connection.LoadById<CapsManChannel>(entity.Id);
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
