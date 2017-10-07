using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using tik4net.Objects;
using tik4net.Objects.Ip;

namespace tik4net.tests
{
    [TestClass]
    public class TikCommandTest : TestBase
    {
        private void DeleteAllItems(string itemsPath)
        {
            foreach (var id in Connection.CallCommandSync($"{itemsPath}/print").OfType<ITikReSentence>().Select(sentence => sentence.GetId()))
            {
                var deleteCommand = Connection.CreateCommandAndParameters($"{itemsPath}/remove", TikSpecialProperties.Id, id);
                deleteCommand.ExecuteNonQuery();
            }
        }


        [TestMethod]
        public void ExecuteNonQuery_Create_New_PPP_Object_Will_Not_Fail()
        {
            const string TEST_NAME = "test-name";
            const string PATH = "/ppp/secret";

            DeleteAllItems(PATH);
            var createCommand = Connection.CreateCommandAndParameters("/ppp/secret/add",
                "name", TEST_NAME);

            createCommand.ExecuteNonQuery();

            //cleanup
            DeleteAllItems("/ppp/secret");
        }

        [TestMethod]
        public void ExecuteNonQuery_Disable_PPP_Object_Will_Not_Fail()
        {
            const string TEST_NAME = "test-name";
            const string PATH = "/ppp/secret";

            DeleteAllItems(PATH);
            var createCommand = Connection.CreateCommandAndParameters("/ppp/secret/add",
                "name", TEST_NAME);
            createCommand.ExecuteNonQuery();

            var updateCommand = Connection.CreateCommandAndParameters("/ppp/secret/set",
                "disabled", "yes",
                TikSpecialProperties.Id, TEST_NAME);
            updateCommand.ExecuteNonQuery();

            //cleanup
            DeleteAllItems("/ppp/secret");
        }

        [TestMethod]
        public void ExecuteNonQuery_Add_And_Remove_IPAddress_Will_Not_Fail()
        {
            const string IP = "192.168.1.1/24";
            const string INTERFACE = "ether1";

            //create IP
            var createCommand = Connection.CreateCommandAndParameters("/ip/address/add",
                "interface", INTERFACE,
                "address", IP);
            createCommand.ExecuteNonQuery();

            //find our IP
            var id = Connection.CallCommandSync("/ip/address/print", $"?=address={IP}").OfType<ITikReSentence>().Single().GetResponseField(TikSpecialProperties.Id);

            //delete by ID
            var deleteCommand = Connection.CreateCommandAndParameters("/ip/address/remove",
                TikSpecialProperties.Id, id);
            deleteCommand.ExecuteNonQuery();
        }
    }
}