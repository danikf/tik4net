using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace tik4net.integrationtests
{
    /// <summary>
    /// A field RouterOS renamed between versions reads over WinBox native as this router's API spells it (WinBox review
    /// F15). Category <c>AnyRouter</c> because the older routers are the point: 6.49.21 and 7.21.5 use the old spellings.
    /// </summary>
    [TestClass]
    [TestCategory(TestCategories.AnyRouter)]
    public class WinboxRenamedFieldTest : TestBase
    {
        [SafeInParallelLegs]
        [DataTestMethod]
        [DataRow("/ip/service", "address", "available-from")]
        [DataRow("/tool/e-mail", "address", "server")]
        public void ARenamedField_ReadsUnderTheApisSpelling(string path, string oldName, string newName)
        {
            var type = ResolveConnectionType();
            if (type != TikConnectionType.WinboxNative && type != TikConnectionType.WinboxNativeMac)
                Assert.Inconclusive($"{type}: the rename rule belongs to the WinBox native decode.");

            using (var api = LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api))
            {
                var theirs = Names(api, path);
                var own = Names(Connection, path);
                foreach (string name in new[] { oldName, newName })
                    Assert.AreEqual(theirs.Contains(name), own.Contains(name),
                        $"{path} {name}: API has it={theirs.Contains(name)}, native has it={own.Contains(name)}");
            }
        }

        // F21: a filter on the spelling this router's API lacks must not pass validation; a write may still name either.
        [SafeInParallelLegs]
        [DataTestMethod]
        [DataRow("/ip/service", "address", "available-from")]
        [DataRow("/tool/e-mail", "address", "server")]
        public void ARenamedField_IsReadableOnlyUnderTheApisSpelling(string path, string oldName, string newName)
        {
            var type = ResolveConnectionType();
            if (type != TikConnectionType.WinboxNative && type != TikConnectionType.WinboxNativeMac)
                Assert.Inconclusive($"{type}: the rename rule belongs to the WinBox native decode.");

            using (var api = LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api))
            {
                var theirs = Names(api, path);
                var readable = Connection.DescribeMenu(path).ReadableFields;
                foreach (string name in new[] { oldName, newName })
                    Assert.AreEqual(theirs.Contains(name), readable.Contains(name),
                        $"{path} {name}: API prints it={theirs.Contains(name)}, DescribeMenu reads it={readable.Contains(name)}");
            }
        }

        // F16: the API prints connection-tracking-port only with sync-connection-tracking=yes, and the window declares
        // no condition on it. A disabled row of the test's own, so no VRRP instance runs.
        [TestMethod]
        public void VrrpConnectionTrackingPort_FollowsItsSwitch_AsOnTheApi()
        {
            var type = ResolveConnectionType();
            if (type != TikConnectionType.WinboxNative && type != TikConnectionType.WinboxNativeMac)
                Assert.Inconclusive($"{type}: the rule belongs to the WinBox native decode.");
            EnsureMinRouterOsVersion(7, "sync-connection-tracking");

            using (var api = LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api))
            {
                string name = "t4n" + Guid.NewGuid().ToString("N").Substring(0, 8);
                string id = api.CreateCommandAndParameters("/interface/vrrp/add", "name", name, "interface", "ether2",
                    "vrid", "201", "disabled", "yes").ExecuteScalar();
                try
                {
                    foreach (string sync in new[] { "no", "yes" })
                    {
                        api.CreateCommandAndParameters("/interface/vrrp/set", ".id", id, "sync-connection-tracking", sync).ExecuteNonQuery();
                        bool theirs = Row(api, name).ContainsKey("connection-tracking-port");
                        bool own = Row(Connection, name).ContainsKey("connection-tracking-port");
                        Assert.AreEqual(sync == "yes", theirs, "the API's own behaviour, which this test rests on");
                        Assert.AreEqual(theirs, own, $"sync-connection-tracking={sync}");
                    }
                }
                finally
                {
                    api.CreateCommandAndParameters("/interface/vrrp/remove", ".id", id).ExecuteNonQuery();
                }
            }
        }

        private static IReadOnlyDictionary<string, string> Row(ITikConnection connection, string name)
            => connection.CreateCommand("/interface/vrrp/print").ExecuteList()
                .Single(r => r.Words.TryGetValue("name", out var n) && n == name).Words;

        private static HashSet<string> Names(ITikConnection connection, string path)
            => new HashSet<string>(connection.CreateCommand(path + "/print").ExecuteList().SelectMany(r => r.Words.Keys),
                StringComparer.Ordinal);
    }
}
