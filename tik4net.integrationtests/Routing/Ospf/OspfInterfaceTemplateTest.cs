using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Routing.Ospf;

namespace tik4net.integrationtests
{
    [TestClass]
    public class OspfInterfaceTemplateTest : TestBase
    {
        [TestMethod]
        public void ListOspfInterfaceTemplatesWillNotFail()
        {
            EnsureCommandAvailable("/routing/ospf/interface-template");
            var list = Connection.LoadAll<OspfInterfaceTemplate>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddOspfInterfaceTemplateWillNotFail()
        {
            EnsureCommandAvailable("/routing/ospf/interface-template");

            // A template requires an area, which requires an instance — build both throwaway entries.
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
            Connection.Save(area);

            var template = new OspfInterfaceTemplate
            {
                Area = areaName,
                Comment = "t4n-test",
            };

            try
            {
                Connection.Save(template);

                var loaded = Connection.LoadById<OspfInterfaceTemplate>(template.Id);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(areaName, loaded.Area);
                Assert.AreEqual("t4n-test", loaded.Comment);
            }
            finally
            {
                // Always clean up: template first, then area, then instance.
                if (template.Id != null)
                    try { Connection.Delete(template); } catch { /* best effort */ }
                if (area.Id != null)
                    try { Connection.Delete(area); } catch { /* best effort */ }
                try { Connection.Delete(instance); } catch { /* best effort */ }
            }
        }
    }
}
