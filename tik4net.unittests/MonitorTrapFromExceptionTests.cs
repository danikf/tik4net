using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;

namespace tik4net.unittests
{
    /// <summary>
    /// What a background monitor/async worker tells the caller when its poll threw (P2.25).
    /// </summary>
    /// <remarks>
    /// The exception cannot cross the callback — the contract is a trap sentence — so this conversion is the
    /// only copy of the reason that survives. Reporting <c>ex.Message</c> alone is what made a router trap, a
    /// socket error and a bug in our own decoder arrive as the same unattributed line, which is the reason
    /// P2.14's flaky async/listen failures had nothing to diagnose. Same defect, and same fix, as the synthetic
    /// <c>!fatal</c> in <c>ApiConnection.CallCommandAsync</c>.
    /// </remarks>
    [TestClass]
    public class MonitorTrapFromExceptionTests
    {
        [TestMethod]
        public void ARouterTrapKeepsTheRoutersOwnWording()
        {
            // The router's text is the answer here; a "TikNoSuchItemException: " prefix would only bury it,
            // and consumers match on this message.
            var connection = new tik4net.Testing.TikFakeConnection();
            var command = connection.CreateCommand("/ip/address/print");
            var trap = TikTrapSentenceResult.FromException(
                new TikNoSuchItemException(command, new TikTrapSentenceResult("no such item")));
            Assert.AreEqual("no such item", trap.Message);
        }

        [TestMethod]
        public void AnythingElseCarriesItsTypeSoTheBlameIsVisible()
        {
            var trap = TikTrapSentenceResult.FromException(new NullReferenceException("Object reference not set."));
            StringAssert.Contains(trap.Message, "NullReferenceException");
            StringAssert.Contains(trap.Message, "Object reference not set.");
        }

        [TestMethod]
        public void TheInnerExceptionSurvives()
        {
            // A socket failure reaches us wrapped; the wrapper's message is the generic half and the inner one
            // is the half that says what actually happened.
            var trap = TikTrapSentenceResult.FromException(
                new InvalidOperationException("read failed", new System.IO.IOException("connection reset")));
            StringAssert.Contains(trap.Message, "read failed");
            StringAssert.Contains(trap.Message, "IOException");
            StringAssert.Contains(trap.Message, "connection reset");
        }

        [TestMethod]
        public void ANullExceptionStillProducesSomethingSayable()
        {
            Assert.IsFalse(string.IsNullOrEmpty(TikTrapSentenceResult.FromException(null).Message));
        }

        [TestMethod]
        public void TheMessageIsAlsoWhatTheSentenceExposesAsAWord()
        {
            // Callers read the trap through ITikTrapSentence.Words["message"], not through the property.
            var trap = TikTrapSentenceResult.FromException(new TimeoutException("timed out"));
            Assert.AreEqual(trap.Message, trap.Words["message"]);
        }
    }
}
