// EntityTransportCheckSuggestionTest.cs — TikEntityTransportCheck measures its CLI suggestion on a live router: an
// entity missing IncludeCliStats or IsSensitive is named by the option whose re-read brings the field back, not by
// both. The built-in entities declare both options, so EntityTransportCheckTest never reaches this path.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using tik4net.Objects;
using tik4net.Testing;

namespace tik4net.integrationtests
{
    [TestClass]
    [TestLock("/ppp/secret")]
    [TestCategory(TestCategories.LegIndependent)]
    public class EntityTransportCheckSuggestionTest : LockedTestBase
    {
        // /interface without IncludeCliStats: a terminal print leaves its counters out.
        [TikEntity("/interface")]
        public class InterfaceWithoutStats
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string Id { get; private set; }

            [TikProperty("name")]
            public TikField<string> Name { get; set; }

            [TikProperty("rx-byte", IsReadOnly = true)]
            public TikField<long?> RxByte { get; set; }
        }

        // /ppp/secret without IsSensitive: RouterOS 7 prints a password over a terminal only with show-sensitive.
        [TikEntity("/ppp/secret")]
        public class PppSecretWithoutSensitive
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string Id { get; private set; }

            [TikProperty("name")]
            public TikField<string> Name { get; set; }

            [TikProperty("password")]
            public TikField<string> Password { get; set; }
        }

        [DataTestMethod]
        [DataRow(TikConnectionType.Telnet)]
        [DataRow(TikConnectionType.Ssh)]
        [DataRow(TikConnectionType.WinboxCli)]
        public void AMissingIncludeCliStats_IsMeasured(TikConnectionType transport)
        {
            using (var api = TestBase.LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api))
            using (var cli = TestBase.LabSetup(transport).Create(transport))
            {
                var report = TikEntityTransportCheck.Compare<InterfaceWithoutStats>(api, cli);
                Console.WriteLine(report);

                var finding = report.Findings.Single(f => f.FieldName == "rx-byte");
                StringAssert.Contains(finding.Suggestion, "measured");
                StringAssert.Contains(finding.Suggestion, "IncludeCliStats");
                Assert.IsFalse(finding.Suggestion.Contains("IsSensitive"), finding.Suggestion);
            }
        }

        [DataTestMethod]
        [DataRow(TikConnectionType.Telnet)]
        [DataRow(TikConnectionType.Ssh)]
        [DataRow(TikConnectionType.WinboxCli)]
        public void AMissingIsSensitive_IsMeasured(TikConnectionType transport)
        {
            using (var api = TestBase.LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api))
            using (var cli = TestBase.LabSetup(transport).Create(transport))
            {
                string name = "t4n" + Guid.NewGuid().ToString("N").Substring(0, 12);
                var id = api.CreateCommandAndParameters("/ppp/secret/add", "name", name, "password", "t4n-check")
                    .ExecuteScalar();
                try
                {
                    var report = TikEntityTransportCheck.Compare<PppSecretWithoutSensitive>(api, cli);
                    Console.WriteLine(report);

                    var finding = report.Findings.Single(f => f.FieldName == "password");
                    StringAssert.Contains(finding.Suggestion, "measured");
                    StringAssert.Contains(finding.Suggestion, "IsSensitive");
                    Assert.IsFalse(finding.Suggestion.Contains("IncludeCliStats"), finding.Suggestion);
                }
                finally
                {
                    api.CreateCommandAndParameters("/ppp/secret/remove", TikSpecialProperties.Id, id).ExecuteNonQuery();
                }
            }
        }
    }
}
