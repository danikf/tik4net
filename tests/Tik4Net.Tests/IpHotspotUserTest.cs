using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using tik4net.Objects;
using tik4net.Objects.Ip.Hotspot;

namespace tik4net.tests
{
    [TestClass]
    public class IpHotspotUserTest : TestBase
    {
        [TestMethod]
        public void ListAllUserProfilesWillNotFail()
        {
            var list = Connection.LoadAll<HotspotUserProfile>().ToList();
        }

        [TestMethod]
        public void AddSingleUserWillNotFail()
        {
            var user = new HotspotUser()
            {
                Name = "TEST " + DateTime.Now.ToString(),
                LimitUptime = "1:00:00",
                Password = "secretpass",
            };

            Connection.Save(user);
        }

        [TestMethod]
        public void UpdateFirstUserWillNotFail()
        {
            var user = Connection.LoadAll<HotspotUser>().FirstOrDefault();
            Assert.IsNotNull(user);

            user.Disabled = true;
            Connection.Save(user);
        }

        [TestMethod]
        public void DeleteAllUsersWillNotFail()
        {
            var users = Connection.DeleteAll<HotspotUser>();
        }

        [TestMethod]
        public void AddUserWithProfileWillNotFail()
        {
            string profileName = "TEST " + DateTime.Now.ToString();
            var profile = new HotspotUserProfile()
            {
                Name = profileName,
            };
            Connection.Save(profile);

            var user = new HotspotUser()
            {
                Name = "User for " + profileName,
                Profile = profileName,
                LimitUptime = "1:00:00",
            };
            Connection.Save(user);
        }

        [TestMethod]
        public void DeleteAllUserProfilesWillNotFail()
        {
            var list = Connection.LoadAll<HotspotUserProfile>();
            Connection.SaveListDifferences(list.Where(l => l.Name == "default") /*list with "default" as expected => delete all others*/, list);
        }
    }
}
