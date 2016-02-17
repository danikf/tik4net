using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tik4net.tests
{
    [TestClass]
    public class IntToTimeStringTestMethods
    {
        [TestMethod]
        public void FromTestMethod_None()
        {
            Assert.AreEqual("none", TikTimeHelper.ToTikTime(0));
        }
        [TestMethod]
        public void FromTestMethod_Second()
        {
            Assert.AreEqual("10s", TikTimeHelper.ToTikTime(10));
        }
        [TestMethod]
        public void FromTestMethod_Minute()
        {
            Assert.AreEqual("1m", TikTimeHelper.ToTikTime(60));
        }

        [TestMethod]
        public void FromTestMethod_Hour()
        {
            Assert.AreEqual("1h", TikTimeHelper.ToTikTime(3600));
        }
        [TestMethod]
        public void FromTestMethod_Day()
        {
            Assert.AreEqual("2d", TikTimeHelper.ToTikTime((3600 * 24 * 2)));
        }

        [TestMethod]
        public void FromTestMethod_Week()
        {
            Assert.AreEqual("1w", TikTimeHelper.ToTikTime((3600 * 24 * 7)));
        }

        [TestMethod]
        public void FromTestMethod_AllFields()
        {
            Assert.AreEqual("1w3d1h2m1s", TikTimeHelper.ToTikTime((1 + 120 + 3600 + 3600 * 24 * 10)));
        }

        [TestMethod]
        public void FromTestMethod_Over1Year()
        {
            Assert.AreEqual("71w3d", TikTimeHelper.ToTikTime((3600 * 24 * 500)));
        }


        [TestMethod]
        public void ToTestMethod_Second()
        {
            Assert.AreEqual(10, TikTimeHelper.FromTikTime("10s"));
        }
        [TestMethod]
        public void ToTestMethod_Minute()
        {
            Assert.AreEqual(60, TikTimeHelper.FromTikTime("1m"));
        }

        [TestMethod]
        public void ToTestMethod_Hour()
        {
            Assert.AreEqual(3600, TikTimeHelper.FromTikTime("1h"));
        }
        [TestMethod]
        public void ToTestMethod_Day()
        {
            Assert.AreEqual((3600 * 24 * 2), TikTimeHelper.FromTikTime("2d"));
        }

        [TestMethod]
        public void ToTestMethod_Week()
        {
            Assert.AreEqual((3600 * 24 * 7), TikTimeHelper.FromTikTime("1w"));
        }

        [TestMethod]
        public void ToTestMethod_AllFields()
        {
            Assert.AreEqual((1 + 120 + 3600 + 3600 * 24 * 10), TikTimeHelper.FromTikTime("1w3d1h2m1s"));
        }

        [TestMethod]
        public void ToTestMethod_Over1Year()
        {
            Assert.AreEqual((3600 * 24 * 500), TikTimeHelper.FromTikTime("71w3d"));
        }
    }
}