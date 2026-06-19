using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Ipsec;

namespace tik4net.tests
{
    [TestClass]
    public class IpsecKeyTest : TestBase
    {
        // NOTE: /ip/ipsec/key/rsa has no plain 'add' command — keys are created via
        // generate-key (an action verb).  The table IS persistent (rows survive reboots)
        // but creation goes through an action, so the standard Add test pattern does not
        // apply here.  The List test exercises the LoadAll<> round-trip; a separate
        // GenerateKey test is provided but cleans up after itself.

        [TestMethod]
        public void ListIpsecKeysWillNotFail()
        {
            EnsureCommandAvailable("/ip/ipsec/key/rsa");
            var list = Connection.LoadAll<IpsecKey>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void GenerateAndDeleteIpsecKeyWillNotFail()
        {
            EnsureCommandAvailable("/ip/ipsec/key/rsa");

            string keyName = "t4n" + Guid.NewGuid().ToString("N").Substring(0, 12);

            // Generate a 2048-bit key via the extension method.
            Connection.GenerateIpsecKey(keyName, "2048");

            // Verify it appeared in the table.
            var list = Connection.LoadAll<IpsecKey>();
            Assert.IsNotNull(list);

            IpsecKey created = null;
            foreach (var k in list)
            {
                if (k.Name == keyName)
                {
                    created = k;
                    break;
                }
            }

            Assert.IsNotNull(created, "Generated key was not found in /ip/ipsec/key/rsa table.");
            Assert.AreEqual("2048", created.KeySize);
            Assert.IsTrue(created.PrivateKey, "Locally generated key should have private-key=true.");

            // Clean up.
            Connection.Delete(created);
        }
    }
}
