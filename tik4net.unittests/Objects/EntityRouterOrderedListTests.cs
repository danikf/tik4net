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
    /// 2026-11-04). The router keeps these fields as a set, so a list merge that compared them in order would write
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
