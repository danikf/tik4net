using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip;
using tik4net.Objects.Ip.DhcpServer;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// A lease's <c>lease-time</c> is the same kind of field as its server's, and reads the same way. Measured on 7.24.4:
    /// the binary API prints <c>1d2h</c>, a CLI <c>print detail as-value</c> <c>1d02:00:00</c>.
    /// </summary>
    [TestClass]
    public class DhcpLeaseTimeTests
    {
        private static TikEntityPropertyAccessor Accessor<TEntity>(string field)
            => TikEntityMetadataCache.GetMetadata<TEntity>().Properties.Single(p => p.FieldName == field);

        [TestMethod]
        public void ALeaseTimeIsATikDuration_AsOnTheServer()
        {
            Assert.AreEqual(typeof(TikField<TikDuration?>), typeof(DhcpServerLease).GetProperty("LeaseTime")!.PropertyType);
            Assert.AreEqual(typeof(IpDhcpServer).GetProperty("LeaseTime")!.PropertyType,
                typeof(DhcpServerLease).GetProperty("LeaseTime")!.PropertyType);
        }

        [TestMethod]
        public void TheApiAndCliSpellingsOfALeaseTimeReadAsOneValue()
        {
            var accessor = Accessor<DhcpServerLease>("lease-time");
            var viaApi = new DhcpServerLease();
            var viaCli = new DhcpServerLease();

            accessor.SetEntityValue(viaApi, "1d2h");
            accessor.SetEntityValue(viaCli, "1d02:00:00");

            Assert.AreEqual(viaApi.LeaseTime.Value, viaCli.LeaseTime.Value);
            Assert.AreEqual("1d2h", accessor.GetEntityValue(viaCli), "written back in the form every transport accepts");
        }
    }
}
