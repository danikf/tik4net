using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.System;

namespace tik4net.tests
{
    [TestClass]
    public class SystemPackageTest : TestBase
    {
        [TestMethod]
        public void ListPackagesWillNotFail()
        {
            EnsureCommandAvailable("/system/package");
            var list = Connection.LoadAll<SystemPackage>();
            Assert.IsNotNull(list);
            // routeros base package is always present
            Assert.IsTrue(list.Any(p => p.Name == "routeros"), "routeros package not found");
        }
    }
}
