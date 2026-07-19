using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using tik4net.Objects;

namespace tik4net.integrationtests
{
    [TestClass]
    public class CrudTest: TestBase
    {
        #region Helper methods
        private void Cleanup_DeteleAddressByIp(string ip)
        {
            // Delete ALL rows matching the address — not LoadSingleOrDefault. A stray duplicate (e.g. an
            // orphan an unstable transport left behind when it failed to resolve the interface and stored a
            // sentinel handle) would otherwise make LoadSingleOrDefault throw TikCommandAmbiguousResultException
            // and permanently wedge every CRUD test until the router is cleaned by hand.
            foreach (var ipAddress in Connection.LoadList<Objects.Ip.IpAddress>(Connection.CreateParameter("address", ip)))
                Connection.Delete(ipAddress);
        }

        private string Init_CreateAddress(string ip, string iface)
        {
            var address = new Objects.Ip.IpAddress()
            {
                Address = ip,
                Interface = iface
            };
            Connection.Save(address);

            return address.Id;
        }
        #endregion

        #region Create
        [TestMethod]
        public void Create_IpAddress_With_LowLevel_API()
        {
            string IP = TestConstants.Address;
            string INTERFACE = TestConstants.Interface;
            Cleanup_DeteleAddressByIp(IP);

            try
            {
                var response = Connection.CallCommandSync("/ip/address/add", $"=address={IP}", $"=interface={INTERFACE}");

                Assert.AreEqual(1, response.Count());
                Assert.IsInstanceOfType(response.Single(), typeof(ITikDoneSentence));
                string id = ((ITikDoneSentence)response.Single()).GetResponseWord();

                Assert.IsNotNull(id);
                Assert.AreNotEqual(string.Empty, id);
            }
            finally
            {
                //cleanup
                Cleanup_DeteleAddressByIp(IP);
            }
        }

        [TestMethod]
        public void Create_IpAddress_With_ADO_API()
        {
            string IP = TestConstants.Address;
            string INTERFACE = TestConstants.Interface;
            Cleanup_DeteleAddressByIp(IP);

            try
            {
                var createCommand = Connection.CreateCommandAndParameters("/ip/address/add",
                    "address", IP,
                    "interface", INTERFACE);
                var id = createCommand.ExecuteScalar();

                Assert.IsNotNull(id);
                Assert.AreNotEqual(string.Empty, id);
            }
            finally
            {
                //cleanup
                Cleanup_DeteleAddressByIp(IP);
            }
        }


        [TestMethod]
        public void Create_IpAddress_With_Highlevel_API()
        {
            string IP = TestConstants.Address;
            string INTERFACE = TestConstants.Interface;
            Cleanup_DeteleAddressByIp(IP);

            try
            {
                var address = new Objects.Ip.IpAddress()
                {
                    Address = IP,
                    Interface = INTERFACE
                };

                Connection.Save(address);

                Assert.IsNotNull(address.Id);
                Assert.AreNotEqual(string.Empty, address.Id);
            }
            finally
            {
                //cleanup
                Cleanup_DeteleAddressByIp(IP);
            }
        }

        #endregion

        #region Receive

        [TestMethod]
        public void Load_IpAddress_With_LowLevel_API()
        {
            string IP = TestConstants.Address;
            string INTERFACE = TestConstants.Interface;
            Cleanup_DeteleAddressByIp(IP);
            Init_CreateAddress(IP, INTERFACE);

            try
            {
                var response = Connection.CallCommandSync("/ip/address/print", $"?=address={IP}");

                /* EXAMPLE:
                //find by IP address -> we need ID of item
                var itemId  = Connection.CallCommandSync("/ip/address/print", $"?=address={IP}")
                    .OfType<ITikReSentence>()
                    .Single()
                    .GetId();

                */

                Assert.IsNotNull(response);
                Assert.AreEqual(2, response.Count());
                Assert.IsInstanceOfType(response.ToArray()[0], typeof(ITikReSentence));
                Assert.IsInstanceOfType(response.ToArray()[1], typeof(ITikDoneSentence));
                Assert.AreEqual(IP, ((ITikReSentence)response.ToArray()[0]).GetResponseField("address"));
                Assert.AreEqual(INTERFACE, ((ITikReSentence)response.ToArray()[0]).GetResponseField("interface"));
            }
            finally
            {
                //cleanup
                Cleanup_DeteleAddressByIp(IP);
            }
        }

        [TestMethod]
        public void Load_IpAddress_With_ADO_API()
        {
            string IP = TestConstants.Address;
            string INTERFACE = TestConstants.Interface;
            Cleanup_DeteleAddressByIp(IP);
            var id = Init_CreateAddress(IP, INTERFACE);

            try
            {
                var loadCmd = Connection.CreateCommandAndParameters("/ip/address/print", "address", IP);
                var response = loadCmd.ExecuteList();

                Assert.IsNotNull(response);
                Assert.AreEqual(1, response.Count());
                Assert.AreEqual(id, response.Single().GetId());
                Assert.AreEqual(IP, response.Single().GetResponseField("address"));
                Assert.AreEqual(INTERFACE, response.Single().GetResponseField("interface"));
            }
            finally
            {
                //cleanup
                Cleanup_DeteleAddressByIp(IP);
            }
        }

        [TestMethod]
        public void Load_IpAddress_With_Highlevel_API()
        {
            string IP = TestConstants.Address;
            string INTERFACE = TestConstants.Interface;
            Cleanup_DeteleAddressByIp(IP);
            Init_CreateAddress(IP, INTERFACE);

            try
            {
                var ipAddress = Connection.LoadSingle<Objects.Ip.IpAddress>(
                    Connection.CreateParameter("address", IP));

                Assert.IsNotNull(ipAddress);
                Assert.AreEqual(IP, ipAddress.Address);
                Assert.AreEqual(INTERFACE, ipAddress.Interface);
            }
            finally
            {
                //cleanup
                Cleanup_DeteleAddressByIp(IP);
            }
        }
        #endregion

        #region Update

        [TestMethod]
        public void Update_IpAddress_With_LowLevel_API()
        {
            string IP = TestConstants.Address;
            string INTERFACE = TestConstants.Interface;
            Cleanup_DeteleAddressByIp(IP);
            var id = Init_CreateAddress(IP, INTERFACE);

            try
            {
                var response = Connection.CallCommandSync("/ip/address/set", "=comment=test comment", $"=.id={id}");

                Assert.IsNotNull(response);
                Assert.AreEqual(1, response.Count());
                Assert.IsInstanceOfType(response.Single(), typeof(ITikDoneSentence));
            }
            finally
            {
                //cleanup
                Cleanup_DeteleAddressByIp(IP);
            }
        }

        [TestMethod]
        public void Update_IpAddress_With_ADO_API()
        {
            string IP = TestConstants.Address;
            string INTERFACE = TestConstants.Interface;
            Cleanup_DeteleAddressByIp(IP);
            var id = Init_CreateAddress(IP, INTERFACE);

            try
            {
                var updateCmd = Connection.CreateCommandAndParameters("/ip/address/set",
                    "comment", "test comment",
                    TikSpecialProperties.Id, id);
                updateCmd.ExecuteNonQuery();
            }
            finally
            {
                //cleanup
                Cleanup_DeteleAddressByIp(IP);
            }
        }

        [TestMethod]
        public void Update_IpAddress_With_Highlevel_API()
        {
            string IP = TestConstants.Address;
            string INTERFACE = TestConstants.Interface;
            Cleanup_DeteleAddressByIp(IP);
            var id = Init_CreateAddress(IP, INTERFACE);

            try
            {
                var address = Connection.LoadById<Objects.Ip.IpAddress>(id);
                address.Comment = "test comment";
                Connection.Save(address);
            }
            finally
            {
                //cleanup
                Cleanup_DeteleAddressByIp(IP);
            }
        }
        #endregion

        #region Delete

        [TestMethod]
        public void Delete_IpAddress_With_LowLevel_API()
        {
            string IP = TestConstants.Address;
            string INTERFACE = TestConstants.Interface;
            Cleanup_DeteleAddressByIp(IP);
            var id = Init_CreateAddress(IP, INTERFACE);

            var response = Connection.CallCommandSync("/ip/address/remove", $"=.id={id}");

            Assert.IsNotNull(response);
            Assert.AreEqual(1, response.Count());
            Assert.IsInstanceOfType(response.Single(), typeof(ITikDoneSentence));
        }

        [TestMethod]
        public void Delete_IpAddress_With_ADO_API()
        {
            string IP = TestConstants.Address;
            string INTERFACE = TestConstants.Interface;
            Cleanup_DeteleAddressByIp(IP);
            var id = Init_CreateAddress(IP, INTERFACE);

            var deleteCmd = Connection.CreateCommandAndParameters("/ip/address/remove",
                TikSpecialProperties.Id, id);
            deleteCmd.ExecuteNonQuery();
        }

        [TestMethod]
        public void Delete_IpAddress_With_Highlevel_API()
        {
            string IP = TestConstants.Address;
            string INTERFACE = TestConstants.Interface;
            Cleanup_DeteleAddressByIp(IP);
            var id = Init_CreateAddress(IP, INTERFACE);

            var address = Connection.LoadById<Objects.Ip.IpAddress>(id);
            Connection.Delete(address);
        }
        #endregion



    }
}
