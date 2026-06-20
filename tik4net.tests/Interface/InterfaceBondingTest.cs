using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface;

namespace tik4net.tests
{
    [TestClass]
    public class InterfaceBondingTest : TestBase
    {
        [TestMethod]
        public void ListBondingsWillNotFail()
        {
            EnsureCommandAvailable("/interface/bonding");
            var list = Connection.LoadAll<InterfaceBonding>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddBondingWillNotFail()
        {
            EnsureCommandAvailable("/interface/bonding");
            string marker = Guid.NewGuid().ToString();
            var bonding = new InterfaceBonding
            {
                Name = "test-bond",
                Slaves = "ether1",
                Comment = marker,
            };
            Connection.Save(bonding);

            var loaded = Connection.LoadById<InterfaceBonding>(bonding.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Comment);
            Assert.AreEqual("test-bond", loaded.Name);

            Connection.Delete(loaded);
        }
    }
}
