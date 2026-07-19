using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.System;

namespace tik4net.integrationtests
{
    [TestClass]
    public class SystemScriptTest : TestBase
    {
        // 1) List — LoadAll must not throw and must return a (possibly empty) list.
        [TestMethod]
        public void ListSystemScriptsWillNotFail()
        {
            EnsureCommandAvailable("/system/script");
            var list = Connection.LoadAll<SystemScript>();
            Assert.IsNotNull(list);
        }

        // 2) Add — create, reload by id, assert name, then delete (always clean up).
        [TestMethod]
        public void AddSystemScriptWillNotFail()
        {
            EnsureCommandAvailable("/system/script");
            string marker = Guid.NewGuid().ToString();
            var entity = new SystemScript
            {
                Name = marker,
                Source = ":log info test",
            };
            Connection.Save(entity);

            var loaded = Connection.LoadById<SystemScript>(entity.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Name);

            Connection.Delete(loaded);
        }
    }
}
