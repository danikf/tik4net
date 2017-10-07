using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Configuration;
using System.Text;
using Tik4Net.Objects.Interface;
using Tik4Net.Objects;

namespace Tik4Net.tests
{
    [TestClass]
    public class ApiSslTest
    {
        private ITikConnection _connection;

        protected ITikConnection Connection
        {
            get { return _connection; }
        }

        [TestInitialize]
        public void Init()
        {
            _connection = ConnectionFactory.OpenConnection(TikConnectionType.ApiSsl, ConfigurationManager.AppSettings["host"], ConfigurationManager.AppSettings["user"], ConfigurationManager.AppSettings["pass"]);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _connection.Dispose();
        }

        [TestMethod]
        public void OpenSslConnectionWillNotFail()
        {
            //dummy - just must not fail
        }

        [TestMethod]
        public void ListAllInterfaceWillNotFail()
        {
            var list = Connection.LoadAll<Interface>();
            Assert.IsNotNull(list);
        }
    }
}
