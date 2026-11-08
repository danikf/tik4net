using System;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// <c>/ip/dhcp-server/config</c> <c>store-leases-disk</c>: the router (7.25.1, CLI completion on
    /// <c>set store-leases-disk=</c>) offers the words <c>immediately</c> and <c>never</c>; any other value is a duration
    /// such as <c>5m</c>, which the CLI transports print in clock form.
    /// </summary>
    [TestClass]
    public class DhcpServerConfigTests
    {
        private static TikEntityPropertyAccessor Accessor()
            => TikEntityMetadataCache.GetMetadata<DhcpServerConfig>().Properties.Single(p => p.FieldName == "store-leases-disk");

        [TestMethod]
        public void TheStoreLeasesDiskConstantsAreTheWordsTheRouterTakes()
        {
#pragma warning disable CS0618 // the obsolete constants still have to say what they always claimed to
            Assert.AreEqual("immediately", DhcpServerConfig.StoreLeasesDiskType.Immediately);
            Assert.AreEqual("never", DhcpServerConfig.StoreLeasesDiskType.Never);
#pragma warning restore CS0618
        }

        [TestMethod]
        public void StoreLeasesDiskIsATikDuration()
        {
            Assert.AreEqual(typeof(TikField<TikDuration?>), typeof(DhcpServerConfig).GetProperty(nameof(DhcpServerConfig.StoreLeasesDisk))!.PropertyType);
        }

        [TestMethod]
        public void TheConstantsPointToTheTikDurationSpelling()
        {
#pragma warning disable CS0618 // the type under test is the obsolete one
            var obsolete = typeof(DhcpServerConfig.StoreLeasesDiskType).GetCustomAttribute<ObsoleteAttribute>();
#pragma warning restore CS0618
            Assert.IsNotNull(obsolete, "StoreLeasesDiskType names words a TikDuration field takes by TikDurationSpecial");
            Assert.IsFalse(obsolete!.IsError, "a warning: the constants still hold the right words");
            StringAssert.Contains(obsolete.Message, "TikDuration.FromSpecial");
        }

        [DataTestMethod]
        [DataRow("immediately", TikDurationSpecial.Immediately)]
        [DataRow("never", TikDurationSpecial.Never)]
        public void TheRoutersWordsReadAsSpecials(string word, TikDurationSpecial expected)
        {
            var config = new DhcpServerConfig();
            Accessor().SetEntityValue(config, word);

            Assert.AreEqual(expected, config.StoreLeasesDisk.Value!.Value.Special);
            Assert.AreEqual(word, Accessor().GetEntityValue(config));
        }

        [TestMethod]
        public void TheApiAndCliSpellingsOfAnIntervalReadAsOneValue()
        {
            var viaApi = new DhcpServerConfig();
            var viaCli = new DhcpServerConfig();
            Accessor().SetEntityValue(viaApi, "5m");
            Accessor().SetEntityValue(viaCli, "00:05:00");

            Assert.AreEqual(TimeSpan.FromMinutes(5), viaApi.StoreLeasesDisk.Value!.Value.Value);
            Assert.AreEqual(viaApi.StoreLeasesDisk.Value, viaCli.StoreLeasesDisk.Value);
            Assert.AreEqual("5m", Accessor().GetEntityValue(viaCli), "written back in the form every transport accepts");
        }

        [TestMethod]
        public void ACallerSpellsTheWordsThroughTikDurationSpecial()
        {
            var config = new DhcpServerConfig
            {
                StoreLeasesDisk = TikDuration.FromSpecial(TikDurationSpecial.Immediately),
            };
            Assert.AreEqual("immediately", Accessor().GetEntityValue(config));
        }
    }
}
