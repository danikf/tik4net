using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
    }
}
