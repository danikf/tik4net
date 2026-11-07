using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface.Wifi;

namespace tik4net.integrationtests
{
    [TestClass]
    public class WifiConfigurationTest : TestBase
    {
        [TestMethod]
        public void ListWifiConfigurationsWillNotFail()
        {
            EnsureCommandAvailable("/interface/wifi/configuration");
            var list = Connection.LoadAll<WifiConfiguration>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddWifiConfigurationWillNotFail()
        {
            EnsureCommandAvailable("/interface/wifi/configuration");
            string marker = Guid.NewGuid().ToString();
            var config = new WifiConfiguration
            {
                Name = "test-" + marker.Substring(0, 8),
                Comment = marker,
            };
            SaveTracked(config);

            try
            {
                var loaded = Connection.LoadById<WifiConfiguration>(config.Id);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(marker, loaded.Comment);
            }
            finally
            {
                Connection.Delete(config);
            }
        }

        [TestMethod]
        public void ChainsInAnotherOrder_SettleInAMerge()
        {
            // The router keeps the chain list as a set and prints it sorted (2,0,1 reads 0,1,2), so a merge of the
            // value in the caller's order sends nothing; a value with other members still sends.
            EnsureCommandAvailable("/interface/wifi/configuration");
            string name = "test-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var entity = new WifiConfiguration { Name = name, Chains = new TikValueList<int>(2, 0, 1) };
            SaveTracked(entity);

            var loaded = Connection.LoadById<WifiConfiguration>(entity.Id);
            Assert.AreEqual(new TikValueList<int>(0, 1, 2), loaded.Chains.Value);

            Assert.AreEqual(0, Updates(name, new TikValueList<int>(2, 0, 1)), "the same chains in another order");
            Assert.AreEqual(1, Updates(name, new TikValueList<int>(1, 0)), "other chains");

            Merge(name, new TikValueList<int>(1, 0)).Save();
            Assert.AreEqual("0,1", Connection.LoadById<WifiConfiguration>(entity.Id).Chains.Value.ToString());
            Assert.AreEqual(0, Updates(name, new TikValueList<int>(1, 0)), "after the write");
        }

        private TikListMerge<WifiConfiguration> Merge(string name, TikValueList<int> chains)
        {
            var original = Connection.LoadAll<WifiConfiguration>().Where(c => c.Name.Value == name).ToList();
            var expected = new[] { new WifiConfiguration { Name = name, Chains = chains } };
            return Connection.CreateMerge(expected, original).WithKey(c => c.Name.Value).Field(c => c.Chains);
        }

        private int Updates(string name, TikValueList<int> chains)
        {
            Merge(name, chains).Simulate(out int inserts, out int updates, out int deletes, out int moves);
            Assert.AreEqual(0, inserts + deletes + moves);
            return updates;
        }
    }
}
