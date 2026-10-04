using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Radius;

namespace tik4net.integrationtests
{
    [TestClass]
    [TestCategory(TestCategories.AnyRouter)]
    [TestLock("/radius/incoming")]
    public class RadiusIncomingTest : TestBase
    {
        [TestMethod]
        public void LoadRadiusIncomingWillNotFail()
        {
            EnsureCommandAvailable("/radius/incoming");
            var incoming = Connection.LoadSingle<RadiusIncoming>();
            Assert.IsNotNull(incoming);
            Assert.IsTrue(incoming.Port.IsPresent, "port: " + incoming.Port.RawValue);
            Assert.IsTrue(incoming.Accept.IsPresent, "accept: " + incoming.Accept.RawValue);
        }

        [TestMethod]
        public void ThePortIsWritten()
        {
            // A router-wide setting: restored in the finally. The port only, so accept stays off and nothing listens.
            EnsureCommandAvailable("/radius/incoming");
            var incoming = Connection.LoadSingle<RadiusIncoming>();
            int original = incoming.Port.Value.Value;
            try
            {
                incoming.Port = original == 3800 ? 3801 : 3800;
                Connection.Save(incoming);
                Assert.AreEqual(original == 3800 ? 3801 : 3800, Connection.LoadSingle<RadiusIncoming>().Port.Value);
            }
            finally
            {
                var restore = Connection.LoadSingle<RadiusIncoming>();
                restore.Port = original;
                Connection.Save(restore);
            }
        }
    }
}
