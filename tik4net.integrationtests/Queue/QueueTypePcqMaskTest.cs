// QueueTypePcqMaskTest.cs — an all-ones netmask is /32, not "not set".
//
// A .jg field that declares 4294967295 as its default is normally the router saying "not set" — a logging
// action's Syslog Severity arrives that way on a row the API prints nothing for. A `netmask` is the
// exception: types.netmask.tostr is netmask2len(val), so all-ones is /32 and RouterOS prints it. All five
// netmask fields in the 7.24 catalog declare it as their default, and the three stock pcq queue types
// carry it, so the rule dropped the field from every one of them.
//
// Two halves, and both were wrong: the field was suppressed, and nothing rendered a netmask as a length.

using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;

namespace tik4net.integrationtests
{
    [TestClass]
    public class QueueTypePcqMaskTest : TestBase
    {
        private const string PcqQueue = "pcq-upload-default";

        [TestMethod]
        public void APcqQueueTypeReportsItsAddressMasksAsPrefixLengths()
        {
            var row = Connection.CreateCommandAndParameters("/queue/type/print",
                    TikCommandParameterFormat.Filter, "name", PcqQueue)
                .ExecuteList().Single();

            Assert.AreEqual("32", row.GetResponseFieldOrDefault("pcq-src-address-mask", null),
                "all-ones is the /32 the API prints, not a 'not set' marker");
            Assert.AreEqual("32", row.GetResponseFieldOrDefault("pcq-dst-address-mask", null));
            Assert.AreEqual("128", row.GetResponseFieldOrDefault("pcq-src-address6-mask", null),
                "and the IPv6 half, which is a plain number, is unchanged");
        }
    }
}
