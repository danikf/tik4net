// CliAsValueRowSplitTests.cs — where one as-value row ends and the next begins.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    [TestClass]
    public class CliAsValueRowSplitTests
    {
        [TestMethod]
        public void RowsWithoutAnId_SplitWhereAFieldComesRoundAgain()
        {
            // ':put [/console inspect request=child path=ip,route,add as-value]' on 7.24.4, shortened.
            var rows = CliOutputParser.ParseAsValue(
                "name=add;node-type=cmd;type=self;name=blackhole;node-type=arg;type=child;name=routing-table;node-type=arg;type=child");

            Assert.AreEqual(3, rows.Count);
            Assert.AreEqual("self", rows[0].GetResponseField("type"));
            Assert.AreEqual("routing-table", rows[2].GetResponseField("name"));
        }

        [TestMethod]
        public void RowsWithAnId_SplitAtTheId_AsBefore()
        {
            // A field absent from the first row but present in the second must not end the second one early.
            var rows = CliOutputParser.ParseAsValue(".id=*1;name=a;.id=*2;name=b;comment=x");

            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("x", rows[1].GetResponseField("comment"));
        }
    }
}
