using System;
using System.Configuration;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects.Ip.hotspot;
using tik4net.Objects;
using System.Collections.Generic;

namespace tik4net.tests
{
    [TestClass]
    public class IpHotspotTest
    {
        private ITikConnection _connection;

        [TestInitialize]
        public void Init()
        {
            _connection = ConnectionFactory.OpenConnection(TikConnectionType.Api, ConfigurationManager.AppSettings["host"], ConfigurationManager.AppSettings["user"], ConfigurationManager.AppSettings["pass"]);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _connection.Dispose();
        }


        [TestMethod]
        public void AddSingleUserWillNotFail()
        {
            var user = new HotspotUser()
            {
                Name = "TEST " + DateTime.Now.ToString(),
                LimitUptime = "1:00:00",
            };

            _connection.Save(user);
        }

        [TestMethod]
        public void UpdateFirstUserWillNotFail()
        {
            var user = _connection.LoadAll<HotspotUser>().FirstOrDefault();
            Assert.IsNotNull(user);

            user.Disabled = true;
            _connection.Save(user);
        }

        [TestMethod]
        public void DeleteAllUsersWillNotFail()
        {
            var users = _connection.DeleteAll<HotspotUser>();
        }

        [TestMethod]
        public void AddUserWithProfileWillNotFail()
        {
            string profileName = "TEST " + DateTime.Now.ToString();
            var profile = new HotspotUser.UserProfile()
            {
                Name = profileName,
            };
            _connection.Save(profile);

            var user = new HotspotUser()
            {
                Name = "User for " + profileName,
                Profile = profileName,
                LimitUptime = "1:00:00",
            };
            _connection.Save(user);
        }

        [TestMethod]
        public void DeleteAllUserProfilesWillNotFail()
        {
            var list = _connection.LoadAll<HotspotUser.UserProfile>();
            _connection.SaveListDifferences(list.Where(l=>l.Name == "default") /*list with "default" as expected => delete all others*/, list);
        }
    }
}
