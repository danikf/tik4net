using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using tik4net.Objects;

namespace tik4net.tests
{
    [TestClass]
    public class CrudTest: TestBase
    {
        #region Helper methods
        private void Cleanup_DeteleAddressByIp(string ip)
        {
            var ipAddress = Connection.LoadSingleOrDefault<Objects.Ip.IpAddress>(Connection.CreateParameter("address", ip));
            if (ipAddress != null)
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
            const string IP = "192.168.1.1/24";
            const string INTERFACE = "ether1";
            Cleanup_DeteleAddressByIp(IP);

            var response = Connection.CallCommandSync("/ip/address/add", $"=address={IP}", $"=interface={INTERFACE}");

            Assert.AreEqual(1, response.Count());
            Assert.IsInstanceOfType(response.Single(), typeof(ITikDoneSentence));
            string id = ((ITikDoneSentence)response.Single()).GetResponseWord();

            Assert.IsNotNull(id);
            Assert.AreNotEqual(string.Empty, id);

            //cleanup
            Cleanup_DeteleAddressByIp(IP);
        }

        [TestMethod]
        public void Create_IpAddress_With_ADO_API()
        {
            const string IP = "192.168.1.1/24";
            const string INTERFACE = "ether1";
            Cleanup_DeteleAddressByIp(IP);

            var createCommand = Connection.CreateCommandAndParameters("/ip/address/add", 
                "address", IP,
                "interface", INTERFACE);
            var id = createCommand.ExecuteScalar();

            Assert.IsNotNull(id);
            Assert.AreNotEqual(string.Empty, id);

            //cleanup
            Cleanup_DeteleAddressByIp(IP);
        }


        [TestMethod]
        public void Create_IpAddress_With_Highlevel_API()
        {
            const string IP = "192.168.1.1/24";
            const string INTERFACE = "ether1";
            Cleanup_DeteleAddressByIp(IP);

            var address = new Objects.Ip.IpAddress()
            {
                Address = IP,
                Interface = INTERFACE
            };

            Connection.Save(address);

            Assert.IsNotNull(address.Id);
            Assert.AreNotEqual(string.Empty, address.Id);

            //cleanup
            Cleanup_DeteleAddressByIp(IP);
        }

        #endregion

        #region Receive

        [TestMethod]
        public void Load_IpAddress_With_LowLevel_API()
        {
            const string IP = "192.168.1.1/24";
            const string INTERFACE = "ether1";
            Cleanup_DeteleAddressByIp(IP);
            Init_CreateAddress(IP, INTERFACE);

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

            //cleanup
            Cleanup_DeteleAddressByIp(IP);
        }

        [TestMethod]
        public void Load_IpAddress_With_ADO_API()
        {
            const string IP = "192.168.1.1/24";
            const string INTERFACE = "ether1";
            Cleanup_DeteleAddressByIp(IP);
            var id = Init_CreateAddress(IP, INTERFACE);

            var loadCmd = Connection.CreateCommandAndParameters("/ip/address/print", "address", IP);
            var response = loadCmd.ExecuteList();

            Assert.IsNotNull(response);
            Assert.AreEqual(1, response.Count());
            Assert.AreEqual(id, response.Single().GetId());
            Assert.AreEqual(IP, response.Single().GetResponseField("address"));
            Assert.AreEqual(INTERFACE, response.Single().GetResponseField("interface"));

            //cleanup
            Cleanup_DeteleAddressByIp(IP);
        }

        [TestMethod]
        public void Load_IpAddress_With_Highlevel_API()
        {
            const string IP = "192.168.1.1/24";
            const string INTERFACE = "ether1";
            Cleanup_DeteleAddressByIp(IP);
            Init_CreateAddress(IP, INTERFACE);

            var ipAddress = Connection.LoadSingle<Objects.Ip.IpAddress>(
                Connection.CreateParameter("address", IP));

            Assert.IsNotNull(ipAddress);
            Assert.AreEqual(IP, ipAddress.Address);
            Assert.AreEqual(INTERFACE, ipAddress.Interface);

            //cleanup
            Cleanup_DeteleAddressByIp(IP);
        }
        #endregion

        #region Update

        [TestMethod]
        public void Update_IpAddress_With_LowLevel_API()
        {
            const string IP = "192.168.1.1/24";
            const string INTERFACE = "ether1";
            Cleanup_DeteleAddressByIp(IP);
            var id = Init_CreateAddress(IP, INTERFACE);

            var response = Connection.CallCommandSync("/ip/address/set", "=comment=test comment", $"=.id={id}");

            Assert.IsNotNull(response);
            Assert.AreEqual(1, response.Count());
            Assert.IsInstanceOfType(response.Single(), typeof(ITikDoneSentence));

            //cleanup
            Cleanup_DeteleAddressByIp(IP);
        }

        [TestMethod]
        public void Update_IpAddress_With_ADO_API()
        {
            const string IP = "192.168.1.1/24";
            const string INTERFACE = "ether1";
            Cleanup_DeteleAddressByIp(IP);
            var id = Init_CreateAddress(IP, INTERFACE);

            var updateCmd = Connection.CreateCommandAndParameters("/ip/address/set", 
                "comment", "test comment", 
                TikSpecialProperties.Id, id);
            updateCmd.ExecuteNonQuery();

            //cleanup
            Cleanup_DeteleAddressByIp(IP);
        }

        [TestMethod]
        public void Update_IpAddress_With_Highlevel_API()
        {
            const string IP = "192.168.1.1/24";
            const string INTERFACE = "ether1";
            Cleanup_DeteleAddressByIp(IP);
            var id = Init_CreateAddress(IP, INTERFACE);

            var address = Connection.LoadById<Objects.Ip.IpAddress>(id);
            address.Comment = "test comment";
            Connection.Save(address);

            //cleanup
            Cleanup_DeteleAddressByIp(IP);
        }
        #endregion

        #region Delete

        [TestMethod]
        public void Delete_IpAddress_With_LowLevel_API()
        {
            const string IP = "192.168.1.1/24";
            const string INTERFACE = "ether1";
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
            const string IP = "192.168.1.1/24";
            const string INTERFACE = "ether1";
            Cleanup_DeteleAddressByIp(IP);
            var id = Init_CreateAddress(IP, INTERFACE);

            var deleteCmd = Connection.CreateCommandAndParameters("/ip/address/remove",
                TikSpecialProperties.Id, id);
            deleteCmd.ExecuteNonQuery();
        }

        [TestMethod]
        public void Delete_IpAddress_With_Highlevel_API()
        {
            const string IP = "192.168.1.1/24";
            const string INTERFACE = "ether1";
            Cleanup_DeteleAddressByIp(IP);
            var id = Init_CreateAddress(IP, INTERFACE);

            var address = Connection.LoadById<Objects.Ip.IpAddress>(id);
            Connection.Delete(address);
        }
        #endregion



    }
}
