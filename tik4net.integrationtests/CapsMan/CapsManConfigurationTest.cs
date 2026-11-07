using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.CapsMan;

namespace tik4net.integrationtests
{
    [TestClass]
    public class CapsManConfigurationTest : TestBase
    {
        [TestMethod]
        public void ListCapsManConfigurationsWillNotFail()
        {
            EnsureCommandAvailable("/caps-man/configuration");
            var list = Connection.LoadAll<CapsManConfiguration>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddCapsManConfigurationWillNotFail()
        {
            EnsureCommandAvailable("/caps-man/configuration");
            string marker = Guid.NewGuid().ToString();
            var entity = new CapsManConfiguration
            {
                Name = "tik4net-test-" + marker.Substring(0, 8),
                Comment = marker,
            };
            SaveTracked(entity);

            try
            {
                var loaded = Connection.LoadById<CapsManConfiguration>(entity.Id);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(marker, loaded.Comment);
            }
            finally
            {
                Connection.Delete(entity);
            }
        }

        [TestMethod]
        public void RxChainsInAnotherOrder_SettleInAMerge()
        {
            // The router keeps the chain list as a set and prints it sorted (2,0,1 reads 0,1,2), so a merge of the
            // value in the caller's order sends nothing; a value with other members still sends.
            EnsureCommandAvailable("/caps-man/configuration");
            string name = "tik4net-test-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var entity = new CapsManConfiguration { Name = name, RxChains = new TikValueList<int>(2, 0, 1) };
            SaveTracked(entity);

            var loaded = Connection.LoadById<CapsManConfiguration>(entity.Id);
            Assert.AreEqual(new TikValueList<int>(0, 1, 2), loaded.RxChains.Value);

            Assert.AreEqual(0, Updates(name, new TikValueList<int>(2, 0, 1)), "the same chains in another order");
            Assert.AreEqual(1, Updates(name, new TikValueList<int>(1, 0)), "other chains");

            Merge(name, new TikValueList<int>(1, 0)).Save();
            Assert.AreEqual("0,1", Connection.LoadById<CapsManConfiguration>(entity.Id).RxChains.Value.ToString());
            Assert.AreEqual(0, Updates(name, new TikValueList<int>(1, 0)), "after the write");
        }

        private TikListMerge<CapsManConfiguration> Merge(string name, TikValueList<int> chains)
        {
            var original = Connection.LoadAll<CapsManConfiguration>().Where(c => c.Name.Value == name).ToList();
            var expected = new[] { new CapsManConfiguration { Name = name, RxChains = chains } };
            return Connection.CreateMerge(expected, original).WithKey(c => c.Name.Value).Field(c => c.RxChains);
        }

        private int Updates(string name, TikValueList<int> chains)
        {
            Merge(name, chains).Simulate(out int inserts, out int updates, out int deletes, out int moves);
            Assert.AreEqual(0, inserts + deletes + moves);
            return updates;
        }
    }
}
