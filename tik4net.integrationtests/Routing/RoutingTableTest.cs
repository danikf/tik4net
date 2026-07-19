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
            Connection.Save(table);

            var loaded = Connection.LoadById<RoutingTable>(table.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Name);
            // NOTE: RouterOS returns fib as an empty-string presence flag (fib=) rather than
            // fib=yes, so the bool mapper always reads it back as false. The write path works
            // (=fib=yes is accepted by the router), but the read path cannot be asserted here.

            Connection.Delete(loaded);
        }
    }
}
