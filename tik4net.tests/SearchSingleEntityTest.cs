using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using tik4net.Objects;
using tik4net.Objects.Interface;

namespace tik4net.tests
{
    [TestClass]
    public class SearchSingleEntityTest: TestBase
    {
        [TestMethod]
        public void SearchByName_Interface_WillWork()
        {
            var eth = Connection.LoadByName<Objects.Interface.Interface>("ether1");

            Assert.IsNotNull(eth);

            /*
                > /interface/print
                > =detail=
                > ?name=ether1
                < !re
                < =.id=*1
                < =name=ether1
                < =default-name=ether1
                ...                 
                < !done              
            */
        }

        [TestMethod]
        public void SearchByDefaultNameParameter_Interface_WillWork()
        {
            var eth = Connection.LoadSingle<Interface>(Connection.CreateParameter("default-name", "ether1"));
            Assert.IsNotNull(eth);

            /*
                > /interface/print
                > =detail=
                > ?name=ether1
                < !re
                < =.id=*1
                < =name=ether1
                < =default-name=ether1
                ...                 
                < !done              
            */
        }

        [TestMethod]
        [ExpectedException(typeof(TikNoSuchItemException))]
        public void SearchById_InPlaceOfName_Interface_WillThrow_NoSOuchIttemException()
        {
            var eth = Connection.LoadById<Objects.Interface.Interface>("ether1");

            Assert.IsNotNull(eth);

            /*
                > /interface/print
                > =detail=
                > ?.id=ether1

                < !done
            */
        }

        [TestMethod]
        public void SearchByNameInLoadAll_Interface_Will()
        {
            var ether1 = Connection.LoadAll<Objects.Interface.Interface>()
                .Single(iface => iface.DefaultName.StartsWith("eth") && iface.DefaultName.EndsWith("1"));

            Assert.IsNotNull(ether1);
        }

    }
}
