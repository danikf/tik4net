using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using tik4net.Objects;
using tik4net.Objects.Ppp;

namespace tik4net.tests
{
    [TestClass]
    public class PppTest : TestBase
    {
        [TestMethod]
        public void LoadPppActiveWilNotFail()
        {
            var result = Connection.LoadAll<PppActive>();
        }

        [TestMethod]
        public void LoadPppSecretWilNotFail()
        {
            var result = Connection.LoadAll<PppSecret>();
        }

        [TestMethod]
        public void CreateAndDeletePppSecretWilNotFail()
        {
            var before = Connection.LoadAll<PppSecret>();
            var newSecret = new PppSecret()
            {
                Name = "Test",
            };
            Connection.Save(newSecret);
            Connection.Delete(newSecret);
            var after = Connection.LoadAll<PppSecret>();
            Assert.AreEqual(before.Count(), after.Count());
        }

        [TestMethod]
        public void LoadPppProfileWilNotFail()
        {
            var result = Connection.LoadAll<PppProfile>();
        }

        [TestMethod]
        public void CreateAndDeletePppProfileWilNotFail()
        {
            // pre-cleanup: remove leftovers with the same name that would cause Save to fail
            foreach (var leftover in Connection.LoadAll<PppProfile>().Where(p => p.Name == "Test"))
                Connection.Delete(leftover);

            var before = Connection.LoadAll<PppProfile>();
            var newProfile = new PppProfile() { Name = "Test" };
            Connection.Save(newProfile);
            try
            {
                Connection.Delete(newProfile);
            }
            catch
            {
                try { Connection.Delete(newProfile); } catch { }
                throw;
            }
            var after = Connection.LoadAll<PppProfile>();
            Assert.AreEqual(before.Count(), after.Count());
        }

        [TestMethod]
        public void LoadPppAaaWilNotFail()
        {
            var result = Connection.LoadSingle<PppAaa>();
        }
    }
}
