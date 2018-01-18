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
        public void OpenSslConnectionWillNotFail()
        {
            using (var connection = ConnectionFactory.OpenConnection(TikConnectionType.ApiSsl, ConfigurationManager.AppSettings["host"], ConfigurationManager.AppSettings["user"], ConfigurationManager.AppSettings["pass"]))
            {
                connection.Close();
            }
        }

        [TestMethod]
        [ExpectedException(typeof(AggregateException))]
        public void OpenConnectionReceiveTimeoutWillThrowExceptionWhenShortTimeout()
        {
            using (var connection = ConnectionFactory.CreateConnection(TikConnectionType.ApiSsl))
            {                
                connection.ReceiveTimeout = 1; //very short timeout + using async version to avoid conflict with Wait(timeout) code in sync version.
                connection.OpenAsync(ConfigurationManager.AppSettings["host"], ConfigurationManager.AppSettings["user"], ConfigurationManager.AppSettings["pass"]).Wait();
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
        [ExpectedException(typeof(TikConnectionException))]
        public void OpenConnectionToUnaccessibleAddressThrowsExceptionAfterTimeout()
        {
            using (var connection = ConnectionFactory.CreateConnection(TikConnectionType.ApiSsl))
            {
                connection.ReceiveTimeout = 500; //wait for 2x 500ms
                connection.Open("192.168.99.1" /*Not accessible IP*/, ConfigurationManager.AppSettings["user"], ConfigurationManager.AppSettings["pass"]);
                connection.Close();
            }
        }
    }
}
