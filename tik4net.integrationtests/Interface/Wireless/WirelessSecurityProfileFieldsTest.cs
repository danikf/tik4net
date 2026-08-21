using System;
using System.Configuration;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;

namespace tik4net.integrationtests
{
    /// <summary>
    /// G8: the RADIUS tab and the Static Keys tab of <c>/interface/wireless/security-profiles</c>.
    /// </summary>
    /// <remarks>
    /// This path was the path-map audit's one standing MISMATCH — 19 of 39 fields shared. Two causes, and
    /// the second was not what the item assumed:
    /// <list type="bullet">
    /// <item>the RADIUS tab drops the <c>radius-</c> prefix the API spells out and renames three fields, the
    /// same shape as <c>/ip/hotspot/profile</c>'s RADIUS tab — seven aliases, plus
    /// <c>static-transmit-key</c> to <c>transmit-key</c>;</item>
    /// <item>the Static Keys tab was assumed missing from the wire. It is not: with
    /// <c>mode=static-keys-required</c> and <c>static-algo-0=40bit-wep</c> the record carries
    /// <c>0x7=1</c> and <c>0xB</c>. The <c>.jg</c> wraps each pair in a <c>type:'tuple'</c> whose two
    /// children carry ids but no names, while RouterOS splits every tuple into two fields.</item>
    /// </list>
    /// <para>Everything is asserted through the binary API rather than by reading back over the transport
    /// under test: a write that lands on the wrong key is one the router accepts, answers and ignores, and
    /// reading it back through the same wrong key would agree with itself (G4).</para>
    /// </remarks>
    [TestClass]
    public class WirelessSecurityProfileFieldsTest : TestBase
    {
        private const string ProfilePath = "/interface/wireless/security-profiles";

        private static ITikConnection OpenApi()
        {
            var api = ConnectionFactory.CreateConnection(TikConnectionType.Api);
            api.Open(ConfigurationManager.AppSettings["host"],
                     ConfigurationManager.AppSettings["user"],
                     ConfigurationManager.AppSettings["pass"] ?? "");
            return api;
        }

        private static string CreateProfile(ITikConnection api)
        {
            string name = "t4n" + Guid.NewGuid().ToString("N").Substring(0, 10);
            return api.CreateCommandAndParameters(ProfilePath + "/add",
                "name", name, "mode", "static-keys-required").ExecuteScalar();
        }

        private static string ReadBack(ITikConnection api, string id, string field)
        {
            var row = api.CreateCommandAndParameters(ProfilePath + "/print", TikCommandParameterFormat.Filter, ".id", id)
                .ExecuteList().Single();
            return row.GetResponseFieldOrDefault(field, null);
        }

        /// <summary>
        /// The RADIUS tab's renamed fields must resolve for a write — and land on the field the API reports
        /// under the <c>radius-</c> name.
        /// </summary>
        [TestMethod]
        public void RadiusTabFieldsWriteThroughTheirApiNames()
        {
            EnsureCommandAvailable(ProfilePath);
            using (var api = OpenApi())
            {
                string id = CreateProfile(api);
                try
                {
                    Connection.CreateCommandAndParameters(ProfilePath + "/set",
                        TikSpecialProperties.Id, id,
                        "radius-mac-authentication", "yes",
                        "radius-mac-accounting", "yes",
                        "radius-called-format", "ssid",
                        "radius-mac-mode", "as-username-and-password").ExecuteNonQuery();

                    Assert.AreEqual("true", ReadBack(api, id, "radius-mac-authentication"));
                    Assert.AreEqual("true", ReadBack(api, id, "radius-mac-accounting"));
                    Assert.AreEqual("ssid", ReadBack(api, id, "radius-called-format"));
                    Assert.AreEqual("as-username-and-password", ReadBack(api, id, "radius-mac-mode"));
                }
                finally
                {
                    api.CreateCommandAndParameters(ProfilePath + "/remove",
                        TikSpecialProperties.Id, id).ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// The case of a MAC format is a VALUE, not decoration: this menu offers the same seven formats
        /// twice, upper then lower, and the case decides how the MAC reaches the RADIUS server. Writing the
        /// lowercase one must not land on its uppercase twin.
        /// </summary>
        [TestMethod]
        public void ALowercaseMacFormatIsNotWrittenAsItsUppercaseTwin()
        {
            EnsureCommandAvailable(ProfilePath);
            using (var api = OpenApi())
            {
                string id = CreateProfile(api);
                try
                {
                    Connection.CreateCommandAndParameters(ProfilePath + "/set",
                        TikSpecialProperties.Id, id,
                        "radius-mac-format", "xx:xx:xx:xx:xx:xx").ExecuteNonQuery();
                    Assert.AreEqual("xx:xx:xx:xx:xx:xx", ReadBack(api, id, "radius-mac-format"),
                        "a case-insensitive match returns whichever member comes first — the uppercase one");

                    Connection.CreateCommandAndParameters(ProfilePath + "/set",
                        TikSpecialProperties.Id, id,
                        "radius-mac-format", "XX-XX-XX-XX-XX-XX").ExecuteNonQuery();
                    Assert.AreEqual("XX-XX-XX-XX-XX-XX", ReadBack(api, id, "radius-mac-format"),
                        "the separator matters too — 'XX XX XX XX XX XX' folds onto this one");

                    // And the read direction: the value must come back in the case it was set in.
                    var mine = Connection.CreateCommandAndParameters(ProfilePath + "/print", TikCommandParameterFormat.Filter, ".id", id)
                        .ExecuteList().Single();
                    Assert.AreEqual("XX-XX-XX-XX-XX-XX", mine.GetResponseFieldOrDefault("radius-mac-format", null));
                }
                finally
                {
                    api.CreateCommandAndParameters(ProfilePath + "/remove",
                        TikSpecialProperties.Id, id).ExecuteNonQuery();
                }
            }
        }

        /// <summary>The Static Keys tab: each tuple is two API fields, and both must round-trip.</summary>
        [TestMethod]
        public void StaticKeyTuplesReadAndWriteAsTwoFieldsEach()
        {
            EnsureCommandAvailable(ProfilePath);
            using (var api = OpenApi())
            {
                string id = CreateProfile(api);
                try
                {
                    Connection.CreateCommandAndParameters(ProfilePath + "/set",
                        TikSpecialProperties.Id, id,
                        // The key lengths are the router's, not ours: 40-bit WEP takes 10 hex characters and
                        // 104-bit takes 26. Setting an algorithm without a key of the matching length is
                        // refused with "too short key", which is a useful thing for this test to respect
                        // rather than work around — it means the pair really did reach the same record.
                        "static-algo-0", "40bit-wep",
                        "static-key-0", "1234567890",
                        "static-algo-2", "104bit-wep",
                        "static-key-2", "12345678901234567890123456",
                        "static-transmit-key", "key-2").ExecuteNonQuery();

                    Assert.AreEqual("40bit-wep", ReadBack(api, id, "static-algo-0"));
                    Assert.AreEqual("1234567890", ReadBack(api, id, "static-key-0"));
                    Assert.AreEqual("104bit-wep", ReadBack(api, id, "static-algo-2"),
                        "the .jg spells this one '104 bit wep', which normalizes to a value RouterOS rejects");
                    Assert.AreEqual("key-2", ReadBack(api, id, "static-transmit-key"));

                    // The read direction, over the transport under test. Only the ALGORITHM half is
                    // asserted here: RouterOS's own CLI omits every secret-typed field from
                    // `print as-value` — static-key-*, the pre-shared keys, mschapv2-password — with or
                    // without `detail`, so the five CLI transports never see a key value. That is the
                    // router's decision, not a gap in this client, and the write landing correctly is
                    // already proven by the API read-backs above.
                    var mine = Connection.CreateCommandAndParameters(ProfilePath + "/print", TikCommandParameterFormat.Filter, ".id", id)
                        .ExecuteList().Single();
                    Assert.AreEqual("40bit-wep", mine.GetResponseFieldOrDefault("static-algo-0", null));
                    Assert.AreEqual("104bit-wep", mine.GetResponseFieldOrDefault("static-algo-2", null));
                    Assert.AreEqual("key-2", mine.GetResponseFieldOrDefault("static-transmit-key", null));
                }
                finally
                {
                    api.CreateCommandAndParameters(ProfilePath + "/remove",
                        TikSpecialProperties.Id, id).ExecuteNonQuery();
                }
            }
        }
    }
}
