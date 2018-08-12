using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tik4net.tests
{
    public class TestBase
    {
        private ITikConnection _connection;

        protected ITikConnection Connection
        {
            get { return _connection; }
        }

        [TestInitialize]
        public void Init()
        {
            RecreateConnection();
        }

        [TestCleanup]
        public void Cleanup()
        {
            _connection.Dispose();
        }

        protected void RecreateConnection()
        {
            _connection = ConnectionFactory.OpenConnection(TikConnectionType.Api, ConfigurationManager.AppSettings["host"], ConfigurationManager.AppSettings["user"], ConfigurationManager.AppSettings["pass"]);
            _connection.DebugEnabled = true;
        }
    }
}
