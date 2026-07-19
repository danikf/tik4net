using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using tik4net.Objects;
using tik4net.Objects.Interface;

namespace tik4net.integrationtests
{
    [TestClass]
    public class InterfaceMonitorTrafficTest : TestBase
    {
        [TestMethod]
        public void GetTrafficSnapshotForEther1WillNotFail()
        {
            EnsureCapability(TikConnectionCapability.Streaming, "monitor-traffic snapshot");
            var tmp = Connection.GetInterfaceMonitorTrafficSnapshot(TestConstants.Interface);
        }

        [TestMethod]
        public void LoadTrafficSnapshotWillNotFail()
        {
            EnsureCapability(TikConnectionCapability.Streaming, "monitor-traffic once");
            var tmp = Connection.LoadSingle<InterfaceMonitorTraffic>(
                Connection.CreateParameter("interface", TestConstants.Interface),
                Connection.CreateParameter("once", ""));
        }

        [TestMethod]
        public void LoadTrafficWithDurationNotFail()
        {
            EnsureCapability(TikConnectionCapability.Streaming, "monitor-traffic streaming");
            var tmp = Connection.LoadWithDuration<InterfaceMonitorTraffic>(3,
                Connection.CreateParameter("interface", TestConstants.Interface));

            Assert.IsNotNull(tmp);
            Assert.IsTrue(tmp.Count() > 0);
        }


    }
}
