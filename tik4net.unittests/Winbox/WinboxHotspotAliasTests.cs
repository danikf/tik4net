// WinboxHotspotAliasTests.cs — router-free tests for the hotspot tables' field names: the WinBox windows write the
// units into the label ('Rate Limit (rx/tx)') where the API says rate-limit. Measured on 7.24.4 on a t4n user
// profile with rate-limit=1M/2M: the API prints rate-limit=1M/2M and the M2 record carries the same text.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxHotspotAliasTests
    {
        private const string UserProfileWindow =
            "[{name:'Hotspot User Profile',title:'User Profiles',type:'map',path:[ 63,3 ],c:[" +
            "{name:'Name',type:'string',id:'s1'}," +
            "{name:'Rate Limit (rx/tx)',type:'string',id:'s17'}" +
            "]}]";

        private static readonly int[] UserProfile = { 63, 3 };

        private static WinboxFieldResolver Resolver(WinboxJgCatalog catalog)
            => new WinboxFieldResolver("/ip/hotspot/user/profile", UserProfile, catalog, new Dictionary<string, int>());

        private static WinboxJgCatalog Catalog()
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(UserProfileWindow), "the trimmed window must parse");
            return catalog;
        }

        [TestMethod]
        public void AUserProfileReportsItsRateLimitUnderTheApiName()
        {
            var catalog = Catalog();
            var resolver = Resolver(catalog);
            var rec = new Dictionary<int, Tuple<string, object>>
            {
                [0x1] = Tuple.Create("str", (object)"t4n"),
                [0x17] = Tuple.Create("str", (object)"1M/2M"),
            };

            var decoded = new WinboxRecordCodec(null, catalog)
                .DecodeRecord(rec, resolver.BuildKeyToApiName(), resolver.BuildKeyToField());

            Assert.AreEqual("1M/2M", decoded["rate-limit"]);
            Assert.IsFalse(decoded.ContainsKey("rate-limit-(rx/tx)"));
        }

        [TestMethod]
        public void AUserProfileKeepsItsDefaultFlag()
        {
            // The alias set replaced the path's plain HotspotDefault(); `default` must still come from the *0 id.
            var catalog = Catalog();
            var resolver = Resolver(catalog);
            var rec = new Dictionary<int, Tuple<string, object>>
            {
                [0xFE0001] = Tuple.Create("u32", (object)0u),
                [0x1] = Tuple.Create("str", (object)"default"),
            };

            var decoded = new WinboxRecordCodec(null, catalog)
                .DecodeRecord(rec, resolver.BuildKeyToApiName(), resolver.BuildKeyToField(), resolver.DerivedBoolFields,
                    null, null);

            Assert.AreEqual("true", decoded["default"]);
        }
    }
}
