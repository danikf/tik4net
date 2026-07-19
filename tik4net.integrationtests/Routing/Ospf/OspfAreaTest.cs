using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Routing.Ospf;

namespace tik4net.integrationtests
{
    [TestClass]
    public class OspfAreaTest : TestBase
    {
        [TestMethod]
        public void ListOspfAreasWillNotFail()
        {
            EnsureCommandAvailable("/routing/ospf/area");
            var list = Connection.LoadAll<OspfArea>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddOspfAreaWillNotFail()
        {
            EnsureCommandAvailable("/routing/ospf/area");

            // Creating an area requires an existing instance — build a throwaway one first.
            string instName = "t4n" + Guid.NewGuid().ToString("N").Substring(0, 12);
            var instance = new OspfInstance
            {
                Name = instName,
            };
            Connection.Save(instance);

            string areaName = "t4n" + Guid.NewGuid().ToString("N").Substring(0, 12);
            var area = new OspfArea
            {
                Name = areaName,
                Instance = instName,
                AreaId = "10.0.0.1",
            };

            try
            {
                Connection.Save(area);

                var loaded = Connection.LoadById<OspfArea>(area.Id);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(areaName, loaded.Name);
                Assert.AreEqual(instName, loaded.Instance);
                Assert.AreEqual("10.0.0.1", loaded.AreaId);
            }
            finally
            {
                // Always clean up: area first, then instance.
                if (area.Id != null)
                    try { Connection.Delete(area); } catch { /* best effort */ }
                try { Connection.Delete(instance); } catch { /* best effort */ }
            }
        }
    }
}
