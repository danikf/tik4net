using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using tik4net.Mndp;

namespace tik4net.tests
{
    [TestClass]
    public class MndpTests
    {
        [TestMethod]
        public void MNDP_WillWork()
        {
            var items = MndpHelper.Discover();

            Assert.IsTrue(items.Count() > 0);
        }
    }
}

