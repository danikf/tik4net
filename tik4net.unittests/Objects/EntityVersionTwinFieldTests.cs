// EntityVersionTwinFieldTests.cs — a field RouterOS renamed between 6 and 7 is ONE property with
// TikPropertyAttribute.AlternateNames: read under whichever name the row carries, saved under the name it was
// read under.
//
// Two properties per name cannot work, which is why this is the shape: the mapper fills a field the row does
// not carry with the property's default, and for a string that is "" — so the property for the name the router
// does NOT print reads "", and `AvailableFrom ?? Address` stops at it.

using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip;
using tik4net.Objects.Tool;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    [TestClass]
    public class EntityVersionTwinFieldTests
    {
        private static TikFakeConnection Router(string command, params Dictionary<string, string>[] rows)
            => new TikFakeConnection()
                .WithResponse(
                    cmd => cmd.FirstOrDefault() == command,
                    rows.Select(r => (ITikSentence)new TikFakeReSentence(r))
                        .Concat(new ITikSentence[] { new TikFakeDoneSentence() }))
                .WithNonQuery(cmd => cmd.First().EndsWith("/set"));

        private static Dictionary<string, string> Service(string addressName, string value)
            => new Dictionary<string, string>
            {
                [".id"] = "*1", ["name"] = "ftp", ["port"] = "21", [addressName] = value,
                ["invalid"] = "false", ["disabled"] = "false",
            };

        private static Dictionary<string, string> Email(string serverName, string value)
            => new Dictionary<string, string>
            {
                [serverName] = value, ["port"] = "25", ["from"] = "<>", ["user"] = "", ["password"] = "",
            };

        [TestMethod]
        public void OnRouterOs6_TheServiceAccessListIsReadUnderAddress()
        {
            var ftp = Router("/ip/service/print", Service("address", "10.0.0.0/8")).LoadAll<IpService>().Single();

            Assert.AreEqual("10.0.0.0/8", ftp.Address);
        }

        [TestMethod]
        public void OnRouterOs7_TheServiceAccessListIsReadUnderAvailableFrom()
        {
            var ftp = Router("/ip/service/print", Service("available-from", "10.0.0.0/8")).LoadAll<IpService>().Single();

            Assert.AreEqual("10.0.0.0/8", ftp.Address);
        }

        [TestMethod]
        public void ARowCarryingBothNames_ReadsTheDeclaredOne()
        {
            // WinBox native reports both of RouterOS's words for such a field, with the same value.
            var row = Service("address", "10.0.0.0/8");
            row["available-from"] = "192.0.2.0/24";

            var ftp = Router("/ip/service/print", row).LoadAll<IpService>().Single();

            Assert.AreEqual("10.0.0.0/8", ftp.Address);
        }

        [TestMethod]
        public void ARowCarryingNeitherName_ReadsTheDefault()
        {
            var row = Service("address", "x");
            row.Remove("address");

            var ftp = Router("/ip/service/print", row).LoadAll<IpService>().Single();

            Assert.AreEqual("", ftp.Address);
        }

        [TestMethod]
        public void OnRouterOs6_TheEmailServerIsReadAndSavedUnderAddress()
        {
            var connection = Router("/tool/e-mail/print", Email("address", "192.0.2.25"));
            var email = connection.LoadSingle<ToolEmail>();
            Assert.AreEqual("192.0.2.25", email.Server);

            email.Server = "192.0.2.26";
            connection.Save(email);

            // 6.49.13 refuses `server` ("unknown parameter"): the save must use the name the row was read under.
            connection.AssertWasSent(rows => rows.First() == "/tool/e-mail/set" && rows.Contains("=address=192.0.2.26"));
            connection.AssertWasSent(rows => rows.First() != "/tool/e-mail/set" || !rows.Any(r => r.StartsWith("=server=")));
        }

        [TestMethod]
        public void OnRouterOs7_TheEmailServerIsReadAndSavedUnderServer()
        {
            var connection = Router("/tool/e-mail/print", Email("server", "192.0.2.25"));
            var email = connection.LoadSingle<ToolEmail>();
            Assert.AreEqual("192.0.2.25", email.Server);

            email.Server = "192.0.2.26";
            connection.Save(email);

            connection.AssertWasSent(rows => rows.First() == "/tool/e-mail/set" && rows.Contains("=server=192.0.2.26"));
        }

        [TestMethod]
        public void AClone_SavesUnderTheNameItsOriginalWasReadUnder()
        {
            var connection = Router("/tool/e-mail/print", Email("address", "192.0.2.25"));
            var clone = connection.LoadSingle<ToolEmail>().CloneEntity();

            clone.Server = "192.0.2.26";
            connection.Save(clone);

            connection.AssertWasSent(rows => rows.First() == "/tool/e-mail/set" && rows.Contains("=address=192.0.2.26"));
        }

        [TestMethod]
        public void AnEntityNeverRead_IsSavedUnderTheDeclaredName()
        {
            var connection = Router("/tool/e-mail/print", Email("server", "192.0.2.25"));

            connection.Save(new ToolEmail { Server = "192.0.2.26" });

            connection.AssertWasSent(rows => rows.First() == "/tool/e-mail/set" && rows.Contains("=server=192.0.2.26"));
        }
    }
}
