using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Objects;
using tik4net.Objects.Ip;
using tik4net.Objects.Ip.Firewall;

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
        [ExpectedException(typeof(TikNoSuchCommandException))]
        public void InvalidSyntaxCommand_WillThrowCorrectException()
        {
            var cmd = Connection.CreateCommand("/blablabla/blabla");
            cmd.ExecuteNonQuery();
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

        [TestMethod]
        public void ExecuteNonQuery_Update_Interface_Via_Name_In_Id_Will_Not_Fail()
        {
            //const string IP = "192.168.1.1/24";
            const string INTERFACE = "wlan1";

            //update interface name
            var updateCommand = Connection.CreateCommandAndParameters("/interface/wireless/set",
                "ssid", "test_ssid",
                ".id", INTERFACE);
            updateCommand.ExecuteNonQuery();
        }

        [TestMethod]
        public void ExecuteSingleRow_With_Tag_Parameter_Will_Not_HangUp_Or_Fail()
        {
            var command = Connection.CreateCommandAndParameters("/system/health/print", TikSpecialProperties.Tag, "1234");
            command.ExecuteSingleRow();
        }

        [Ignore]
        [TestMethod]
        [ExpectedException(typeof(System.IO.IOException))]
        public void AsyncExecuteClosed_AfterReboot_AndNextCommandThrowsException()
        {
            var torchAsyncCmd = Connection.LoadAsync<Objects.Tool.ToolTorch>(t => {; },
                null,
                Connection.CreateParameter("interface", "ether1"));

            Thread.Sleep(3000);
            Connection.ExecuteNonQuery("/system/reboot");
            Thread.Sleep(3000);

            Assert.IsFalse(torchAsyncCmd.IsRunning);

            Connection.ExecuteScalar("/system/identity/print"); // throws IO exception (rebooted router)
        }

        [Ignore]
        [TestMethod]
        [ExpectedException(typeof(TikCommandException))]
        public void AsyncExecuteWithDurationExecuteThrowsException_AfterReboot()
        {
            var torchCommand = Connection.CreateCommandAndParameters("/tool/torch", "interface", "ether1");

            new Thread(() =>
            {
                Thread.Sleep(1000);
                Connection.ExecuteNonQuery("/system/reboot");
            }).Start();

            try
            {
                var result = torchCommand.ExecuteListWithDuration(20);
                Thread.Sleep(3000);
            }
            catch
            {
                Assert.IsFalse(torchCommand.IsRunning);
                throw;
            }
        }

        [Ignore]
        [TestMethod]
        public void AsyncExecuteWithDurationExecuteReturnsCorrectReason_AfterReboot()
        {
            var torchCommand = Connection.CreateCommandAndParameters("/tool/torch", "interface", "ether1");

            new Thread(() =>
            {
                Thread.Sleep(1000);
                Connection.ExecuteNonQuery("/system/reboot");
            }).Start();

            bool wasAborted;
            string abortReason;
            var result = torchCommand.ExecuteListWithDuration(20, out wasAborted, out abortReason);
            Thread.Sleep(3000);

            Assert.IsFalse(torchCommand.IsRunning);
            Assert.IsTrue(wasAborted);
            Assert.AreEqual(abortReason, "Cancelled"); //TODO - Cancelled is returned because !done sentence is retrieved before connection is closed!
        }

        [TestMethod]
        public void InvalidCommandThrowsExceptionButConnectionRemainsOpened()
        {
            Exception thrownException = null;
            try
            {
                Connection.ExecuteNonQuery("blah blah");
            }
            catch (Exception ex)
            {
                thrownException = ex;
            }

            Assert.IsNotNull(thrownException);
            Assert.IsTrue(Connection.IsOpened);
            var result = Connection.ExecuteScalar("/system/identity/print");
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public void ExecuteAsync_OnDoneCallback_Called()
        {
            bool onDoneCallbackCalled = false;

            var torchCommand = Connection.CreateCommandAndParameters("/tool/torch", "interface", "ether1");
            torchCommand.ExecuteAsync(response => { }, error => { }, () => { onDoneCallbackCalled = true; });
            Thread.Sleep(3000);
            torchCommand.CancelAndJoin();

            Assert.IsTrue(onDoneCallbackCalled);
        }

        [TestMethod]
        public void RunScript_Issue53_WillNotFail()
        {
            const string name = "TEST_NAME_ISSUE53";
            const string scriptLines = ":log info (\"start\") \r\n/ system identity print \r\n/ system identity print\r\n:log info (\"end\") ";
            const int commandRowsCnt = 2; // 2x call of / system identity print
            ITikCommand scriptCreateCmd = Connection.CreateCommandAndParameters("/system/script/add",
                "name", name,
                "source", scriptLines);
            var id = scriptCreateCmd.ExecuteScalar();
            try
            {
                //run via ID
                ITikCommand scriptRunCmd = Connection.CreateCommand("/system/script/run",
                    Connection.CreateParameter(TikSpecialProperties.Id, id, TikCommandParameterFormat.NameValue));
                var responseRows = scriptRunCmd.ExecuteList();
                Assert.IsTrue(responseRows.Count() == commandRowsCnt); //one empty !re row per script line command

                ////run via number
                //ITikCommand scriptRunCmd1 = Connection.CreateCommand("/system/script/run",
                //    Connection.CreateParameter("number", "0", TikCommandParameterFormat.NameValue));
                //var responseRows1 = scriptRunCmd1.ExecuteList();
                //Assert.IsTrue(responseRows1.Count() == commandRowsCnt); //one empty !re row per script line command
            }
            finally
            {
                Connection.CreateCommandAndParameters("/system/script/remove", TikSpecialProperties.Id, id).ExecuteNonQuery();
            }
        }

        [TestMethod]
        public void ExecuteSingleRowOrDefault_ReturnsNull_IfEmptyResultset()
        {
            var testCommand = Connection.CreateCommandAndParameters("/interface/print", "name", "NOT_EXISTING_NAME");
            var result = testCommand.ExecuteSingleRowOrDefault();

            Assert.IsNull(result);
        }

        [TestMethod]
        public void ExecuteSingleRowOrDefault_ReturnsSingleRow_IfExists()
        {
            var testCommand = Connection.CreateCommand("/system/identity/print");
            var result = testCommand.ExecuteSingleRowOrDefault();

            Assert.IsNotNull(result);
        }

        [TestMethod]
        [ExpectedException(typeof(TikCommandAmbiguousResultException))]
        public void ExecuteSingleRowOrDefault_WithMultipleResponses_WillThrowCorrectException()
        {
            var testCommand = Connection.CreateCommand("/ip/firewal/service-port/print");
            testCommand.ExecuteSingleRowOrDefault();
        }

        [TestMethod]
        public void ExecuteScalarWithTarget_WillNotFail()
        {
            var ipAdresses = Connection.LoadAll<IpAddress>();

            var testCommand = Connection.CreateCommandAndParameters("/ip/address/print", TikCommandParameterFormat.Filter, TikSpecialProperties.Id, ipAdresses.First().Id);
            var readId = testCommand.ExecuteScalar(TikSpecialProperties.Id);

            Assert.IsTrue(!string.IsNullOrWhiteSpace(readId));
        }

        [TestMethod]
        public void ExecuteListWithProplistFilter_WillNotFail()
        {
            var testCommand = Connection.CreateCommand("/ip/address/print");
            var result = testCommand.ExecuteList(TikSpecialProperties.Id);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.Count() > 0);
        }

        [TestMethod]
        public void ExecuteScalarOrDefault_WillReturnDefault_WhenNotFound()
        {
            const string defaultValue = "def";

            var testCommand = Connection.CreateCommandAndParameters("/ip/address/print", TikCommandParameterFormat.Filter, TikSpecialProperties.Id, "not-existing-id");
            var result = testCommand.ExecuteScalarOrDefault(defaultValue, TikSpecialProperties.Id);

            Assert.AreEqual(result, defaultValue);
        }

        [TestMethod]
        public void ExecuteScalarOrDefaultWithTarget_WillNotFail()
        {
            var ipAdresses = Connection.LoadAll<IpAddress>();

            var testCommand = Connection.CreateCommandAndParameters("/ip/address/print", TikCommandParameterFormat.Filter, TikSpecialProperties.Id, ipAdresses.First().Id);
            var readId = testCommand.ExecuteScalarOrDefault("not used default", TikSpecialProperties.Id);

            Assert.AreEqual(readId, ipAdresses.First().Id);
        }

        [TestMethod]
        [ExpectedException(typeof(TikNoSuchItemException))]
        public void ExecuteScalarWithUnexistentId_WillThrowCorrectException()
        {
            var testCommand = Connection.CreateCommandAndParameters("/ip/address/print", TikCommandParameterFormat.Filter, TikSpecialProperties.Id, "-NoID-");
            var id = testCommand.ExecuteScalar(TikSpecialProperties.Id);            
        }

        [TestMethod]
        [ExpectedException(typeof(TikNoSuchItemException))]
        public void ExecuteSingleRowWithUnexistentId_WillThrowCorrectException()
        {
            var testCommand = Connection.CreateCommandAndParameters("/ip/address/print", TikCommandParameterFormat.Filter, TikSpecialProperties.Id, "-NoID-");
            var row = testCommand.ExecuteSingleRow();
        }

        [TestMethod]
        [ExpectedException(typeof(TikNoSuchItemException))]
        public void LoadByIdWithUnexistentId_WillThrowCorrectException()
        {
            var result = Connection.LoadSingle<IpAddress>(Connection.CreateParameter(TikSpecialProperties.Id, "-NoID-"));
        }

        [TestMethod]
        [ExpectedException(typeof(TikCommandAmbiguousResultException))]
        public void LoadByIdWithoutFilter_WillThrowCorrectException()
        {
            Connection.LoadSingle<FirewalServicePort>();
        }

        [TestMethod]
        [ExpectedException(typeof(TikNoSuchItemException))]
        public void DeleteNonExistentEntity_WillThrowCorrectException()
        {
            var cmd = Connection.CreateCommandAndParameters("/ip/address/remove", TikCommandParameterFormat.NameValue, TikSpecialProperties.Id, "-NoID-");
            cmd.ExecuteNonQuery();
        }

        [TestMethod]
        [ExpectedException(typeof(TikNoSuchItemException))]
        public void UpdateNonExistentEntity_WillThrowCorrectException()
        {
            var cmd = Connection.CreateCommandAndParameters("/ip/address/set", TikCommandParameterFormat.NameValue, TikSpecialProperties.Id, "-NoID-", "comment", "bla bla bla");
            cmd.ExecuteNonQuery();
        }

        [TestMethod]
        [ExpectedException(typeof(TikAlreadyHaveSuchItemException))]
        public void CreateDuplicitEntity_WillThrowCorrectException()
        {
            var ipAddr = Connection.LoadAll<IpAddress>().First();

            var newAddr = new IpAddress() { Address = ipAddr.Address, Netmask = ipAddr.Netmask, Network = ipAddr.Network, Interface = ipAddr.Interface };
            Connection.Save(newAddr);
        }
    }
}

//http://forum.mikrotik.com/viewtopic.php?t=88694
//http://wiki.microtik.com/viewtopic.php?f=9&p=576978