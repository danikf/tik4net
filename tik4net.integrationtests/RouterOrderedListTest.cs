using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Certificate;
using tik4net.Objects.Interface.Wireless;
using tik4net.Objects.Ip.Hotspot;
using tik4net.Objects.Ip.Ipsec;
using tik4net.Objects.Radius;
using tik4net.Objects.System;
using tik4net.Objects.User;

namespace tik4net.integrationtests
{
    /// <summary>
    /// Lists the router keeps as a set and prints in its own order: a value written in another order reads back equal,
    /// and a merge of it sends nothing. The order a value is written in is never the router's here, so each test sees
    /// the router reorder it.
    /// </summary>
    [TestClass]
    [SafeInParallelLegs]
    [TestCategory(TestCategories.AnyRouter)]
    public class RouterOrderedListTest : TestBase
    {
        [TestMethod]
        public void AScriptPolicy_InAnotherOrder_SettlesInAMerge()
        {
            EnsureCommandAvailable("/system/script");
            var policy = new TikValueList<SystemScriptPolicy>(SystemScriptPolicy.Write, SystemScriptPolicy.Read, SystemScriptPolicy.Test);
            string name = TestNames.Unique("t4n-ordered");
            var script = SaveTracked(new SystemScript { Name = name, Source = "", Policy = policy });

            var loaded = Connection.LoadById<SystemScript>(script.Id);
            Assert.AreEqual(policy, loaded.Policy.Value, "read back: " + loaded.Policy);

            Assert.AreEqual(0, Updates(new SystemScript { Name = name, Policy = policy }, loaded, s => s.Policy, s => s.Name.ToString()));
        }

        [TestMethod]
        public void AnIpsecDhGroup_InAnotherOrder_SettlesInAMerge()
        {
            EnsureCommandAvailable("/ip/ipsec/profile");
            var groups = new TikValueList<IpsecDhGroup>(IpsecDhGroup.Ecp256, IpsecDhGroup.Modp1024, IpsecDhGroup.Modp2048);
            string name = TestNames.Unique("t4n-ordered");
            var profile = SaveTracked(new IpsecProfile { Name = name, DhGroup = groups });

            var loaded = Connection.LoadById<IpsecProfile>(profile.Id);
            Assert.AreEqual(groups, loaded.DhGroup.Value, "read back: " + loaded.DhGroup);

            Assert.AreEqual(0, Updates(new IpsecProfile { Name = name, DhGroup = groups }, loaded, p => p.DhGroup, p => p.Name.ToString()));
        }

        /// <summary>
        /// A group's policy reads back every policy, granted or denied, and a text <c>set</c> changes only the ones it
        /// names (the remarks on <see cref="UserGroup.Policy"/>): denying one by negating its member leaves the others.
        /// </summary>
        [TestMethod]
        public void AGroupPolicy_ReadsEveryPolicy_AndDeniesOneByItsNegatedMember()
        {
            EnsureCommandAvailable("/user/group");
            string name = TestNames.Unique("t4n-ordered");
            var group = SaveTracked(new UserGroup
            {
                Name = name,
                Policy = new TikValueList<TikNegatableValue<UserPolicy>>(UserPolicy.Write, UserPolicy.Read, UserPolicy.Test),
            });

            var loaded = Connection.LoadById<UserGroup>(group.Id);
            var granted = loaded.Policy.Value.Select(p => p.TryGetValue(out var member) ? member : default(TikNegatableValue<UserPolicy>?))
                .Where(m => m.HasValue && !m.Value.IsNegated).Select(m => m.Value.Value).OrderBy(m => m).ToArray();
            CollectionAssert.AreEqual(new[] { UserPolicy.Read, UserPolicy.Write, UserPolicy.Test }, granted, "read back: " + loaded.Policy);
            Assert.IsTrue(loaded.Policy.Value.Contains(TikNegatableValue<UserPolicy>.Not(UserPolicy.Ftp)),
                "a policy the group lacks reads negated: " + loaded.Policy);

            loaded.Policy = loaded.Policy.Value.Without(UserPolicy.Write).With(TikNegatableValue<UserPolicy>.Not(UserPolicy.Write));
            Connection.Save(loaded);

            var denied = Connection.LoadById<UserGroup>(group.Id);
            Assert.AreEqual(loaded.Policy.Value, denied.Policy.Value);
            Assert.AreEqual(0, Updates(new UserGroup { Name = name, Policy = new TikValueList<TikNegatableValue<UserPolicy>>(denied.Policy.Value.Reverse()) },
                denied, g => g.Policy, g => g.Name.ToString()));
        }

        [TestMethod]
        public void IpsecProposalAlgorithms_InAnotherOrder_SettleInAMerge()
        {
            EnsureCommandAvailable("/ip/ipsec/proposal");
            var auth = new TikValueList<IpsecAuthAlgorithm>(IpsecAuthAlgorithm.Sha256, IpsecAuthAlgorithm.Sha1, IpsecAuthAlgorithm.Sha512);
            var enc = new TikValueList<IpsecProposalEncAlgorithm>(IpsecProposalEncAlgorithm.Aes128Cbc, IpsecProposalEncAlgorithm.Aes256Cbc);
            string name = TestNames.Unique("t4n-ordered");
            var proposal = SaveTracked(new IpsecProposal { Name = name, AuthAlgorithms = auth, EncAlgorithms = enc });

            var loaded = Connection.LoadById<IpsecProposal>(proposal.Id);
            Assert.AreEqual(auth, loaded.AuthAlgorithms.Value, "read back: " + loaded.AuthAlgorithms);
            Assert.AreEqual(enc, loaded.EncAlgorithms.Value, "read back: " + loaded.EncAlgorithms);

            Assert.AreEqual(0, Updates(new IpsecProposal { Name = name, AuthAlgorithms = auth }, loaded, p => p.AuthAlgorithms, p => p.Name.ToString()));
            Assert.AreEqual(0, Updates(new IpsecProposal { Name = name, EncAlgorithms = enc }, loaded, p => p.EncAlgorithms, p => p.Name.ToString()));
        }

        [TestMethod]
        public void ARadiusService_InAnotherOrder_SettlesInAMerge()
        {
            EnsureCommandAvailable("/radius");
            var service = new TikValueList<RadiusService>(RadiusService.Hotspot, RadiusService.Ppp, RadiusService.Login);
            string comment = TestNames.Unique("t4n-ordered");
            var radius = SaveTracked(new Radius { Address = "192.0.2.9", Comment = comment, Service = service });

            var loaded = Connection.LoadById<Radius>(radius.Id);
            Assert.AreEqual(service, loaded.Service.Value, "read back: " + loaded.Service);

            Assert.AreEqual(0, Updates(new Radius { Comment = comment, Service = service }, loaded, r => r.Service, r => r.Comment.ToString()));
        }

        [TestMethod]
        public void AHotspotLoginBy_InAnotherOrder_SettlesInAMerge()
        {
            EnsureCommandAvailable("/ip/hotspot/profile");
            var loginBy = new TikValueList<HotspotLoginMethod>(HotspotLoginMethod.Https, HotspotLoginMethod.Cookie, HotspotLoginMethod.Mac);
            string name = TestNames.Unique("t4n-ordered");
            var profile = SaveTracked(new HotspotServerProfile { Name = name, LoginBy = loginBy });

            var loaded = Connection.LoadById<HotspotServerProfile>(profile.Id);
            Assert.AreEqual(loginBy, loaded.LoginBy.Value, "read back: " + loaded.LoginBy);

            Assert.AreEqual(0, Updates(new HotspotServerProfile { Name = name, LoginBy = loginBy }, loaded, p => p.LoginBy, p => p.Name.ToString()));
        }

        [TestMethod]
        public void WirelessCiphers_InAnotherOrder_SettleInAMerge()
        {
            EnsureCommandAvailable("/interface/wireless/security-profiles");
            var ciphers = new TikValueList<WirelessCipher>(WirelessCipher.AesCcm, WirelessCipher.Tkip);
            string name = TestNames.Unique("t4n-ordered");
            var profile = SaveTracked(new WirelessSecurityProfile { Name = name, UnicastCiphers = ciphers });

            var loaded = Connection.LoadById<WirelessSecurityProfile>(profile.Id);
            Assert.AreEqual(ciphers, loaded.UnicastCiphers.Value, "read back: " + loaded.UnicastCiphers);

            Assert.AreEqual(0, Updates(new WirelessSecurityProfile { Name = name, UnicastCiphers = ciphers }, loaded, p => p.UnicastCiphers, p => p.Name.ToString()));
        }

        [TestMethod]
        public void ACertificateKeyUsage_InAnotherOrder_SettlesInAMerge()
        {
            EnsureCommandAvailable("/certificate");
            var usage = new TikValueList<CertificateKeyUsage>(CertificateKeyUsage.TlsClient, CertificateKeyUsage.DigitalSignature, CertificateKeyUsage.KeyCertSign);
            string name = TestNames.Unique("t4n-ordered");
            var certificate = SaveTracked(new Certificate { Name = name, CommonName = name, KeyUsage = usage });

            var loaded = Connection.LoadById<Certificate>(certificate.Id);
            Assert.AreEqual(usage, loaded.KeyUsage.Value, "read back: " + loaded.KeyUsage);

            Assert.AreEqual(0, Updates(new Certificate { Name = name, KeyUsage = usage }, loaded, c => c.KeyUsage, c => c.Name.ToString()));
        }

        private int Updates<T, TProperty>(T expected, T onRouter, System.Linq.Expressions.Expression<Func<T, TProperty>> field, Func<T, string> key)
            where T : new()
        {
            Connection.CreateMerge(new[] { expected }, new[] { onRouter }).WithKey(key).Field(field)
                .Simulate(out _, out int updates, out _);
            return updates;
        }
    }
}
