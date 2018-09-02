using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net.tests
{
    [TestClass]
    public class ConnectionTest
    {
        [TestMethod]
        public void OpenConnectionWillNotFail()
        {
            using (var connection = ConnectionFactory.OpenConnection(TikConnectionType.ApiSsl, ConfigurationManager.AppSettings["host"], ConfigurationManager.AppSettings["user"], ConfigurationManager.AppSettings["pass"]))
            {
                connection.Close();
            }
        }

        [TestMethod]
        public void ConnectionEncodingWorksCorrectly()
        {
            using (var connection = ConnectionFactory.OpenConnection(TikConnectionType.ApiSsl, ConfigurationManager.AppSettings["host"], ConfigurationManager.AppSettings["user"], ConfigurationManager.AppSettings["pass"]))
            {
                connection.Encoding = Encoding.GetEncoding("windows-1250");
                ITikCommand readCmd = connection.CreateCommand("/system/identity/print");
                var originalIdentity = readCmd.ExecuteScalar();

                //modify
                const string testStringWithExoticCharacters = "Příliš žluťoučký kůň úpěl ďábelské ódy.";
                ITikCommand setCmd = connection.CreateCommand("/system/identity/set");
                setCmd.AddParameterAndValues("name", testStringWithExoticCharacters);
                setCmd.ExecuteNonQuery();

                //read modified
                var newIdentity = readCmd.ExecuteScalar();
                Assert.AreEqual(testStringWithExoticCharacters, newIdentity);

                //cleanup
                setCmd.Parameters.Clear();
                setCmd.AddParameterAndValues("name", originalIdentity);
                setCmd.ExecuteNonQuery();
            }
        }

        [TestMethod]
        public void ConnectionSendTagWithSyncCommandEnabled_WorksCorrectly()
        {
            using (var connection = ConnectionFactory.OpenConnection(TikConnectionType.ApiSsl, ConfigurationManager.AppSettings["host"], ConfigurationManager.AppSettings["user"], ConfigurationManager.AppSettings["pass"]))
            {
                connection.SendTagWithSyncCommand = true;
                List<string> sentWords = new List<string>();
                connection.OnWriteRow += (e, args) =>
                    {
                        sentWords.Add(args.Word);
                    };

                ITikCommand readCmd = connection.CreateCommand("/system/identity/print");
                readCmd.ExecuteScalar();

                Assert.IsTrue(sentWords.Any(w => w.StartsWith(TikSpecialProperties.Tag)));
            }
        }

        [TestMethod]
        public void ConnectionSendTagWithSyncCommandDisabled_WorksCorrectly()
        {
            using (var connection = ConnectionFactory.OpenConnection(TikConnectionType.ApiSsl, ConfigurationManager.AppSettings["host"], ConfigurationManager.AppSettings["user"], ConfigurationManager.AppSettings["pass"]))
            {
                connection.SendTagWithSyncCommand = false;
                List<string> sentWords = new List<string>();
                connection.OnWriteRow += (e, args) =>
                {
                    sentWords.Add(args.Word);
                };

                ITikCommand readCmd = connection.CreateCommand("/system/identity/print");
                readCmd.ExecuteScalar();

                Assert.IsFalse(sentWords.Any(w => w.StartsWith(TikSpecialProperties.Tag)));
            }
        }

        [TestMethod]
        public void OpenSslConnectionWillNotFail()
        {
            using (var connection = ConnectionFactory.OpenConnection(TikConnectionType.ApiSsl, ConfigurationManager.AppSettings["host"], ConfigurationManager.AppSettings["user"], ConfigurationManager.AppSettings["pass"]))
            {
                connection.Close();
            }
        }

        [TestMethod]
        [ExpectedException(typeof(System.IO.IOException))]
        public void OpenConnectionReceiveTimeoutWillThrowExceptionWhenShortTimeout()
        {
            using (var connection = ConnectionFactory.CreateConnection(TikConnectionType.ApiSsl))
            {
                connection.ReceiveTimeout = 1; //very short timeout
                connection.Open(ConfigurationManager.AppSettings["host"], ConfigurationManager.AppSettings["user"], ConfigurationManager.AppSettings["pass"]);
                connection.Close();
            }
        }

        [TestMethod]
        [ExpectedException(typeof(System.Net.Sockets.SocketException))]
        public void OpenConnectionToInvalidAddressThrowsException()
        {
            using (var connection = ConnectionFactory.CreateConnection(TikConnectionType.ApiSsl))
            {
                connection.Open("127.0.0.1", ConfigurationManager.AppSettings["user"], ConfigurationManager.AppSettings["pass"]);
                connection.Close();
            }
        }

        [TestMethod]
        [ExpectedException(typeof(System.Net.Sockets.SocketException))]
        public void OpenConnectionToUnaccessibleAddressThrowsExceptionAfterTimeout()
        {
            using (var connection = ConnectionFactory.CreateConnection(TikConnectionType.ApiSsl))
            {
                connection.ReceiveTimeout = 500; //wait for 2x 500ms
                connection.Open("192.168.99.1" /*Not accessible IP*/, ConfigurationManager.AppSettings["user"], ConfigurationManager.AppSettings["pass"]);
                connection.Close();
            }
        }

        [TestMethod]
        public void OpenAsyncWillNotFail()
        {
            Task.Run(async () =>
                {
                    using (var connection = ConnectionFactory.CreateConnection(TikConnectionType.ApiSsl))
                    {
                        connection.ReceiveTimeout = 500; //wait for 2x 500ms
                        await connection.OpenAsync(ConfigurationManager.AppSettings["host"], ConfigurationManager.AppSettings["user"], ConfigurationManager.AppSettings["pass"]);
                    }
                }).GetAwaiter().GetResult();
        }

        [TestMethod]
        [ExpectedException(typeof(System.IO.IOException))]
        public void OpenConnectionAsyncReceiveTimeoutWillThrowExceptionWhenShortTimeout()
        {
            Task.Run(async () =>
            {
                using (var connection = ConnectionFactory.CreateConnection(TikConnectionType.ApiSsl))
                {
                    connection.ReceiveTimeout = 1; //very short timeout + using async version 
                    connection.OpenAsync(ConfigurationManager.AppSettings["host"], ConfigurationManager.AppSettings["user"], ConfigurationManager.AppSettings["pass"]).GetAwaiter().GetResult();
                    connection.Close();
                }
            }).GetAwaiter().GetResult();
        }

        [TestMethod]
        public void CallCommandSync_With_Inlined_Tag_Will_Not_HangUp_Or_Fail()
        {
            // read with tag formated directly in command
            using (var connection = ConnectionFactory.OpenConnection(TikConnectionType.ApiSsl, ConfigurationManager.AppSettings["host"], ConfigurationManager.AppSettings["user"], ConfigurationManager.AppSettings["pass"]))
            {
                string[] commandRows = new string[]
                {
                    "/system/health/print",
                    TikSpecialProperties.Tag+"=1234"
                };
                var result = connection.CallCommandSync(commandRows);

                Assert.IsTrue(result.Count() == 2);
                Assert.IsTrue(result.Count(s => s is ITikReSentence) == 1);
                Assert.IsTrue(result.Count(s => s is ITikDoneSentence) == 1);
            }
        }



        //[TestMethod]
        //public void CallCommandSync_Reboot_Will_Not_HangUp()
        //{
        //    // read with tag formated directly in command
        //    using (var connection = ConnectionFactory.OpenConnection(TikConnectionType.ApiSsl, ConfigurationManager.AppSettings["host"], ConfigurationManager.AppSettings["user"], ConfigurationManager.AppSettings["pass"]))
        //    {
        //        //var result = connection.CallCommandSync("/system/reboot");                
        //        var command = connection.CreateCommand("/system/reboot");
        //        command.ExecuteNonQuery();
        //    }
        //}
    }
}
