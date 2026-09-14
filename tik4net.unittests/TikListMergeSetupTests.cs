using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;
using tik4net.Testing;

namespace tik4net.unittests
{
    /// <summary>
    /// A merge without <see cref="TikListMerge{TEntity}.WithKey"/> cannot tell which router row is which expected
    /// row. It must be refused with a message naming the missing call, not fail on a null delegate somewhere inside.
    /// </summary>
    [TestClass]
    public class TikListMergeSetupTests
    {
        private static TikListMerge<FirewallAddressList> MergeWithoutKey()
        {
            var expected = new List<FirewallAddressList> { new FirewallAddressList { List = "L", Address = "10.0.0.1" } };
            var original = new List<FirewallAddressList> { new FirewallAddressList { List = "L", Address = "10.0.0.2" } };
            return new TikFakeConnection().CreateMerge(expected, original).Field(a => a.Comment);
        }

        [TestMethod]
        public void Simulate_WithoutKey_IsRefusedNamingWithKey()
        {
            var ex = Assert.ThrowsException<InvalidOperationException>(
                () => MergeWithoutKey().Simulate(out _, out _, out _, out _));
            StringAssert.Contains(ex.Message, "WithKey");
        }

        [TestMethod]
        public void Save_WithoutKey_IsRefusedBeforeTouchingTheRouter()
        {
            var ex = Assert.ThrowsException<InvalidOperationException>(() => MergeWithoutKey().Save());
            StringAssert.Contains(ex.Message, "WithKey");
        }
    }
}
