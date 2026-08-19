using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Routing;

namespace tik4net.integrationtests
{
    [TestClass]
    public class RoutingTableTest : TestBase
    {
        [TestMethod]
        public void ListRoutingTablesWillNotFail()
        {
            EnsureCommandAvailable("/routing/table");
            var list = Connection.LoadAll<RoutingTable>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddRoutingTableWillNotFail()
        {
            EnsureCommandAvailable("/routing/table");
            // Use a short deterministic prefix + random suffix so names don't collide.
            string marker = "t4n" + Guid.NewGuid().ToString("N").Substring(0, 12);
            var table = new RoutingTable
            {
                Name = marker,
                Fib = true,
            };
            SaveTracked(table);

            var loaded = Connection.LoadById<RoutingTable>(table.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Name);
            // fib is a valueless PRESENCE flag: the binary API and REST answer `fib=` (empty) for a
            // table that has it and omit the word for one that does not, while the CLI transports and
            // WinboxNative answer `fib=true`. The property is declared IsPresenceFlag, so all three
            // read the same thing — before that, this read was false on api/rest whatever was written,
            // which is why the assert used to be a comment saying so.
            Assert.AreEqual(true, loaded.Fib,
                "fib was written as yes; the router reports it back, valuelessly over api/rest");

            Connection.Delete(loaded);
        }
    }
}
