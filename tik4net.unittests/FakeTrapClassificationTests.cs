using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Testing;

namespace tik4net.unittests
{
    /// <summary>
    /// A trap registered on the fake throws what a transport throws for the same text, so code tested on the fake
    /// exercises its <c>catch (TikNoSuchItemException)</c>, and a <c>catch</c> of the base type does not pass for the
    /// wrong reason.
    /// </summary>
    [TestClass]
    public class FakeTrapClassificationTests
    {
        private static ITikCommand CommandTrappingWith(string message)
        {
            var connection = new TikFakeConnection().WithTrap(rows => rows.First() == "/ip/address/set", message);
            return connection.CreateCommand("/ip/address/set");
        }

        [DataTestMethod]
        [DataRow("no such item", typeof(TikNoSuchItemException))]
        [DataRow("no such command", typeof(TikNoSuchCommandException))]
        [DataRow("failure: already have such address", typeof(TikAlreadyHaveSuchItemException))]
        [DataRow("bad address", typeof(TikCommandTrapException))]
        public void ATrapThrowsTheExceptionItsTextClassifiesInto(string message, Type expected)
        {
            try
            {
                CommandTrappingWith(message).ExecuteNonQuery();
                Assert.Fail("The trap did not throw.");
            }
            catch (TikCommandTrapException thrown)
            {
                Assert.AreEqual(expected, thrown.GetType());
            }
        }

        [TestMethod]
        public void TheClassifiedExceptionIsStillATrapException()
        {
            Assert.IsInstanceOfType(
                Assert.ThrowsException<TikNoSuchItemException>(() => CommandTrappingWith("no such item").ExecuteList()),
                typeof(TikCommandTrapException));
        }

        [TestMethod]
        public void EveryReadPathClassifies()
        {
            Assert.ThrowsException<TikNoSuchItemException>(() => CommandTrappingWith("no such item").ExecuteScalar());
            Assert.ThrowsException<TikNoSuchItemException>(() => CommandTrappingWith("no such item").ExecuteSingleRow());
            Assert.ThrowsException<TikNoSuchItemException>(() => CommandTrappingWith("no such item").ExecuteList());
        }
    }
}
