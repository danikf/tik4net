using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace tik4net.integrationtests
{
    /// <summary>
    /// <c>/user</c> <c>password</c> over WinBox native reaches the router as the API's does, though the window wants the
    /// value twice (WinBox review F23). Proven by logging in with it, over the binary API. A user of the test's own.
    /// </summary>
    [TestClass]
    [TestCategory(TestCategories.AnyRouter)]
    public class WinboxUserPasswordTest : TestBase
    {
        private static void Login(string user, string password)
        {
            using (var connection = new TikConnectionSetup(LabAddress(TikConnectionType.Api), user, password)
                { AllowInvalidCertificate = true }.Create(TikConnectionType.Api))
                Assert.IsNotNull(connection.CreateCommand("/system/identity/print").ExecuteList());
        }

        [TestMethod]
        public void APasswordWrittenByAddAndSet_LogsIn()
        {
            var type = ResolveConnectionType();
            if (type != TikConnectionType.WinboxNative && type != TikConnectionType.WinboxNativeMac)
                Assert.Inconclusive($"{type}: the confirmation field belongs to the WinBox native window.");

            string user = "t4n" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string first = user + "A", second = user + "B";
            string id = Connection.CreateCommandAndParameters("/user/add", "name", user, "group", "full", "password", first).ExecuteScalar();
            try
            {
                Login(user, first);
                // A set naming another field as well: both have to land.
                Connection.CreateCommandAndParameters("/user/set", TikSpecialProperties.Id, id, "password", second, "comment", user).ExecuteNonQuery();
                Login(user, second);
                try
                {
                    Login(user, first);
                    Assert.Fail("the old password still logs in");
                }
                catch (TikConnectionLoginException) { }
                using (var api = LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api))
                    Assert.AreEqual(user, api.CreateCommandAndParameters("/user/print", TikSpecialProperties.Id, id).ExecuteList()
                        .Single().Words["comment"]);
            }
            finally
            {
                using (var api = LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api))
                    api.CreateCommandAndParameters("/user/remove", TikSpecialProperties.Id, id).ExecuteNonQuery();
            }
        }
    }
}
