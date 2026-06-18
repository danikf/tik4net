using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.System;

namespace tik4net.tests
{
    [TestClass]
    public class FileTest : TestBase
    {
        // 1) List — LoadAll must not throw and must return a (possibly empty) list.
        [TestMethod]
        public void ListFilesWillNotFail()
        {
            EnsureCommandAvailable("/file");
            var list = Connection.LoadAll<File>();
            Assert.IsNotNull(list);
        }
    }
}
