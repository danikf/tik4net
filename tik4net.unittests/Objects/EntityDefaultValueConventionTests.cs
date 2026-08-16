using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// Reflection checks over every <c>[TikEntity]</c> for the mistakes <c>[TikProperty(DefaultValue = …)]</c>
    /// invites — the value has to be written the way the <b>router</b> spells it, and it has to agree with
    /// what the property holds when nobody touched it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mapper decides whether a field is worth sending on <c>/add</c> by serializing the property and
    /// comparing the result against <c>DefaultValue</c> — equal means "untouched, leave it out". Get the
    /// spelling wrong and the comparison can never succeed, so the field goes out on every create and set.
    /// </para>
    /// <para>
    /// <b>The bigger half of this rule is waiting on B4</b>, deliberately. A non-nullable <c>bool</c> is
    /// <c>false</c> when untouched, so strictly it would have to declare <c>"no"</c> — but sweeping the 56
    /// properties that declare the router's default instead was tried (A10) and reverted: it makes an
    /// explicitly assigned <c>false</c> indistinguishable from untouched, so the field is dropped and the
    /// router applies <c>yes</c>. <c>FirewallMangleMergeTest</c> caught it as rules that would not survive
    /// their own round trip. Once <c>bool?</c> can carry "unset" separately from <c>false</c>, the router's
    /// default is exactly what belongs here and this test tightens to say so.
    /// </para>
    /// </remarks>
    [TestClass]
    public class EntityDefaultValueConventionTests
    {
        private static IEnumerable<Type> EntityTypes()
            => typeof(tik4net.Objects.Ip.IpAddress).Assembly
                .GetTypes()
                .Where(t => t.GetCustomAttribute<TikEntityAttribute>() != null);

        private static IEnumerable<(Type Entity, PropertyInfo Property, TikPropertyAttribute Attribute)> Properties()
            => EntityTypes()
                .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(p => (Entity: t, Property: p, Attribute: p.GetCustomAttribute<TikPropertyAttribute>()))
                    .Where(x => x.Attribute != null));

        [TestMethod]
        public void ABoolPropertyDeclaresItsDefaultInTheRoutersWireForm()
        {
            // The mapper serializes a bool to "yes"/"no" and compares THAT against DefaultValue. A C# spelling
            // therefore matches nothing: the field is force-sent on every add and set, which also gives the
            // native WinBox transport an M2 key to resolve for a field nobody asked to write.
            var offenders = Properties()
                .Where(x => x.Property.PropertyType == typeof(bool) || x.Property.PropertyType == typeof(bool?))
                .Where(x => x.Attribute.DefaultValue != null
                         && x.Attribute.DefaultValue != "no" && x.Attribute.DefaultValue != "yes")
                .Select(x => $"{x.Entity.Name}.{x.Property.Name} ('{x.Attribute.FieldName}') "
                           + $"declares DefaultValue = \"{x.Attribute.DefaultValue}\"")
                .OrderBy(s => s)
                .ToList();

            Assert.AreEqual(0, offenders.Count,
                "A bool serializes to \"yes\"/\"no\", so DefaultValue has to be one of those — never the C# "
                + "spelling." + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        /// <summary>
        /// The 13 enum properties that carry the same defect the bool sweep fixed, each created with its
        /// zero member's value instead of the router's default. Listed rather than fixed because the fix is
        /// not the same one: for an enum, the alternative to changing the attribute is to <b>reorder the
        /// enum</b> so the router's default becomes the zero member — which additionally makes an untouched
        /// entity read as what the router will do, something a non-nullable <c>bool</c> can never manage.
        /// Two of these look like the reordering case on sight (a certificate created with <c>md5</c>/1024
        /// rather than <c>sha256</c>/2048), so the choice is per property and belongs to its own item, not
        /// to a sweep. Named here so a <b>new</b> one still fails this test.
        /// </summary>
        private static readonly HashSet<string> KnownEnumDefaultGaps = new HashSet<string>
        {
            "Certificate.DigestAlgorithm",          // md5 vs. the router's sha256 — reorder, do not relabel
            "Certificate.KeySize",                  // 1024 vs. 2048 — same
            "InterfaceBridge.Arp",
            "InterfaceBridge.ProtocolMode",
            "InterfacePppoeClient.AddDefaultRoute", // declares "false"/"true"; RouterOS spells these yes/no
            "InterfacePppoeClient.DialOnDemand",
            "InterfacePppoeClient.UsePeerDns",
            "InterfaceVlan.Arp",
            "IpCloud.DdnsEnabled",
            "IpsecPolicy.Action",
            "IpsecProposal.PfsGroup",
            "Radius.RequireMessageAuth",
            "SystemLoggingAction.SyslogFacility",
        };

        [TestMethod]
        public void AnEnumPropertyDeclaresTheWireFormOfItsZeroMemberAsItsDefault()
        {
            // Same rule, and the reason /ip/dhcp-client add-default-route legitimately says "yes": its enum's
            // default member IS Yes. Checking it here keeps that from looking like an oversight later.
            var offenders = new List<string>();
            var fixedSinceListed = new List<string>();

            foreach (var x in Properties().Where(p => p.Property.PropertyType.IsEnum))
            {
                if (x.Attribute.DefaultValue == null)
                    continue;

                string expected = WireNameOfDefaultMember(x.Property.PropertyType);
                if (expected == null)
                    continue;

                string key = $"{x.Entity.Name}.{x.Property.Name}";
                bool disagrees = x.Attribute.DefaultValue != expected;

                if (disagrees && !KnownEnumDefaultGaps.Contains(key))
                    offenders.Add($"{key} ('{x.Attribute.FieldName}') declares DefaultValue = "
                                + $"\"{x.Attribute.DefaultValue}\", but the enum's default member serializes "
                                + $"to \"{expected}\"");
                else if (!disagrees && KnownEnumDefaultGaps.Contains(key))
                    fixedSinceListed.Add(key);
            }

            Assert.AreEqual(0, offenders.Count,
                "An enum property is its zero member when untouched, so that member's wire value is what "
                + "DefaultValue has to be." + Environment.NewLine + string.Join(Environment.NewLine, offenders));

            // A tally that only ever grows stops meaning anything. If one of the 13 was fixed, it leaves the
            // list in the same change.
            Assert.AreEqual(0, fixedSinceListed.Count,
                "these now satisfy the rule and must be removed from KnownEnumDefaultGaps: "
                + string.Join(", ", fixedSinceListed));
        }

        // The wire value of the member an untouched property holds — the one whose numeric value is 0.
        // Returns null when the enum has no zero member (nothing to compare against).
        private static string WireNameOfDefaultMember(Type enumType)
        {
            object zero = Enum.ToObject(enumType, 0);
            if (!Enum.IsDefined(enumType, zero))
                return null;

            string memberName = Enum.GetName(enumType, zero);
            var member = enumType.GetField(memberName);
            var tikEnum = member.GetCustomAttribute<TikEnumAttribute>();
            return tikEnum != null ? tikEnum.Value : memberName.ToLowerInvariant();
        }
    }
}
