using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Objects;
using tik4net.Objects.Interface.Wireless;

namespace tik4net.tests
{

    [TestClass]
    public class InterfaceWirelessTest : TestBase
    {
        [TestMethod]
        public void AsyncLoad_WirelessAccessList_WillNotFail()
        {
            var tmpAccessList = new WirelessAccessList()
            {
                Interface = "all",
            };
            Connection.Save(tmpAccessList);

            try
            {
                var result = new List<WirelessAccessList>();
                bool failed = false;
                var cmd = Connection.LoadAsync<WirelessAccessList>(
                    (item) => { result.Add(item); },
                    (ex) => { failed = true; }
                );
                Thread.Sleep(1000);
                cmd.CancelAndJoin();

                Assert.IsFalse(failed);
                Assert.IsTrue(result.Count > 0);
            }
            finally
            {
                Connection.Delete(tmpAccessList);
            }
        }
    }
}
