using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.System;

namespace tik4net.integrationtests
{
    /// <summary>
    /// The router identity — the smallest singleton there is (one object, one field, no <c>.id</c>), which
    /// makes it the cheapest place to cover the singleton read AND write path on every transport.
    /// </summary>
    /// <remarks>
    /// Both halves were broken over WinBox native until P2.44 and nothing caught it, because no test had
    /// ever touched <see cref="SystemIdentity"/>: the read decoded the field under its WinBox label
    /// (<c>identity</c>) so <c>LoadSingle</c> threw "Missing field 'name'", and the write went down the
    /// record path, which demands an <c>.id</c> a singleton does not have.
    /// </remarks>
    [TestClass]
    public class SystemIdentityTest : TestBase
    {
        [TestMethod]
        public void LoadSystemIdentityWillNotFail()
        {
            EnsureCommandAvailable("/system/identity");
            var identity = Connection.LoadSingle<SystemIdentity>();
            Assert.IsNotNull(identity);
            Assert.IsFalse(string.IsNullOrEmpty(identity.Name), "the router always has an identity");
        }

        [TestMethod]
        public void SaveSystemIdentity_RoundTrip_WillWork()
        {
            EnsureCommandAvailable("/system/identity");
            var original = Connection.LoadSingle<SystemIdentity>();
            string originalName = original.Name;
            string tempName = "tik4net-" + Guid.NewGuid().ToString("N").Substring(0, 8);

            try
            {
                original.Name = tempName;
                Connection.Save(original);

                var reloaded = Connection.LoadSingle<SystemIdentity>();
                Assert.AreEqual(tempName, reloaded.Name, "the identity write did not reach the router");
            }
            finally
            {
                // The identity is global router state and shows up in every CLI prompt — restore it even
                // when the assertion above fails, or every later run starts against a renamed router.
                var restore = Connection.LoadSingle<SystemIdentity>();
                restore.Name = originalName;
                Connection.Save(restore);
            }

            Assert.AreEqual(originalName, Connection.LoadSingle<SystemIdentity>().Name);
        }
    }
}
