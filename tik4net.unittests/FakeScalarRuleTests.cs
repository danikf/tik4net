using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Testing;

namespace tik4net.unittests
{
    /// <summary>
    /// <see cref="TikFakeCommand"/>'s <c>ExecuteScalar()</c> with no target follows the rule every transport follows
    /// (D18, strict): a row's one value field other than <c>.id</c> / <c>.tag</c>; the <c>.id</c> when there is none;
    /// <see cref="TikCommandUnexpectedResponseException"/> for more than one. A fake laxer than the transports hides
    /// the bug it exists to catch.
    /// </summary>
    [TestClass]
    public class FakeScalarRuleTests
    {
        private static ITikCommand Command(params (string Key, string Value)[] row)
        {
            var connection = new TikFakeConnection().WithResponse(_ => true, new ITikSentence[]
            {
                new TikFakeReSentence(row.ToDictionary(w => w.Key, w => w.Value)),
                new TikFakeDoneSentence(),
            });
            return connection.CreateCommand("/interface/print");
        }

        [TestMethod]
        public void TheIdAndOneField_ReadsTheField()
            => Assert.AreEqual("a", Command((".id", "*1"), ("name", "a")).ExecuteScalar());

        [TestMethod]
        public void TheIdAlone_ReadsTheId()
            => Assert.AreEqual("*1", Command((".id", "*1")).ExecuteScalar());

        [TestMethod]
        public void TwoFields_Throw()
        {
            var ex = Assert.ThrowsException<TikCommandUnexpectedResponseException>(
                () => Command(("name", "a"), ("mtu", "1500")).ExecuteScalar());
            StringAssert.Contains(ex.Message, "the row has 2 (name, mtu)");
        }

        [TestMethod]
        public void TwoFields_ThrowFromTheOrDefaultFormToo()
            => Assert.ThrowsException<TikCommandUnexpectedResponseException>(
                () => Command(("name", "a"), ("mtu", "1500")).ExecuteScalarOrDefault("x"));
    }
}
