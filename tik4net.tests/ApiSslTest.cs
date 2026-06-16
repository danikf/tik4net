using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Configuration;
using System.Text;
using tik4net.Objects.Interface;
using tik4net.Objects;

namespace tik4net.tests
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

        // (SSL open is already proven by Init() and by ConnectionTest.OpenSslConnectionWillNotFail;
        //  this class keeps a real SSL workload below.)

        [TestMethod]
        public void ListAllInterfaceWillNotFail()
        {
            var list = Connection.LoadAll<Interface>();
            Assert.IsNotNull(list);
        }
    }
}
