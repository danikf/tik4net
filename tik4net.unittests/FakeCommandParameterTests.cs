using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Testing;

namespace tik4net.unittests
{
    /// <summary>
    /// <see cref="TikFakeCommand"/> takes a name/value list as the real commands do (<c>GenericCommandShapeTests</c>,
    /// <c>ApiCommandWireShapeTests</c>), so a test against the fake cannot pass on a call the router-facing
    /// command refuses.
    /// </summary>
    [TestClass]
    public class FakeCommandParameterTests
    {
        [TestMethod]
        public void AddParameterAndValues_RefusesAnUnpairedLastName()
        {
            var cmd = new TikFakeConnection().CreateCommand("/ip/address/print");
            var ex = Assert.ThrowsException<ArgumentException>(() => cmd.AddParameterAndValues("name", "a", "comment"));

            StringAssert.Contains(ex.Message, "'comment'");
            Assert.AreEqual(0, cmd.Parameters.Count);
        }

        [TestMethod]
        public void CreateCommandAndParameters_RefusesAnUnpairedLastName()
        {
            var ex = Assert.ThrowsException<ArgumentException>(
                () => new TikFakeConnection().CreateCommandAndParameters("/ip/address/print", "name", "a", "comment"));

            StringAssert.Contains(ex.Message, "'comment'");
        }

        [TestMethod]
        public void PairsAreAddedInOrder()
        {
            var cmd = new TikFakeConnection().CreateCommandAndParameters("/ip/address/print", "name", "a", "comment", "b");

            Assert.AreEqual(2, cmd.Parameters.Count);
            Assert.AreEqual("comment", cmd.Parameters[1].Name);
            Assert.AreEqual("b", cmd.Parameters[1].Value);
        }
    }
}
