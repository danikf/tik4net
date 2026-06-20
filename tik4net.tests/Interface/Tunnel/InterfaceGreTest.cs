using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface.Tunnel;

namespace tik4net.tests
{
    [TestClass]
    public class InterfaceGreTest : TestBase
    {
        [TestMethod]
        public void ListGresWillNotFail()
        {
            EnsureCommandAvailable("/interface/gre");
            var list = Connection.LoadAll<InterfaceGre>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddGreWillNotFail()
        {
            EnsureCommandAvailable("/interface/gre");
            string marker = Guid.NewGuid().ToString();
            var gre = new InterfaceGre
            {
                Name = "test-gre",
                RemoteAddress = "192.0.2.1",
                Comment = marker,
            };
            Connection.Save(gre);

            var loaded = Connection.LoadById<InterfaceGre>(gre.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Comment);
            Assert.AreEqual("192.0.2.1", loaded.RemoteAddress);

            Connection.Delete(loaded);
        }
    }
}
