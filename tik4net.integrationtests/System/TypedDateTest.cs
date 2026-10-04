using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using RouterCertificate = tik4net.Objects.Certificate.Certificate;
using tik4net.Objects.System;
using RouterUser = tik4net.Objects.User.User;

namespace tik4net.integrationtests
{
    /// <summary>
    /// Dates are <see cref="DateTime"/> properties on every RouterOS version: 7.x prints <c>2026-11-21</c>, 6.x
    /// <c>nov/21/2026</c>, and the mapper writes <c>nov/21/2026</c>, the one spelling both accept. Runs on every lab
    /// router (CHR2 is 6.49), because the version is the point.
    /// </summary>
    [TestClass]
    [TestCategory(TestCategories.AnyRouter)]
    public class TypedDateTest : TestBase
    {
        [TestMethod]
        public void SchedulerStartDateRoundTrips()
        {
            EnsureCommandAvailable("/system/scheduler");
            var entity = new SystemScheduler
            {
                Name = "t4n-date-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                OnEvent = ":put hi",
                Disabled = true,
                StartDate = new DateTime(2027, 3, 14),
            };
            SaveTracked(entity);

            var loaded = Connection.LoadById<SystemScheduler>(entity.Id);
            Assert.AreEqual(new DateTime(2027, 3, 14), loaded.StartDate.Value, "added; read back as " + loaded.StartDate.RawValue);

            // WinBox native sent the text on a u32 key and the router ignored it with status OK: the set must MOVE it.
            loaded.StartDate = new DateTime(2028, 12, 1);
            Connection.Save(loaded);
            var reloaded = Connection.LoadById<SystemScheduler>(entity.Id);
            Assert.AreEqual(new DateTime(2028, 12, 1), reloaded.StartDate.Value, "set; read back as " + reloaded.StartDate.RawValue);

            Connection.Delete(reloaded);
        }

        [TestMethod]
        [SafeInParallelLegs]
        public void TimestampsReadAsDates()
        {
            var resource = Connection.LoadSingle<SystemResource>();
            Assert.IsTrue(resource.BuildTime.IsPresent, "build-time: " + resource.BuildTime.RawValue);
            Assert.IsTrue(resource.BuildTime.Value.Value.Year >= 2020);

            var clock = Connection.LoadSingle<SystemClock>();
            Assert.IsTrue(clock.Date.IsPresent, "clock date: " + clock.Date.RawValue);
            Assert.AreEqual(TimeSpan.Zero, clock.Date.Value.Value.TimeOfDay);

            // This connection has just logged in, so its user has a last-logged-in.
            var user = Connection.LoadAll<RouterUser>().Single(u => u.Name == LabConfig.Get("user"));
            Assert.IsTrue(user.LastLoggedIn.IsPresent, "last-logged-in: " + user.LastLoggedIn.RawValue);

            var certificates = Connection.LoadAll<RouterCertificate>();
            foreach (var certificate in certificates)
            {
                Assert.IsTrue(certificate.InvalidBefore.IsPresent, certificate.Name + " invalid-before: " + certificate.InvalidBefore.RawValue);
                Assert.IsTrue(certificate.InvalidAfter.Value > certificate.InvalidBefore.Value, certificate.Name.Value);
            }
        }
    }
}
