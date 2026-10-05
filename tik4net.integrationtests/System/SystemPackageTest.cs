using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.System;

namespace tik4net.integrationtests
{
    [TestClass]
    [SafeInParallelLegs]
    public class SystemPackageTest : TestBase
    {
        [TestMethod]
        public void ListPackagesWillNotFail()
        {
            EnsureCommandAvailable("/system/package");
            var list = Connection.LoadAll<SystemPackage>();
            Assert.IsNotNull(list);
            // The base package is always present: 'routeros' on RouterOS 7, 'system' on 6 (where 'routeros-<arch>'
            // is the bundle the other packages belong to).
            Assert.IsTrue(list.Any(p => p.Name == "routeros" || p.Name == "system"),
                "neither the 'routeros' (7.x) nor the 'system' (6.x) package was found");
        }
    }
}
