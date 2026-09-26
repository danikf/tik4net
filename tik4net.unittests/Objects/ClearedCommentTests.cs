// ClearedCommentTests.cs — clearing a loaded comment sends comment="" on the set, not an unset. RouterOS has no
// unset verb on /interface (and other menus): restoring an absent comment failed with "no such command (unset)".
// An empty comment is no comment on every transport (TikEmptyComment), so comment="" is the clearing that works.

#nullable enable

using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    [TestClass]
    public class ClearedCommentTests
    {
        [TestMethod]
        public void ClearingALoadedComment_SetsItEmpty_WithoutAnUnset()
        {
            var connection = new TikFakeConnection()
                .WithResponse(cmd => cmd.First() == "/interface/print", _ => new ITikSentence[]
                {
                    new TikFakeReSentence(new Dictionary<string, string> { [".id"] = "*1", ["name"] = "ether1", ["comment"] = "uplink" }),
                    new TikFakeDoneSentence(),
                })
                .WithNonQuery(cmd => cmd.First() == "/interface/set");
            var eth = connection.LoadAll<Interface>().Single();

            eth.Comment = null;
            connection.Save(eth);

            CollectionAssert.Contains(connection.SentCommands.Single(c => c.First() == "/interface/set").ToArray(), "=comment=");
            Assert.IsFalse(connection.SentCommands.Any(c => c.First() == "/interface/unset"));
        }

        [TestMethod]
        public void ClearingAnotherField_StillUnsetsIt()
        {
            var connection = new TikFakeConnection()
                .WithResponse(cmd => cmd.First() == "/interface/print", _ => new ITikSentence[]
                {
                    new TikFakeReSentence(new Dictionary<string, string> { [".id"] = "*1", ["name"] = "ether1", ["mtu"] = "1400" }),
                    new TikFakeDoneSentence(),
                })
                .WithNonQuery(cmd => cmd.First() == "/interface/set" || cmd.First() == "/interface/unset");
            var eth = connection.LoadAll<Interface>().Single();

            eth.Mtu = null;
            connection.Save(eth);

            Assert.IsTrue(connection.SentCommands.Any(c => c.First() == "/interface/unset" && c.Contains("=value-name=mtu")));
        }
    }
}
