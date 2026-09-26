// CliFlagFieldsTest.cs — every transport reports the same flags as the binary API.
//
// RouterOS before 7.20 leaves every flag field (disabled, dynamic, running, invalid, active, …) out of the CLI's
// 'print as-value'; the CLI transports ask for them by name there (TikSpecialProperties.CliFlags). A flag that
// went missing does not throw: the property keeps its CLR default, so an interface that is up reads
// Running = false. This test compares the flags row by row against the binary API, which always sends them, so
// it holds on any version — run it against a pre-7.20 router to cover the by-name read, and against the lab
// router to show nothing changed where the router prints the flags itself.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using tik4net.Objects;
using tik4net.Objects.Interface;
using tik4net.Objects.Ip;

namespace tik4net.integrationtests.Cli
{
    [TestClass]
    public class CliFlagFieldsTest
    {
        [DataTestMethod]
        [DataRow(TikConnectionType.Telnet)]
        [DataRow(TikConnectionType.Ssh)]
        [DataRow(TikConnectionType.MacTelnet)]
        [DataRow(TikConnectionType.WinboxCli)]
        [DataRow(TikConnectionType.WinboxCliMac)]
        [DataRow(TikConnectionType.Rest)]
        [DataRow(TikConnectionType.WinboxNative)]
        public void FlagsMatchTheBinaryApi(TikConnectionType transport)
        {
            using (var api = TestBase.LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api))
            using (var other = TestBase.LabSetup(transport).Create(transport))
            {
                var apiInterfaces = api.LoadAll<Interface>().ToList();
                Assert.IsTrue(apiInterfaces.Any(i => i.Running),
                    "no interface is running on this router, so a Running that defaulted to false would pass unseen");

                Compare("interface", apiInterfaces, other.LoadAll<Interface>().ToList(), i => i.Id,
                    i => new object[] { i.Running, i.Disabled });
                Compare("ip address", api.LoadAll<IpAddress>().ToList(), other.LoadAll<IpAddress>().ToList(), a => a.Id,
                    a => new object[] { a.Disabled, a.Dynamic, a.Invalid });
                Compare("ip route", api.LoadAll<IpRoute>().ToList(), other.LoadAll<IpRoute>().ToList(), r => r.Id,
                    r => new object[] { r.Active, r.Dynamic, r.Static, r.Disabled, r.Connect });
            }
        }

        private static void Compare<T>(string menu, List<T> expected, List<T> actual, Func<T, string> id,
            Func<T, object[]> flags)
        {
            Assert.AreEqual(expected.Count, actual.Count, menu + ": row count");
            var byId = actual.ToDictionary(id);
            foreach (var row in expected)
            {
                Assert.IsTrue(byId.TryGetValue(id(row), out T other), menu + " " + id(row) + " is missing");
                CollectionAssert.AreEqual(flags(row), flags(other),
                    menu + " " + id(row) + ": flags differ from the binary API's ("
                    + string.Join(",", flags(row)) + " vs " + string.Join(",", flags(other)) + ")");
            }
        }
    }
}
