using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Routing.Ospf;

namespace tik4net.tests
{
    [TestClass]
    public class OspfInstanceTest : TestBase
    {
        [TestMethod]
        public void ListOspfInstancesWillNotFail()
        {
            EnsureCommandAvailable("/routing/ospf/instance");
            var list = Connection.LoadAll<OspfInstance>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddOspfInstanceWillNotFail()
        {
            EnsureCommandAvailable("/routing/ospf/instance");
            // Use a short prefix + random suffix to avoid name collisions.
            string marker = "t4n" + Guid.NewGuid().ToString("N").Substring(0, 12);
            var instance = new OspfInstance
            {
                Name = marker,
            };
            Connection.Save(instance);

            var loaded = Connection.LoadById<OspfInstance>(instance.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Name);

            Connection.Delete(loaded);
        }
    }
}
