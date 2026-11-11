using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// The default cache directory is the user's own <c>LocalApplicationData/tik4net</c> on every OS (security review D4):
    /// on most Linux hosts the temporary directory is the shared, world-writable <c>/tmp</c>, where another local user
    /// could create <c>tik4net</c> first and plant the catalogs and grammar files the transports trust.
    /// </summary>
    [TestClass]
    public class CatalogCacheDefaultPathTests
    {
        [TestMethod]
        public void TheDefaultIsTik4netUnderLocalApplicationData()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Assert.IsFalse(string.IsNullOrEmpty(local), "this machine has no LocalApplicationData; the test cannot judge");

            Assert.AreEqual(Path.Combine(local, "tik4net"), TikConnectionSetup.DefaultCatalogCachePath);
        }

        [TestMethod]
        public void TheDefaultIsNotUnderTheTemporaryDirectory()
        {
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string cache = Path.GetFullPath(TikConnectionSetup.DefaultCatalogCachePath);
            Assert.IsFalse(cache.StartsWith(temp, StringComparison.OrdinalIgnoreCase), cache + " is under " + temp);
        }

        [TestMethod]
        public void ANewSetupAndANewNativeConnectionStartFromTheDefault()
        {
            Assert.AreEqual(TikConnectionSetup.DefaultCatalogCachePath, new TikConnectionSetup("192.0.2.1", "u", "").CatalogCachePath);
            using (var native = ConnectionFactory.CreateConnection(TikConnectionType.WinboxNative))
                Assert.AreEqual(TikConnectionSetup.DefaultCatalogCachePath, ((ITikCatalogCacheConnection)native).CatalogCachePath);
        }
    }
}
