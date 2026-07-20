using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tik4net.unittests
{
    [TestClass]
    public class TikTrapClassifierTests
    {
        [TestMethod]
        public void Classify_Empty_ReturnsGeneric()
        {
            Assert.AreEqual(TikTrapKind.Generic, TikTrapClassifier.Classify(""));
            Assert.AreEqual(TikTrapKind.Generic, TikTrapClassifier.Classify(null));
        }

        // ── no such item ────────────────────────────────────────────────────

        [TestMethod]
        public void Classify_ApiNoSuchItem()
        {
            Assert.AreEqual(TikTrapKind.NoSuchItem, TikTrapClassifier.Classify("no such item"));
        }

        [TestMethod]
        public void Classify_CliExpectedItemId()
        {
            Assert.AreEqual(TikTrapKind.NoSuchItem, TikTrapClassifier.Classify("expected item id (line 1 column 12)"));
        }

        [TestMethod]
        public void Classify_RestMissingOrInvalidResourceIdentifier()
        {
            Assert.AreEqual(TikTrapKind.NoSuchItem, TikTrapClassifier.Classify("missing or invalid resource identifier"));
        }

        // ── no such command ─────────────────────────────────────────────────

        [TestMethod]
        public void Classify_ApiNoSuchCommand()
        {
            Assert.AreEqual(TikTrapKind.NoSuchCommand, TikTrapClassifier.Classify("no such command"));
        }

        [TestMethod]
        public void Classify_CliBadCommandName()
        {
            Assert.AreEqual(TikTrapKind.NoSuchCommand, TikTrapClassifier.Classify("bad command name foo (line 1 column 1)"));
        }

        [TestMethod]
        public void Classify_CliExpectedEndOfCommand()
        {
            Assert.AreEqual(TikTrapKind.NoSuchCommand, TikTrapClassifier.Classify("expected end of command (line 1 column 5)"));
        }

        [TestMethod]
        public void Classify_CliSyntaxError()
        {
            Assert.AreEqual(TikTrapKind.NoSuchCommand, TikTrapClassifier.Classify("syntax error (line 1 column 1)"));
        }

        [TestMethod]
        public void Classify_NoSuchDirectory()
        {
            Assert.AreEqual(TikTrapKind.NoSuchCommand, TikTrapClassifier.Classify("no such directory"));
        }

        // ── already have such item ──────────────────────────────────────────

        [TestMethod]
        public void Classify_ApiFailureAlreadyHaveSuchAddress()
        {
            Assert.AreEqual(TikTrapKind.AlreadyHaveSuchItem, TikTrapClassifier.Classify("failure: already have such address"));
        }

        [TestMethod]
        public void Classify_AlreadyHaveDeviceWithSuchName()
        {
            Assert.AreEqual(TikTrapKind.AlreadyHaveSuchItem, TikTrapClassifier.Classify("already have device with such name"));
        }

        [TestMethod]
        public void Classify_ItemWithSuchNameAlreadyExists()
        {
            Assert.AreEqual(TikTrapKind.AlreadyHaveSuchItem, TikTrapClassifier.Classify("item with such name already exists"));
        }

        // ── generic / case-insensitivity ────────────────────────────────────

        [TestMethod]
        public void Classify_UnknownMessage_ReturnsGeneric()
        {
            Assert.AreEqual(TikTrapKind.Generic, TikTrapClassifier.Classify("some unrelated router error"));
        }

        [TestMethod]
        public void Classify_IsCaseInsensitive()
        {
            Assert.AreEqual(TikTrapKind.NoSuchItem, TikTrapClassifier.Classify("NO SUCH ITEM"));
        }
    }
}
