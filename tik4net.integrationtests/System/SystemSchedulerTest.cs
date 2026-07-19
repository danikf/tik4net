using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.System;

namespace tik4net.integrationtests
{
    [TestClass]
    public class SystemSchedulerTest : TestBase
    {
        // 1) List — LoadAll must not throw and must return a (possibly empty) list.
        [TestMethod]
        public void ListSystemSchedulersWillNotFail()
        {
            EnsureCommandAvailable("/system/scheduler");
            var list = Connection.LoadAll<SystemScheduler>();
            Assert.IsNotNull(list);
        }

        // 2) Add — create, reload by id, assert, then delete (always clean up).
        [TestMethod]
        public void AddSystemSchedulerWillNotFail()
        {
            EnsureCommandAvailable("/system/scheduler");
            string marker = Guid.NewGuid().ToString();
            var entity = new SystemScheduler
            {
                Name = "t4n-test-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                OnEvent = ":put hi",
                Interval = "00:05:00",
                Comment = marker,
            };
            Connection.Save(entity);

            var loaded = Connection.LoadById<SystemScheduler>(entity.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Comment);

            Connection.Delete(loaded);
        }
    }
}
