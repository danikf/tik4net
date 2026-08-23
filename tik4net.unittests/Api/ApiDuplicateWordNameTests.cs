// ApiDuplicateWordNameTests.cs — the names the API transport invents, and telling them from real ones.
//
// RouterOS sends the same word twice in one sentence on a handful of rows: `trusted` on /certificate,
// `published` on /ip/arp, `dynamic` on /interface/list, `template` and `responder` on two IPsec menus. A
// dictionary cannot hold the second under the same key, so ApiSentence keeps it under base+2 rather than
// dropping it.
//
// That name is OURS, not the router's — and anything comparing this transport's vocabulary against another
// transport's has to subtract it, or every other transport looks a field short of the API. The rule lives
// with the code that applies it precisely so the two cannot drift apart.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    [TestClass]
    public class ApiDuplicateWordNameTests
    {
        private static ApiReSentence Sentence(params string[] words) => new ApiReSentence(words);

        [TestMethod]
        public void ASecondWordOfTheSameNameIsKeptRatherThanLost()
        {
            var s = Sentence("=.id=*1", "=name=ca", "=trusted=true", "=trusted=true");

            Assert.AreEqual("true", s.GetResponseField("trusted"));
            Assert.AreEqual("true", s.GetResponseField("trusted2"), "the second one is not dropped");
        }

        [TestMethod]
        public void TheInventedNameIsRecognisedAsOurs()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".id", "name", "trusted", "trusted2",
            };

            Assert.IsTrue(ApiSentence.IsDuplicateWorkaroundName("trusted2", names));
            Assert.IsFalse(ApiSentence.IsDuplicateWorkaroundName("trusted", names),
                "the router's own name stays a router name");
        }

        [TestMethod]
        public void AThirdCopyGetsTheNextNumberAndIsAlsoRecognised()
        {
            var s = Sentence("=addr=1", "=addr=2", "=addr=3");
            Assert.AreEqual("2", s.GetResponseField("addr2"));
            Assert.AreEqual("3", s.GetResponseField("addr3"));

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "addr", "addr2", "addr3" };
            Assert.IsTrue(ApiSentence.IsDuplicateWorkaroundName("addr3", names));
        }

        [TestMethod]
        public void ARouterFieldThatMerelyENDSInADigitIsNotOne()
        {
            // The rule has to be narrow: RouterOS has plenty of real names ending in a number, and calling
            // one of those ours would delete a field from the comparison instead of an artefact. It is only
            // an artefact when the BASE name is in the same row too — which is the one thing the workaround
            // guarantees and a coincidence does not.
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "wlan2", "ipv4-address", "sha256", "eap-methods",
            };

            Assert.IsFalse(ApiSentence.IsDuplicateWorkaroundName("wlan2", names),
                "no 'wlan' beside it, so nothing invented this");
            Assert.IsFalse(ApiSentence.IsDuplicateWorkaroundName("sha256", names));
        }

        [TestMethod]
        public void TheSuffixTheWorkaroundNeverWritesIsNotAccepted()
        {
            // The loop starts at 2 and never pads, so 'x1' and 'x02' cannot have come from it. A name that
            // could not have been invented here must not be treated as though it were.
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "x", "x1", "x02", "x2" };

            Assert.IsFalse(ApiSentence.IsDuplicateWorkaroundName("x1", names));
            Assert.IsFalse(ApiSentence.IsDuplicateWorkaroundName("x02", names));
            Assert.IsTrue(ApiSentence.IsDuplicateWorkaroundName("x2", names));
        }

        [TestMethod]
        public void ANameThatIsNothingButDigitsIsNotOne()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "", "2" };
            Assert.IsFalse(ApiSentence.IsDuplicateWorkaroundName("2", names));
        }
    }
}
