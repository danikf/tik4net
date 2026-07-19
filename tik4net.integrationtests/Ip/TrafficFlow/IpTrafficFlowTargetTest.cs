using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.TrafficFlow;

namespace tik4net.integrationtests
{
    [TestClass]
    public class IpTrafficFlowTargetTest : TestBase
    {
        [TestMethod]
        public void ListIpTrafficFlowTargetsWillNotFail()
        {
            EnsureCommandAvailable("/ip/traffic-flow/target");
            var list = Connection.LoadAll<IpTrafficFlowTarget>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddIpTrafficFlowTargetWillNotFail()
        {
            EnsureCommandAvailable("/ip/traffic-flow/target");
            var target = new IpTrafficFlowTarget
            {
                DstAddress = "192.0.2.1",
                Port = 2055,
                Version = IpTrafficFlowTarget.NetFlowVersion.V5,
            };
            Connection.Save(target);

            var loaded = Connection.LoadById<IpTrafficFlowTarget>(target.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual("192.0.2.1", loaded.DstAddress);
            Assert.AreEqual(IpTrafficFlowTarget.NetFlowVersion.V5, loaded.Version);

            Connection.Delete(loaded);
        }
    }
}
