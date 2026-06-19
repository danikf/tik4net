using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Routing.Filter;

namespace tik4net.tests
{
    [TestClass]
    public class RoutingFilterRuleTest : TestBase
    {
        [TestMethod]
        public void ListRoutingFilterRulesWillNotFail()
        {
            EnsureCommandAvailable("/routing/filter/rule");
            var list = Connection.LoadAll<RoutingFilterRule>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddRoutingFilterRuleWillNotFail()
        {
            EnsureCommandAvailable("/routing/filter/rule");
            string marker = "t4n-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var rule = new RoutingFilterRule
            {
                Chain = marker,
                Rule = "if (dst == 10.0.0.0/8) { accept }",
                Comment = marker,
            };
            Connection.Save(rule);

            var loaded = Connection.LoadById<RoutingFilterRule>(rule.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Chain);
            Assert.AreEqual(marker, loaded.Comment);

            Connection.Delete(loaded);
        }
    }
}
