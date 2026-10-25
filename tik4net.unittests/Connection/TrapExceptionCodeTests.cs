using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Testing;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// <see cref="TikCommandTrapException.Code"/> and <see cref="TikCommandTrapException.CodeDescription"/> read the same
    /// for a trap built without a trap sentence as for a trap sentence that named no category: <c>-1</c> /
    /// <c>category not provided</c>, never null.
    /// </summary>
    [TestClass]
    public class TrapExceptionCodeTests
    {
        /// <summary>A caller's own trap, built from a message through the protected constructor.</summary>
        private sealed class MessageTrapException : TikCommandTrapException
        {
            public MessageTrapException(ITikCommand command, string message) : base(command, message) { }
        }

        private static ITikCommand Command() => new TikFakeConnection().CreateCommand("/ip/address/print");

        private static IEnumerable<object[]> TrapsWithoutASentence()
        {
            yield return new object[] { "TikNoSuchItemException(command)", new TikNoSuchItemException(Command()) };
            yield return new object[] { "TikPathNotMappedException", new TikPathNotMappedException(Command(), "/ip/address", "not mapped") };
            yield return new object[] { "protected (command, message)", new MessageTrapException(Command(), "refused") };
        }

        [DataTestMethod]
        [DynamicData(nameof(TrapsWithoutASentence), DynamicDataSourceType.Method)]
        public void CodeIsMinusOneWhenNoCategoryWasGiven(string constructor, TikCommandTrapException exception)
        {
            Assert.AreEqual("-1", exception.Code, constructor);
            Assert.AreEqual("category not provided", exception.CodeDescription, constructor);
        }
    }
}
