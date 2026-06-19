using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.CapsMan;

namespace tik4net.tests
{
    [TestClass]
    public class CapsManInterfaceTest : TestBase
    {
        // /caps-man/interface lists the managed CAP radio interfaces on the CAPsMAN controller.
        // Most entries are dynamic (created automatically when a CAP connects) and the table is
        // typically empty when no CAPs are connected. Manual/master entries can be pre-created but
        // require radio hardware to become active. For this reason only a List test is provided;
        // an Add test is omitted because creating a useful manual interface entry is only meaningful
        // with real CAP hardware, and cannot be reliably created and cleaned up in a unit-test context.

        [TestMethod]
        public void ListCapsManInterfacesWillNotFail()
        {
            EnsureCommandAvailable("/caps-man/interface");
            var list = Connection.LoadAll<CapsManInterface>();
            Assert.IsNotNull(list);
        }
    }
}
