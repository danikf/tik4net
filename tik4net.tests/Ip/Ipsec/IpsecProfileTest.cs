using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Ipsec;

namespace tik4net.tests
{
    [TestClass]
    public class IpsecProfileTest : TestBase
    {
        [TestMethod]
        public void ListIpsecProfilesWillNotFail()
        {
            EnsureCommandAvailable("/ip/ipsec/profile");
            var list = Connection.LoadAll<IpsecProfile>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddIpsecProfileWillNotFail()
        {
            EnsureCommandAvailable("/ip/ipsec/profile");
            // Use a short alphanumeric marker; RouterOS profile names are plain identifiers.
            string marker = "t4n" + Guid.NewGuid().ToString("N").Substring(0, 12);
            var profile = new IpsecProfile
            {
                Name = marker,
            };
            Connection.Save(profile);

            var loaded = Connection.LoadById<IpsecProfile>(profile.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Name);

            Connection.Delete(loaded);
        }
    }
}
