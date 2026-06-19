using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Routing;

namespace tik4net.tests
{
    [TestClass]
    public class RoutingRuleTest : TestBase
    {
        [TestMethod]
        public void ListRoutingRulesWillNotFail()
        {
            EnsureCommandAvailable("/routing/rule");
            var list = Connection.LoadAll<RoutingRule>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddRoutingRuleWillNotFail()
        {
            EnsureCommandAvailable("/routing/rule");
            string marker = Guid.NewGuid().ToString();
            // Use RFC 5737 documentation prefix for dst-address; action=lookup with table=main
            // are safe defaults that always exist on a ROS7 router.
            var rule = new RoutingRule
            {
                Action = RoutingRule.ActionType.Lookup,
                DstAddress = "192.0.2.0/24",
                Table = "main",
                Comment = marker,
            };
            Connection.Save(rule);

            var loaded = Connection.LoadById<RoutingRule>(rule.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Comment);
            Assert.AreEqual("192.0.2.0/24", loaded.DstAddress);
            Assert.AreEqual(RoutingRule.ActionType.Lookup, loaded.Action);

            Connection.Delete(loaded);
        }
    }
}
