using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Ipsec;

namespace tik4net.tests
{
    [TestClass]
    public class IpsecProposalTest : TestBase
    {
        [TestMethod]
        public void ListIpsecProposalsWillNotFail()
        {
            EnsureCommandAvailable("/ip/ipsec/proposal");
            var list = Connection.LoadAll<IpsecProposal>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddIpsecProposalWillNotFail()
        {
            EnsureCommandAvailable("/ip/ipsec/proposal");
            // Use a short alphanumeric marker; MikroTik proposal names accept hyphens.
            string marker = "t4n" + Guid.NewGuid().ToString("N").Substring(0, 12);
            var proposal = new IpsecProposal
            {
                Name = marker,
                Comment = marker,
            };
            Connection.Save(proposal);

            var loaded = Connection.LoadById<IpsecProposal>(proposal.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Name);

            Connection.Delete(loaded);
        }
    }
}
