using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Radius;

namespace tik4net.integrationtests
{
    [TestClass]
    public class RadiusTest : TestBase
    {
        [TestMethod]
        public void ListRadiusServersWillNotFail()
        {
            EnsureCommandAvailable("/radius");
            var list = Connection.LoadAll<Radius>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddRadiusServerWillNotFail()
        {
            EnsureCommandAvailable("/radius");
            string marker = Guid.NewGuid().ToString();
            var entity = new Radius
            {
                Address = "192.0.2.1",
                Secret = "x",
                Service = "login",
                Comment = marker,
            };
            Connection.Save(entity);

            var loaded = Connection.LoadById<Radius>(entity.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Comment);
            Assert.AreEqual("192.0.2.1", loaded.Address);
            Assert.AreEqual("login", loaded.Service);

            Connection.Delete(loaded);
        }
    }
}
