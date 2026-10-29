// WinboxHandlerTests.cs — an M2 handler is a value (WinBox + MAC review, D8 b).

using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxHandlerTests
    {
        [TestMethod]
        public void TwoHandlersWithOnePath_AreEqual_AndOneKey()
        {
            var a = new WinboxHandler(20, 0);
            WinboxHandler b = new[] { 20, 0 };
            Assert.IsTrue(a == b);
            Assert.IsFalse(a != b);
            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            var set = new HashSet<WinboxHandler> { a, b };
            Assert.AreEqual(1, set.Count);
            Assert.AreNotEqual(new WinboxHandler(20, 0), new WinboxHandler(20, 12));
            Assert.AreNotEqual(new WinboxHandler(20), new WinboxHandler(20, 0), "a prefix is another handler");
        }

        [TestMethod]
        public void TheTextFormIsTheBracketedPath()
        {
            Assert.AreEqual("[20,0]", new WinboxHandler(20, 0).ToString());
            Assert.AreEqual("[17]", new WinboxHandler(17).ToString());
            Assert.AreEqual("[]", default(WinboxHandler).ToString());
        }

        [TestMethod]
        public void TheHandlerOwnsItsPath()
        {
            var path = new[] { 20, 0 };
            WinboxHandler handler = path;
            path[1] = 12;
            Assert.AreEqual(new WinboxHandler(20, 0), handler, "a caller's array changed afterwards");
            handler.ToArray()[1] = 12;
            Assert.AreEqual(new WinboxHandler(20, 0), handler, "the wire copy changed");
            Assert.AreEqual(2, handler.Count);
            Assert.AreEqual(0, handler[1]);
        }

        [TestMethod]
        public void TheEmptyPath_IsDefault()
        {
            Assert.AreEqual(default(WinboxHandler), new WinboxHandler());
            Assert.AreEqual(0, default(WinboxHandler).Count);
            CollectionAssert.AreEqual(new int[0], default(WinboxHandler).ToArray());
        }
    }
}
