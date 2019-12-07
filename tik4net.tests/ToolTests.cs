using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Objects;
using tik4net.Objects.Tool;

namespace tik4net.tests
{
    [TestClass]
    public class ToolTests : TestBase
    {
        #region ping
        [TestMethod]
        public void PingLocalhostWillNotFail()
        {
            const string HOST = "127.0.0.1";
            var result = ToolPing.Execute(Connection, HOST, 4);
            Assert.IsTrue(result.Count() == 4);
        }

        [TestMethod]
        public void PingLocalhostAsyncWillNotFail()
        {
            const string HOST = "127.0.0.1";

            List<ToolPing> responseList = new List<ToolPing>();
            Exception responseException = null;

            ITikCommand pingCommand = Connection.LoadAsync<ToolPing>(
                ping => responseList.Add(ping), //read callback
                exception => responseException = exception, //exception callback
                Connection.CreateParameter("address", HOST),
                Connection.CreateParameter("count", "1"),
                Connection.CreateParameter("size", "64"));

            Thread.Sleep(2 * 1000);

            Assert.IsNull(responseException);
            Assert.AreEqual(responseList.Count, 1);
            Assert.AreEqual(responseList.Single().Host, HOST);
        }

        [TestMethod]
        public void PingLocalhostAsyncWithCloseWillNotFail()
        {
            const string HOST = "127.0.0.1";
            const int MAX_CNT = 100;

            List<ToolPing> responseList = new List<ToolPing>();
            Exception responseException = null;

            ITikCommand pingCommand = Connection.LoadAsync<ToolPing>(
                ping => responseList.Add(ping), //read callback
                exception => responseException = exception, //exception callback
                Connection.CreateParameter("address", HOST),
                Connection.CreateParameter("count", MAX_CNT.ToString()),
                Connection.CreateParameter("size", "64"));

            Thread.Sleep(3 * 1000);
            Connection.Close();
            Thread.Sleep(2 * 1000);

            Assert.IsTrue(!Connection.IsOpened);
            Assert.IsNull(responseException);
            Assert.IsTrue(responseList.Count < MAX_CNT);
            Assert.IsTrue(!responseList.Any(ping => ping.Host != HOST));

            RecreateConnection(); //Cleanup
        }
        #endregion


        #region --- WOL ---
        [TestMethod]
        public void WolWillNotFail()
        {
            //const string OK_MAC = "FF:FF:FF:FF:FF:FF"; //
            const string OK_MAC = "00:11:32:71:AD:AD";

            ToolWol.ExecuteWol(Connection, new MacAddress(OK_MAC));
        }

        [TestMethod]
        public void WolWithOkIfaceWillNotFail()
        {
            const string OK_MAC = "FF:FF:FF:FF:FF:FF"; 
            const string OK_IFACE = "ether1";

            ToolWol.ExecuteWol(Connection, new MacAddress(OK_MAC), OK_IFACE);
        }

        [TestMethod]
        [ExpectedException(typeof(TikCommandTrapException), "input does not match any value of interface")]
        public void WolWithInvalidInterfaceWillFail()
        {
            const string OK_MAC = "FF:FF:FF:FF:FF:FF";
            const string BAD_IFACE = "kjdshfkjdhfkjdaskjfhs";

            ToolWol.ExecuteWol(Connection, new MacAddress(OK_MAC), BAD_IFACE);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void WolWithBadMacWillFail()
        {
            const string BAD_MAC = "00:00";
            ToolWol.ExecuteWol(Connection, BAD_MAC);
        }

        #endregion

        #region /tool/traceroute

        [TestMethod]
        public void TracerouteToLocalhostWillNotFail()
        {
            const string IP = "127.0.0.1";

            var result = ToolTraceroute.Execute(Connection, IP);
            Assert.IsTrue(result.Count() == 2);
        }

        [TestMethod]
        public void TracerouteToLocalhostWillNotFail_2()
        {            
            const string IP = "127.0.0.1";            

            var cmd = Connection.CreateCommandAndParameters("/tool/traceroute", TikCommandParameterFormat.NameValue,
                "address", IP,
                "count", "1");
            var result = cmd.ExecuteList();
            Assert.IsTrue(result.Count() == 2);
        }


        [TestMethod]
        public void TracerouteToGoogleDnsWillNotFail()
        {
            const string IP = "8.8.8.8";

            var cmd = Connection.CreateCommandAndParameters("/tool/traceroute", TikCommandParameterFormat.NameValue,
                "address", IP,
                "count", "1");
            var result = cmd.ExecuteList();
            Assert.IsTrue(result.Count() > 1);
        }

        [TestMethod]
        public void TracerouteUnreachableAddressWillNotFail()
        {
            const string IP = "192.168.4.255";

            var cmd = Connection.CreateCommandAndParameters("/tool/traceroute", TikCommandParameterFormat.NameValue,
                "address", IP,
                "count", "1");
            var result = cmd.ExecuteList();
            Assert.IsTrue(result.Count() > 1); //returns exactly 20 rows ...
        }


        #endregion
    }
}
