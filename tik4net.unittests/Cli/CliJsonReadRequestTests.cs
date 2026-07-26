// CliJsonReadRequestTests.cs — router-free tests for the REQUEST half of P2.17: that an entity carrying
// free-form text asks for JSON, and that the CLI builder wraps the print correctly while every other
// transport drops the marker.
//
// The command shapes asserted here were run against RouterOS 7.23.2 before being encoded:
//   :put [:serialize to=json [/file print detail as-value]]                  -> 27 rows of escaped JSON
//   :put [/file print detail as-value]                                       -> 1 row (the bug)

using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;
using tik4net.Cli;
using tik4net.Objects;
using tik4net.Objects.System;

namespace tik4net.unittests.Cli
{
    [TestClass]
    public class CliJsonReadRequestTests
    {
        private static ITikCommandParameter[] NoParameters => new ITikCommandParameter[0];

        [TestMethod]
        public void BuildPrint_AsJson_WrapsTheWholePrintAndClosesBothBrackets()
        {
            string plain = CliCommandBuilder.BuildPrint("/file/print", NoParameters);
            string json = CliCommandBuilder.BuildPrint("/file/print", NoParameters, asJson: true);

            Assert.AreEqual(":put [/file print as-value]", plain);
            Assert.AreEqual(":put [:serialize to=json [/file print as-value]]", json);
        }

        [TestMethod]
        public void BuildPrintStats_AsJson_WrapsToo()
        {
            Assert.AreEqual(
                ":put [:serialize to=json [/interface print stats as-value]]",
                CliCommandBuilder.BuildPrintStats("/interface/print", NoParameters, asJson: true));
        }

        [TestMethod]
        public void BuildPrint_AsJson_KeepsDetailAndTheWhereClauseInsideTheWrapper()
        {
            var parameters = new ITikCommandParameter[]
            {
                new FakeParameter("detail", "", TikCommandParameterFormat.NameValue),
                new FakeParameter("name", "um5files/js/login.js", TikCommandParameterFormat.Filter),
            };

            Assert.AreEqual(
                ":put [:serialize to=json [/file print detail as-value where name=\"um5files/js/login.js\"]]",
                CliCommandBuilder.BuildPrint("/file/print", parameters, asJson: true));
        }

        [TestMethod]
        public void BuildPrint_NeverEmitsTheMarkerItselfAsACliWord()
        {
            var parameters = new ITikCommandParameter[]
            {
                new FakeParameter(TikSpecialProperties.CliJson, "", TikCommandParameterFormat.NameValue),
            };

            string built = CliCommandBuilder.BuildPrint("/file/print", parameters, asJson: true);

            Assert.IsFalse(built.Contains(TikSpecialProperties.CliJson),
                "The .cli-json marker is a client-side signal and must never reach the router: " + built);
        }

        [TestMethod]
        public void FileEntity_AsksForJson_BecauseContentsIsFreeText()
        {
            var metadata = TikEntityMetadataCache.GetMetadata<File>();

            Assert.IsTrue(metadata.HasFreeTextProperties);
            Assert.IsTrue(metadata.Properties.Single(p => p.FieldName == "contents").IsFreeText);
            Assert.IsFalse(metadata.Properties.Single(p => p.FieldName == "name").IsFreeText);
        }

        [TestMethod]
        public void OrdinaryEntity_DoesNotAskForJson()
        {
            // The wrapper costs a round trip's worth of router-side work and a 7.13 floor, so it must stay
            // opt-in rather than becoming the default read path for every entity.
            Assert.IsFalse(TikEntityMetadataCache.GetMetadata<tik4net.Objects.Ip.IpAddress>().HasFreeTextProperties);
        }

        private sealed class FakeParameter : ITikCommandParameter
        {
            public FakeParameter(string name, string value, TikCommandParameterFormat format)
            {
                Name = name;
                Value = value;
                ParameterFormat = format;
            }

            public string Name { get; set; }
            public string Value { get; set; }
            public TikCommandParameterFormat ParameterFormat { get; set; }
        }
    }
}
