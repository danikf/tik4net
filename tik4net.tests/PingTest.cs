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
    public class PingTest : TestBase
    {
        [TestMethod]
        public void PingLocalhostWillNotFail()
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
        public void PingLocalhostWithCloseWillNotFail()
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
            Thread.Sleep(2* 1000);

            Assert.IsTrue(!Connection.IsOpened);
            Assert.IsNull(responseException);
            Assert.IsTrue(responseList.Count < MAX_CNT);
            Assert.IsTrue(!responseList.Any(ping => ping.Host != HOST));

            RecreateConnection(); //Cleanup
        }
    }
}
