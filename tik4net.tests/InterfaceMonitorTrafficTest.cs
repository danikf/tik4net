using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using tik4net.Objects;
using tik4net.Objects.Interface;

namespace tik4net.tests
{
    [TestClass]
    public class InterfaceMonitorTrafficTest : TestBase
    {
        [TestMethod]
        public void GetTrafficSnapshotForEther1WillNotFail()
        {
            var tmp = Connection.GetInterfaceMonitorTrafficSnapshot("ether1");
        }

        [TestMethod]
        public void LoadTrafficSnapshotWillNotFail()
        {
            var tmp = Connection.LoadSingle<InterfaceMonitorTraffic>(
                Connection.CreateParameter("interface", "ether1"),
                Connection.CreateParameter("once", ""));
        }

        [TestMethod]
        public void LoadTrafficWithDurationNotFail()
        {
            var tmp = Connection.LoadWithDuration<InterfaceMonitorTraffic>(5,
                Connection.CreateParameter("interface", "ether1"));

            Assert.IsNotNull(tmp);
            Assert.IsTrue(tmp.Count() > 0);
        }


    }
}
