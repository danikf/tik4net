// CliJsonParserTests.cs — router-free unit tests for CliJsonParser (P2.17).
//
// Every fixture below is a VERBATIM capture from RouterOS 7.23.2 over telnet
// (Tools/probes/telnet-cli-probe.ps1), so the shapes asserted here are the router's, not a guess:
//
//   :put [:serialize to=json [/file print detail as-value where name="um5files/js/login.js"]]
//   :put [:serialize to=json [/system resource print as-value]]
//   :put [:serialize to=json [/certificate print detail as-value]]
//   :put [:serialize to=json [/ip firewall filter print as-value where comment="t4n-nonexistent"]]
//
// The point of the whole exercise is the first one: over as-value the same read returned 1 record
// instead of 27, because a file body contains ';', '=' and newlines and as-value escapes nothing.

using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    [TestClass]
    public class CliJsonParserTests
    {
        // Captured verbatim. Note the embedded ';' (in "var postData = {};"), the '=' signs and the \n
        // escapes — every one of them a separator in as-value, all harmless here.
        private const string LoginJsFile =
            "[{\".id\":\"**iCZmBt9q4djvHQJupsHY er1RuFc04iP0l\"," +
            "\"contents\":\"function loginSubmited() {\\n    var postData = {};\\n" +
            "    postData.username = document.getElementById(\\\"username\\\").value;\\n}\\n\"," +
            "\"last-modified\":\"2026-07-25 10:23:32\"," +
            "\"name\":\"um5files/js/login.js\",\"size\":752,\"type\":\".js file\"}]";

        [TestMethod]
        public void ParseJson_FileContents_KeepsSeparatorsInsideTheValue()
        {
            var records = CliJsonParser.ParseJson(LoginJsFile);

            Assert.AreEqual(1, records.Count);
            var record = records[0];
            Assert.AreEqual("um5files/js/login.js", record.GetResponseField("name"));
            Assert.AreEqual("752", record.GetResponseField("size"));

            string contents = record.GetResponseField("contents");
            StringAssert.Contains(contents, "var postData = {};");
            StringAssert.Contains(contents, "getElementById(\"username\")");
            // The ';' and '=' stayed inside the value instead of starting new fields/records.
            Assert.AreEqual(6, record.Words.Count);
        }

        [TestMethod]
        public void ParseJson_EmptyResultSet_ReturnsNoRecords()
        {
            // What the router answers for a where-clause that matches nothing.
            Assert.AreEqual(0, CliJsonParser.ParseJson("[]").Count);
            Assert.AreEqual(0, CliJsonParser.ParseJson("").Count);
        }

        [TestMethod]
        public void ParseJson_Singleton_IsABareObjectAndYieldsOneRecord()
        {
            // /system resource serialises WITHOUT the enclosing array — a list parser would see nothing.
            const string resource =
                "{\"architecture-name\":\"x86_64\",\"cpu-count\":16,\"cpu-load\":0," +
                "\"factory-software\":7.100000,\"uptime\":\"06:42:44\",\"version\":\"7.23.2 (stable)\"}";

            var records = CliJsonParser.ParseJson(resource);

            Assert.AreEqual(1, records.Count);
            Assert.AreEqual("x86_64", records[0].GetResponseField("architecture-name"));
            Assert.AreEqual("16", records[0].GetResponseField("cpu-count"));
        }

        [TestMethod]
        public void ParseJson_MultiValueField_IsJoinedTheWayTheApiReportsIt()
        {
            const string certificate =
                "[{\".id\":\"*1\",\"authority\":true,\"issued\":false,\"key-size\":2048.000000," +
                "\"key-usage\":[\"key-cert-sign\",\"crl-sign\"],\"name\":\"ca-tik4net\"," +
                "\"subject-alt-name\":[]}]";

            var record = CliJsonParser.ParseJson(certificate).Single();

            Assert.AreEqual("key-cert-sign,crl-sign", record.GetResponseField("key-usage"));
            Assert.AreEqual("", record.GetResponseField("subject-alt-name"));
            Assert.AreEqual("true", record.GetResponseField("authority"));
            Assert.AreEqual("false", record.GetResponseField("issued"));
        }

        [TestMethod]
        public void ParseJson_IntegralFloat_LosesTheFractionSoIntPropertiesConvert()
        {
            // RouterOS renders some integer fields with a fractional part; the API reports the same field
            // as a plain integer, and the mapper's long.Parse would throw on "2048.000000".
            var record = CliJsonParser.ParseJson("[{\"key-size\":2048.000000}]").Single();
            Assert.AreEqual("2048", record.GetResponseField("key-size"));
        }

        [TestMethod]
        public void NormalizeNumber_KeepsGenuineFractionsAndPassesThroughWhatItCannotHold()
        {
            Assert.AreEqual("2048", CliJsonParser.NormalizeNumber("2048.000000"));
            Assert.AreEqual("7.1", CliJsonParser.NormalizeNumber("7.100000"));
            Assert.AreEqual("0", CliJsonParser.NormalizeNumber("0"));
            Assert.AreEqual("-3", CliJsonParser.NormalizeNumber("-3.0"));
            Assert.AreEqual("1.5", CliJsonParser.NormalizeNumber("1.5"));
            // Beyond decimal's range: hand it over untouched rather than mangle it — the mapper's own
            // conversion error is then the honest outcome.
            Assert.AreEqual("1e400", CliJsonParser.NormalizeNumber("1e400"));
        }

        [TestMethod]
        public void ParseJson_NullField_BecomesEmptyString()
        {
            var record = CliJsonParser.ParseJson("[{\"comment\":null}]").Single();
            Assert.AreEqual("", record.GetResponseField("comment"));
        }

        [TestMethod]
        public void ParseJson_NestedObject_ThrowsInsteadOfInventingAValue()
        {
            // Never observed in as-value output. If it ever appears, the caller must hear about it rather
            // than receive raw JSON text that looks like a legitimate value (P2.25).
            var ex = Assert.ThrowsException<TikSentenceException>(
                () => CliJsonParser.ParseJson("[{\"nested\":{\"a\":1}}]"));
            StringAssert.Contains(ex.Message, "nested");
        }

        [TestMethod]
        public void ParseJson_RawLineBreakInjectedByTheTerminal_DoesNotBreakTheParse()
        {
            // Measured on winboxcli: a 30 KB /file read arrives with a raw 0x0A at offset 10001, mid-word
            // inside a CSS body, where telnet delivers the same read unbroken. A raw control character can
            // never be part of a JSON string — ':serialize' escapes real newlines as the two characters
            // \n — so it is terminal framing and is dropped.
            var record = CliJsonParser
                .ParseJson("[{\"contents\":\"color\n: rgb(111, 111, 111);\",\"name\":\"user.css\"}]")
                .Single();

            Assert.AreEqual("color: rgb(111, 111, 111);", record.GetResponseField("contents"));
            Assert.AreEqual("user.css", record.GetResponseField("name"));
        }

        [TestMethod]
        public void StripRawControlCharacters_LeavesEscapedNewlinesAlone()
        {
            // The two-character escape is what a REAL newline in a file body looks like, and it must
            // survive — otherwise the fix would corrupt exactly the values it exists to protect.
            const string escaped = "[{\"contents\":\"line1\\nline2\"}]";
            Assert.AreEqual(escaped, CliJsonParser.StripRawControlCharacters(escaped));

            var record = CliJsonParser.ParseJson(escaped).Single();
            Assert.AreEqual("line1\nline2", record.GetResponseField("contents"));
        }

        [TestMethod]
        public void ParseJson_MalformedOutput_ThrowsAndQuotesWhatArrived()
        {
            var ex = Assert.ThrowsException<TikSentenceException>(
                () => CliJsonParser.ParseJson("[{\"name\":\"eth"));
            StringAssert.Contains(ex.Message, "not valid JSON");
            StringAssert.Contains(ex.Message, "eth");
        }
    }
}
