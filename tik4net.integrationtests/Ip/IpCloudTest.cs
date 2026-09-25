using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip;

namespace tik4net.integrationtests
{
    [TestClass]
    public class IpCloudTest : TestBase
    {
        [TestMethod]
        public void LoadIpCloudWillNotFail()
        {
            EnsureCommandAvailable("/ip/cloud");
            var cloud = Connection.LoadSingle<IpCloud>();
            Assert.IsNotNull(cloud);
        }

        /// <summary>
        /// <c>ddns-enabled</c> is <c>yes|no|auto</c> on RouterOS 7 and a boolean on RouterOS 6 (6.49.13 prints
        /// <c>false</c>) — the same field with another type. As a <see cref="TikValue{T}"/> it reads Present on 7 and
        /// Unparsed on 6 with the router's word, never fails the load, and a loaded cloud saved untouched is unchanged.
        /// </summary>
        [TestMethod]
        public void DdnsEnabledIsReadOnEveryVersion_AndAnUntouchedCloudIsUnchanged()
        {
            EnsureCommandAvailable("/ip/cloud");
            var cloud = Connection.LoadSingle<IpCloud>();

            Assert.AreNotEqual(TikValueState.Absent, cloud.DdnsEnabled.State, "every RouterOS prints ddns-enabled");
            if (cloud.DdnsEnabled.State == TikValueState.Unparsed)
                CollectionAssert.Contains(new[] { "true", "false", "yes", "no" }, cloud.DdnsEnabled.RawValue,
                    "only RouterOS 6's boolean spelling is expected to be outside the enum");

            Assert.IsFalse(Connection.ChangeTracker().HasChanges(cloud, TikEntityMetadataCache.GetMetadata<IpCloud>()));
            Connection.Save(cloud);   // nothing changed: no command reaches the router
        }
    }
}
