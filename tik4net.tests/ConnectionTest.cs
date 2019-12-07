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
        private const TikConnectionType DEFAULT_CONNECTION_TYPE = TikConnectionType.Api;

        private static ITikConnection CreateOpenedConnection(TikConnectionType? connectionTypeOverride = null, string hostOverride = null, string userOverride = null, string passwordOverride = null)
        {
            var result = ConnectionFactory.OpenConnection(connectionTypeOverride ?? DEFAULT_CONNECTION_TYPE, hostOverride ?? ConfigurationManager.AppSettings["host"], userOverride ?? ConfigurationManager.AppSettings["user"], passwordOverride ?? ConfigurationManager.AppSettings["pass"]);

            return result;
        }

        [TestMethod]
        public void AllConnectionModes_WilWorkTheSameWay()
        {
            var host = ConfigurationManager.AppSettings["host"];
            var user = ConfigurationManager.AppSettings["user"];
            var pass = ConfigurationManager.AppSettings["pass"];

            OpenConnectionAndExecuteSimpleCommand(TikConnectionType.Api, host, user, pass);
            OpenConnectionAndExecuteSimpleCommand(TikConnectionType.ApiSsl, host, user, pass);
#pragma warning disable CS0618 // Type or member is obsolete
            OpenConnectionAndExecuteSimpleCommand(TikConnectionType.Api_v2, host, user, pass);
            OpenConnectionAndExecuteSimpleCommand(TikConnectionType.ApiSsl_v2, host, user, pass);
#pragma warning restore CS0618 // Type or member is obsolete
        }

        private static void OpenConnectionAndExecuteSimpleCommand(TikConnectionType connectionType, string host, string user, string pass)
        {
            using(var connection = CreateOpenedConnection(connectionType, host, user, pass))
            {
                ITikCommand readCmd = connection.CreateCommand("/system/identity/print");
                var originalIdentity = readCmd.ExecuteScalar();

                Assert.IsNotNull(originalIdentity);

                connection.Close();
            }
        }


        [TestMethod]
        public void OpenConnectionWillNotFail()
        {
            using (var connection = CreateOpenedConnection())
            {
                connection.Close();
            }
        }

        [TestMethod]
        [ExpectedException(typeof(TikConnectionLoginException))]
        public void OpenConnectionWithInvalidCredential_WillFailWithProperException()
        {
            using (var connection = CreateOpenedConnection(passwordOverride:"--InvalidPassword--"))
            {
                connection.Close();
            }
        }

        [TestMethod]
        public void ConnectionEncodingWorksCorrectly()
        {
            using (var connection = CreateOpenedConnection())
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
        public void ConnectionSendTagWithSyncExecuteScalarCommandEnabled_WorksCorrectly()
        {
            using (var connection = CreateOpenedConnection())
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
            using (var connection = CreateOpenedConnection())
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
            using (var connection = CreateOpenedConnection())
            {
                connection.Close();
            }
        }

        [TestMethod]
        [ExpectedException(typeof(System.IO.IOException))]
        public void OpenConnectionReceiveTimeoutWillThrowExceptionWhenShortTimeout()
        {
            using (var connection = ConnectionFactory.CreateConnection(DEFAULT_CONNECTION_TYPE))
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
            using (var connection = CreateOpenedConnection(hostOverride: "127.0.0.1") )
            {
                connection.Close();
            }
        }

        [TestMethod]
        [ExpectedException(typeof(System.Net.Sockets.SocketException))]
        public void OpenConnectionToUnaccessibleAddressThrowsExceptionAfterTimeout()
        {
            using (var connection = ConnectionFactory.CreateConnection(DEFAULT_CONNECTION_TYPE))
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
                    using (var connection = ConnectionFactory.CreateConnection(DEFAULT_CONNECTION_TYPE))
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
                using (var connection = ConnectionFactory.CreateConnection(DEFAULT_CONNECTION_TYPE))
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
            using (var connection = CreateOpenedConnection())
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

        [TestMethod]
        public void ReSentenceWithRepeatingFields_WillWorkaroundThisIssue()
        {
            // https://forum.mikrotik.com/viewtopic.php?f=9&t=99954&p=691864#p691858
            var rows = new List<string>()
            {
                "=value=1234",
                "=duplicit=123",
                "=duplicit=123",
                "=duplicit=456",
            };
            var reSentence = Activator.CreateInstance(typeof(ITikReSentence).Assembly.GetType("tik4net.Api.ApiReSentence"), rows as IEnumerable<string>) as ITikReSentence;
            Assert.IsNotNull(reSentence);
            Assert.AreEqual(reSentence.GetResponseField("value"), "1234");
            Assert.AreEqual(reSentence.GetResponseField("duplicit"), "123");
            Assert.AreEqual(reSentence.GetResponseField("duplicit2"), "123");
            Assert.AreEqual(reSentence.GetResponseField("duplicit3"), "456");

        }

        //[TestMethod]
        //public void CallCommandSync_Reboot_Will_Not_HangUp()
        //{
        //    // read with tag formated directly in command
        //    using (var connection = OpenConnection())
        //    {
        //        //var result = connection.CallCommandSync("/system/reboot");                
        //        var command = connection.CreateCommand("/system/reboot");
        //        command.ExecuteNonQuery();
        //    }
        //}
    }
}
