using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// The fake must be substitutable for the typing style the documentation tells people to prefer.
    /// </summary>
    /// <remarks>
    /// The wiki says to prefer a typed connection — <c>ITikCliConnection cli =
    /// setup.CreateTelnetConnection()</c> — over a runtime <c>Supports</c> check, because it turns a missing
    /// feature into a compile error. That advice was unusable in a test: a method taking
    /// <c>ITikApiConnection</c> or <c>ITikCliConnection</c> had nothing to pass it, because
    /// <see cref="TikFakeConnection"/> implemented only <c>ITikConnection</c> and two facets and is sealed.
    /// <para>
    /// A test double may be simpler than the real thing; it may not be <b>narrower</b> than the shape the
    /// library recommends, or the recommendation is one users cannot follow. This is written as a compile-time
    /// assertion — the assignments below are the test — because that is the failure it guards against.
    /// </para>
    /// </remarks>
    [TestClass]
    public class FakeConnectionTypedSubstitutionTests
    {
        [TestMethod]
        public void TheFakeSatisfiesEveryCompositeConnectionInterface()
        {
            var fake = new TikFakeConnection();

            ITikApiConnection api = fake;
            ITikRestConnection rest = fake;
            ITikCliConnection cli = fake;
            ITikMacCliConnection macCli = fake;
            ITikWinboxNativeConnection native = fake;
            ITikWinboxNativeMacConnection nativeMac = fake;

            Assert.IsNotNull(api);
            Assert.IsNotNull(rest);
            Assert.IsNotNull(cli);
            Assert.IsNotNull(macCli);
            Assert.IsNotNull(native);
            Assert.IsNotNull(nativeMac);
        }

        /// <summary>
        /// The facet members are settable, so a test can arrange configuration and assert on it.
        /// </summary>
        [TestMethod]
        public void TheOptionFacetsRoundTrip()
        {
            var fake = new TikFakeConnection
            {
                RouterMac = "AA:BB:CC:DD:EE:FF",
                AllowInvalidCertificate = true,
                SendTagWithSyncCommand = false,
                UseGuiNames = true,
                CatalogHandlerCount = 42,
            };

            Assert.AreEqual("AA:BB:CC:DD:EE:FF", ((ITikMacLayerConnection)fake).RouterMac);
            Assert.IsTrue(((ITikTlsConnection)fake).AllowInvalidCertificate);
            Assert.IsFalse(((ITikTaggedConnection)fake).SendTagWithSyncCommand);
            Assert.IsTrue(((ITikWinboxNativeConnection)fake).UseGuiNames);
            Assert.AreEqual(42, ((ITikWinboxNativeConnection)fake).CatalogHandlerCount);
        }

        /// <summary>
        /// An unscripted completion answers "nothing", which is a real router answer rather than an error.
        /// </summary>
        [TestMethod]
        public void CompletionIsScriptedAndEmptyWhenItIsNot()
        {
            var fake = new TikFakeConnection();
            fake.Completions["/interface/"] = new[] { "print", "set", "remove" };

            CollectionAssert.AreEqual(new[] { "print", "set", "remove" },
                ((tik4net.Cli.ITikCliCompletion)fake).CompleteCli("/interface/").ToArray());
            Assert.AreEqual(0, ((tik4net.Cli.ITikCliCompletion)fake).CompleteCli("/nothing/here ").Count);
        }
    }
}
