using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace Tik4Net.Tests
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
            RecreateConnection().Wait();
        }

        [TestCleanup]
        public void Cleanup()
        {
            _connection.Dispose();
        }

        protected async Task RecreateConnection()
        {
            _connection = await ConnectionFactory.OpenConnectionAsync(TikConnectionType.Api, ConfigurationManager.AppSettings["host"], ConfigurationManager.AppSettings["user"], ConfigurationManager.AppSettings["pass"]);
        }
    }
}
