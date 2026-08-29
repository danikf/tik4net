using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// The fake sentences must honour the same non-null contract the real transports do.
    /// </summary>
    /// <remarks>
    /// <c>ITikTrapSentence.CategoryCode</c>, <c>ITikTrapSentence.CategoryDescription</c> and
    /// <c>ITikSentence.Tag</c> are declared plain <c>string</c>. Every real implementation upholds that —
    /// <c>ApiTrapSentence</c> falls back to <c>"unknown"</c> for a code it does not recognise, and every real
    /// sentence type defaults its tag to the empty string.
    /// <para>
    /// A test double is allowed to be simpler than the real thing; it is <b>not</b> allowed to be weaker.
    /// Handing out a null from behind a non-nullable signature makes a unit test able to fail in the one way
    /// a live router cannot, and the failure lands on the caller's code, which the compiler had told them was
    /// safe. That is the opposite of what the testing package is for, so it is pinned here rather than left
    /// to the next reader of the constructor.
    /// </para>
    /// </remarks>
    [TestClass]
    public class FakeSentenceContractTests
    {
        [TestMethod]
        public void ATrapSentenceBuiltTheOrdinaryWayHasNoNullsInIt()
        {
            // WithTrap(predicate, message) is the documented ordinary case, and it supplies neither the
            // category code nor a tag - so this is exactly the object a normal test gets.
            var trap = new TikFakeTrapSentence("no such item");

            Assert.IsNotNull(trap.Message);
            Assert.IsNotNull(trap.CategoryCode, "CategoryCode is declared non-nullable on ITikTrapSentence");
            Assert.IsNotNull(trap.CategoryDescription,
                "CategoryDescription is declared non-nullable; ApiTrapSentence answers 'unknown' rather than null");
            Assert.IsNotNull(trap.Tag, "Tag is declared non-nullable on ITikSentence");
        }

        [TestMethod]
        public void AnOmittedCategoryAndTagAreEmptyRatherThanNull()
        {
            var trap = new TikFakeTrapSentence("already have such item");

            Assert.AreEqual(string.Empty, trap.CategoryCode);
            Assert.AreEqual(string.Empty, trap.Tag);
            Assert.AreEqual("unknown", trap.CategoryDescription);
        }

        [TestMethod]
        public void SuppliedValuesAreKept()
        {
            var trap = new TikFakeTrapSentence("no such item", categoryCode: "1", tag: "7");

            Assert.AreEqual("1", trap.CategoryCode);
            Assert.AreEqual("7", trap.Tag);
        }
    }
}
