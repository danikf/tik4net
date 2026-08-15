using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Ipsec;

namespace tik4net.integrationtests
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

            // Remember what was there BEFORE. The key is created by an action verb, so it cannot be
            // registered with SaveTracked — and when the action's arguments do not reach the router (the
            // WinBox-native transport currently drops them, see below) the new row has neither the name the
            // test looks for nor anything else to find it by, and every run leaves one more orphan behind.
            // Diffing the .id set finds it whatever it ended up called.
            var before = new HashSet<string>();
            foreach (var k in Connection.LoadAll<IpsecKey>()) before.Add(k.Id);

            // Generate a 2048-bit key via the extension method.
            Connection.GenerateIpsecKey(keyName, "2048");

            var list = Connection.LoadAll<IpsecKey>();
            Assert.IsNotNull(list);

            IpsecKey created = null, appeared = null;
            foreach (var k in list)
            {
                if (k.Name == keyName) created = k;
                if (!before.Contains(k.Id)) appeared = k;
            }

            try
            {
                Assert.IsNotNull(created, "Generated key was not found in /ip/ipsec/key/rsa table.");
                Assert.AreEqual("2048", created.KeySize);
                Assert.IsTrue(created.PrivateKey, "Locally generated key should have private-key=true.");
            }
            finally
            {
                // Delete whatever appeared, named or not, so a failed assert cannot leave a key on the router.
                var toDelete = created ?? appeared;
                if (toDelete != null)
                    try { Connection.Delete(toDelete); } catch { /* teardown must not mask the outcome */ }
            }
        }
    }
}
