#nullable enable
// EntityRouterOrderedListTests.cs — the mapped lists the router keeps as a set and prints in its own order.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.User;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// Each row is a value written to a throwaway row and the value the router printed back (6.49.21 and 7.x over the API,
    /// 2026-11-04 and 2026-11-07). The router keeps these fields as a set, so a list merge that compared them in order would write
    /// them on every run and never settle; a list of an enum's members compares by members.
    /// </summary>
    [TestClass]
    public class EntityRouterOrderedListTests
    {
        [DataTestMethod]
        [DataRow("PptpClient", "/interface/pptp-client", "name", "allow", "Allow", "mschap2,pap,chap", "pap,chap,mschap2")]
        [DataRow("L2tpClient", "/interface/l2tp-client", "name", "allow", "Allow", "mschap2,pap,chap", "pap,chap,mschap2")]
        [DataRow("SstpClient", "/interface/sstp-client", "name", "authentication", "Authentication", "mschap2,pap,chap", "pap,chap,mschap2")]
        [DataRow("InterfacePppoeClient", "/interface/pppoe-client", "name", "allow", "Allow", "mschap2,pap,chap", "pap,chap,mschap2")]
        [DataRow("InterfacePppoeserverServer", "/interface/pppoe-server/server", "service-name", "authentication", "Authentication", "mschap2,pap,chap", "pap,chap,mschap2")]
        [DataRow("IpsecProfile", "/ip/ipsec/profile", "name", "dh-group", "DhGroup", "ecp256,modp1024,modp2048", "ecp256,modp2048,modp1024")]
        [DataRow("SystemScript", "/system/script", "name", "policy", "Policy", "write,read,test", "read,write,test")]
        [DataRow("SystemScheduler", "/system/scheduler", "name", "policy", "Policy", "write,read,test", "read,write,test")]
        [DataRow("UserGroup", "/user/group", "name", "policy", "Policy", "write,!ftp,read,test", "read,write,test,!ftp")]
        [DataRow("WirelessSecurityProfile", "/interface/wireless/security-profiles", "name", "authentication-types", "AuthenticationTypes", "wpa2-psk,wpa-psk", "wpa-psk,wpa2-psk")]
        [DataRow("CapsManSecurity", "/caps-man/security", "name", "authentication-types", "AuthenticationTypes", "wpa2-psk,wpa-psk", "wpa-psk,wpa2-psk")]
        [DataRow("CapsManConfiguration", "/caps-man/configuration", "name", "security.authentication-types", "SecurityAuthenticationTypes", "wpa2-psk,wpa-psk", "wpa-psk,wpa2-psk")]
        [DataRow("WifiSecurity", "/interface/wifi/security", "name", "authentication-types", "AuthenticationTypes", "wpa2-psk,wpa-psk", "wpa-psk,wpa2-psk")]
        [DataRow("WifiSecurity", "/interface/wifi/security", "name", "dh-groups", "DhGroups", "21,19", "19,21")]
        // 2026-11-07: the cipher, algorithm, method and rate sets.
        [DataRow("IpsecProposal", "/ip/ipsec/proposal", "name", "auth-algorithms", "AuthAlgorithms", "sha512,md5,sha256,sha1", "sha512,sha256,sha1,md5")]
        [DataRow("IpsecProposal", "/ip/ipsec/proposal", "name", "enc-algorithms", "EncAlgorithms", "aes-128-cbc,aes-256-cbc,3des", "aes-256-cbc,aes-128-cbc,3des")]
        [DataRow("IpsecProfile", "/ip/ipsec/profile", "name", "enc-algorithm", "EncAlgorithm", "3des,aes-256,aes-128", "aes-256,aes-128,3des")]
        [DataRow("HotspotServerProfile", "/ip/hotspot/profile", "name", "login-by", "LoginBy", "https,cookie,mac", "mac,cookie,https")]
        [DataRow("Radius", "/radius", "address", "service", "Service", "hotspot,ppp,login", "ppp,login,hotspot")]
        [DataRow("OspfInstance", "/routing/ospf/instance", "name", "redistribute", "Redistribute", "static,connected,bgp", "connected,static,bgp")]
        [DataRow("BgpPeer", "/routing/bgp/peer", "name", "address-families", "AddressFamilies", "ipv6,ip", "ip,ipv6")]
        [DataRow("OvpnServer", "/interface/ovpn-server/server", "name", "auth", "Auth", "sha512,md5,sha1", "sha1,md5,sha512")]
        [DataRow("OvpnServer", "/interface/ovpn-server/server", "name", "cipher", "Cipher", "aes256-cbc,aes128-cbc,blowfish128", "blowfish128,aes128-cbc,aes256-cbc")]
        [DataRow("SstpClient", "/interface/sstp-client", "name", "ciphers", "Ciphers", "aes256-gcm-sha384,aes256-sha", "aes256-sha,aes256-gcm-sha384")]
        [DataRow("WifiSecurity", "/interface/wifi/security", "name", "encryption", "Encryption", "gcmp,ccmp,tkip", "tkip,ccmp,gcmp")]
        [DataRow("WifiAccessList", "/interface/wifi/access-list", "comment", "days", "Days", "wed,mon,sun", "sun,mon,wed")]
        [DataRow("WirelessSecurityProfile", "/interface/wireless/security-profiles", "name", "unicast-ciphers", "UnicastCiphers", "aes-ccm,tkip", "tkip,aes-ccm")]
        [DataRow("WirelessSecurityProfile", "/interface/wireless/security-profiles", "name", "group-ciphers", "GroupCiphers", "aes-ccm,tkip", "tkip,aes-ccm")]
        [DataRow("CapsManSecurity", "/caps-man/security", "name", "encryption", "Encryption", "tkip,aes-ccm", "aes-ccm,tkip")]
        [DataRow("CapsManConfiguration", "/caps-man/configuration", "name", "security.encryption", "SecurityEncryption", "tkip,aes-ccm", "aes-ccm,tkip")]
        [DataRow("CapsManConfiguration", "/caps-man/configuration", "name", "rates.basic", "RatesBasic", "11Mbps,1Mbps,2Mbps", "1Mbps,2Mbps,11Mbps")]
        [DataRow("CapsManConfiguration", "/caps-man/configuration", "name", "rates.supported", "RatesSupported", "54Mbps,6Mbps,24Mbps", "6Mbps,24Mbps,54Mbps")]
        [DataRow("CapsManConfiguration", "/caps-man/configuration", "name", "rates.ht-basic-mcs", "RatesHtBasicMcs", "mcs-3,mcs-0,mcs-1", "mcs-0,mcs-1,mcs-3")]
        [DataRow("CapsManConfiguration", "/caps-man/configuration", "name", "rates.ht-supported-mcs", "RatesHtSupportedMcs", "mcs-23,mcs-8", "mcs-8,mcs-23")]
        [DataRow("Certificate", "/certificate", "name", "key-usage", "KeyUsage", "tls-client,digital-signature,key-cert-sign", "digital-signature,key-cert-sign,tls-client")]
        [DataRow("Certificate", "/certificate", "name", "trust-store", "TrustStore", "www,api,ipsec", "ipsec,www,api")]
        // 2026-11-07: the sorted lists whose members are numbers and addresses (RouterSortsMembers).
        [DataRow("WifiConfiguration", "/interface/wifi/configuration", "name", "chains", "Chains", "2,0,1", "0,1,2")]
        [DataRow("WifiConfiguration", "/interface/wifi/configuration", "name", "tx-chains", "TxChains", "2,0,1", "0,1,2")]
        [DataRow("CapsManConfiguration", "/caps-man/configuration", "name", "rx-chains", "RxChains", "2,0,1", "0,1,2")]
        [DataRow("CapsManConfiguration", "/caps-man/configuration", "name", "tx-chains", "TxChains", "2,0,1", "0,1,2")]
        [DataRow("InterfaceWireless", "/interface/wireless", "name", "ht-rxchains", "HtRxchains", "2,0,1", "0,1,2")]
        [DataRow("InterfaceWireless", "/interface/wireless", "name", "ht-txchains", "HtTxchains", "2,0,1", "0,1,2")]
        [DataRow("DhcpServerAlert", "/ip/dhcp-server/alert", "interface", "valid-server", "ValidServer", "AA:BB:CC:DD:EE:02,AA:BB:CC:DD:EE:01", "AA:BB:CC:DD:EE:01,AA:BB:CC:DD:EE:02")]
        // The router drops a duplicate member, so the value written with one is no change either.
        [DataRow("WifiConfiguration", "/interface/wifi/configuration", "name", "chains", "Chains", "1,0,1", "0,1")]
        [DataRow("DhcpServerAlert", "/ip/dhcp-server/alert", "interface", "valid-server", "ValidServer", "AA:BB:CC:DD:EE:02,AA:BB:CC:DD:EE:02", "AA:BB:CC:DD:EE:02")]
        public void AMerge_OfTheValueInTheRoutersOrder_SendsNothing(string entity, string path, string keyField, string field, string property,
            string sent, string printed)
        {
            var type = typeof(TikEntityAttribute).Assembly.GetTypes().Single(t => t.Name == entity && t.GetCustomAttribute<TikEntityAttribute>() != null);
            var sentCommands = (IReadOnlyList<string[]>)typeof(EntityRouterOrderedListTests)
                .GetMethod(nameof(MergeSends), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(type)
                .Invoke(null, new object[] { path, keyField, field, type.GetProperty(property)!, sent, printed })!;

            Assert.AreEqual(0, sentCommands.Count, "the merge wrote: " + string.Join(" | ", sentCommands.Select(c => string.Join(" ", c))));
        }

        [DataTestMethod]
        [DataRow("WifiConfiguration", "/interface/wifi/configuration", "name", "chains", "Chains", "0,1", "0,1,2")]
        [DataRow("InterfaceWireless", "/interface/wireless", "name", "ht-txchains", "HtTxchains", "0,2", "0,1")]
        [DataRow("DhcpServerAlert", "/ip/dhcp-server/alert", "interface", "valid-server", "ValidServer", "AA:BB:CC:DD:EE:03,AA:BB:CC:DD:EE:01", "AA:BB:CC:DD:EE:01,AA:BB:CC:DD:EE:02")]
        // A list the router keeps in the order it is given compares in that order: the first DNS server is the primary.
        [DataRow("DhcpServerNetwork", "/ip/dhcp-server/network", "address", "dns-server", "DnsServer", "8.8.8.8,1.1.1.1", "1.1.1.1,8.8.8.8")]
        public void AMerge_OfOtherMembersOrOfAnOrderedListInAnotherOrder_SendsTheValue(string entity, string path, string keyField, string field,
            string property, string sent, string printed)
        {
            var type = typeof(TikEntityAttribute).Assembly.GetTypes().Single(t => t.Name == entity && t.GetCustomAttribute<TikEntityAttribute>() != null);
            var sentCommands = (IReadOnlyList<string[]>)typeof(EntityRouterOrderedListTests)
                .GetMethod(nameof(MergeSends), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(type)
                .Invoke(null, new object[] { path, keyField, field, type.GetProperty(property)!, sent, printed })!;

            var set = sentCommands.Single(c => c.First() == path + "/set");
            CollectionAssert.Contains(set, "=" + field + "=" + sent);
        }

        [TestMethod]
        public void AGroupPolicy_ReadsEveryPolicy_GrantedOrDenied()
        {
            var connection = Router("/user/group", "name", "read", "policy", "read,test,!ftp,!write,dude");
            var policy = connection.LoadAll<UserGroup>().Single().Policy.Value!;

            Assert.AreEqual(new TikValueList<TikNegatableValue<UserPolicy>>(UserPolicy.Dude, UserPolicy.Read, UserPolicy.Test,
                TikNegatableValue<UserPolicy>.Not(UserPolicy.Ftp), TikNegatableValue<UserPolicy>.Not(UserPolicy.Write)), policy);
        }

        [TestMethod]
        public void AGroupPolicy_DeniesAPolicy_ByNegatingItsMember()
        {
            // The remarks on UserGroup.Policy: a set names only what it changes, so a denied policy is sent negated.
            var connection = Router("/user/group", "name", "g", "policy", "read,write,test,!ftp");
            var group = connection.LoadAll<UserGroup>().Single();

            group.Policy = group.Policy.Value!.Without(UserPolicy.Write).With(TikNegatableValue<UserPolicy>.Not(UserPolicy.Write));
            connection.Save(group);

            var set = connection.SentCommands.Single(c => c.First() == "/user/group/set");
            CollectionAssert.Contains(set, "=policy=read,test,!ftp,!write");
        }

        [TestMethod]
        public void AWordNoEnumHas_IsKept_AndTheRestOfTheListKeepsItsType()
        {
            // A policy a later RouterOS adds must not fail the whole read of /user/group.
            var connection = Router("/user/group", "name", "full", "policy", "read,!future-policy");
            var policy = connection.LoadAll<UserGroup>().Single().Policy.Value!;

            Assert.AreEqual(2, policy.Count);
            Assert.AreEqual("read,!future-policy", string.Join(",", policy.Select(p => p.ToString())));
        }

        private static IReadOnlyList<string[]> MergeSends<T>(string path, string keyField, string field, PropertyInfo property, string sent, string printed)
            where T : new()
        {
            // The expected row is loaded as well, so the value is the one a caller would build from the same text.
            var expected = Router(path, keyField, "x", field, sent).LoadAll<T>().ToList();
            var connection = Router(path, keyField, "x", field, printed);
            var original = connection.LoadAll<T>().ToList();

            var keyProperty = typeof(T).GetProperties().Single(p => p.GetCustomAttribute<TikPropertyAttribute>()?.FieldName == keyField);
            var merge = connection.CreateMerge(expected, original).WithKey(e => keyProperty.GetValue(e)!.ToString()!);

            var row = Expression.Parameter(typeof(T), "row");
            var lambda = Expression.Lambda(typeof(Func<,>).MakeGenericType(typeof(T), property.PropertyType), Expression.Property(row, property), row);
            typeof(TikListMerge<T>).GetMethods()
                .Single(m => m.Name == nameof(TikListMerge<T>.Field) && m.GetParameters().Length == 1)
                .MakeGenericMethod(property.PropertyType)
                .Invoke(merge, new object[] { lambda });
            merge.Save();

            return connection.SentCommands.Where(c => c.First() != path + "/print").ToList();
        }

        private static TikFakeConnection Router(string path, string keyField, string key, string field, string value)
        {
            var row = new Dictionary<string, string> { [".id"] = "*1", [keyField] = key, [field] = value };
            return new TikFakeConnection()
                .WithResponse(cmd => cmd.FirstOrDefault() == path + "/print",
                    _ => new ITikSentence[] { new TikFakeReSentence(row), new TikFakeDoneSentence() })
                .WithNonQuery(cmd => cmd.First() == path + "/set" || cmd.First() == path + "/unset");
        }
    }
}
