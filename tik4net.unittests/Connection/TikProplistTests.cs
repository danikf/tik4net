using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// <see cref="TikProplist.Apply"/> is the one trim REST and WinBox native put their rows through: the binary
    /// API returns exactly the listed fields, and every transport keeps that contract.
    /// </summary>
    [TestClass]
    public class TikProplistTests
    {
        private static IList<TikRecordSentence> Rows() => new List<TikRecordSentence>
        {
            new TikRecordSentence(new Dictionary<string, string> { [".id"] = "*1", ["name"] = "ether1", ["mtu"] = "1500" }),
            new TikRecordSentence(new Dictionary<string, string> { [".id"] = "*2", ["name"] = "ether2", ["mtu"] = "1400" }),
        };

        [TestMethod]
        public void WithoutAProplistTheSameListComesBack()
        {
            var rows = Rows();
            var descriptor = new TikCommandDescriptor("/interface/print",
                new List<ITikCommandParameter> { new TikCommandParameter("name", "ether1", TikCommandParameterFormat.Filter) });

            Assert.AreSame(rows, TikProplist.Apply(descriptor, rows));
        }

        [TestMethod]
        public void WithAProplistOnlyTheListedFieldsAreKept_CaseInsensitively()
        {
            var descriptor = new TikCommandDescriptor("/interface/print",
                new List<ITikCommandParameter> { new TikCommandParameter(TikSpecialProperties.Proplist, "NAME, .id") });

            var trimmed = TikProplist.Apply(descriptor, Rows());

            Assert.AreEqual(2, trimmed.Count);
            foreach (var row in trimmed)
                CollectionAssert.AreEquivalent(new[] { ".id", "name" }, row.Words.Keys.ToArray());
            Assert.AreEqual("ether2", trimmed[1].Words["name"]);
        }
    }
}
