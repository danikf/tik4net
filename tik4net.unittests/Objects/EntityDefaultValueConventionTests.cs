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
    /// The rule the three tests enforce is one rule seen from three sides: <b>whatever an untouched property
    /// holds must be something the router is content to receive — or must not be sent at all.</b> A
    /// non-nullable <c>bool</c> cannot satisfy that (A10 measured both ways round: declaring the router's
    /// default sends the opposite on every create, declaring the CLR default silently drops an explicitly
    /// assigned <c>false</c>), which is why a writable flag is <c>bool?</c>. An enum can satisfy it either
    /// way — by ordering its zero member to the router's default, or by going nullable — so the enum test
    /// accepts both.
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

        [TestMethod]
        public void AWritableBoolIsNullable()
        {
            // The convention B4 settled: a writable flag has three states on the router — yes, no, and "you
            // did not say" — and only bool? can hold all three. A plain bool cannot express the last one, so
            // whichever value it holds, one of the three is misread, and no DefaultValue can rescue it (A10
            // measured both ways round). Read-only properties are exempt: nothing is ever sent from them.
            var offenders = Properties()
                .Where(x => x.Property.PropertyType == typeof(bool))
                .Where(x => !x.Attribute.IsReadOnly && x.Property.SetMethod != null && x.Property.SetMethod.IsPublic)
                .Select(x => $"{x.Entity.Name}.{x.Property.Name} ('{x.Attribute.FieldName}')")
                .OrderBy(s => s)
                .ToList();

            Assert.AreEqual(0, offenders.Count,
                "a writable bool must be declared bool? so 'unset' is distinguishable from 'false':"
                + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        [TestMethod]
        public void AnEnumPropertyEitherDefaultsToItsZeroMemberOrIsNullable()
        {
            // An untouched non-nullable enum property IS its zero member, so if that member is not what the
            // router would have chosen, the mapper sends it on every /add and the row is created with the
            // wrong value — a certificate with md5/1024 rather than sha256/2048 (A12). There are two correct
            // shapes and this accepts either: make the router's default the zero member, or make the property
            // nullable so "untouched" is a state of its own and nothing is sent at all.
            //
            // Which shape to reach for is decided by the enum, not by taste. SyslogFacilityType's member ORDER
            // is the syslog facility numbering and KeySizeType's is ascending key sizes; renumbering those to
            // move a default to the front would trade one wrong for another, so those went nullable.
            // /ip/dhcp-client add-default-route is the opposite case — its enum's zero member genuinely IS the
            // router's default, so it needs nothing.
            var offenders = new List<string>();

            foreach (var x in Properties())
            {
                var valueType = Nullable.GetUnderlyingType(x.Property.PropertyType) ?? x.Property.PropertyType;
                if (!valueType.IsEnum || x.Attribute.DefaultValue == null)
                    continue;
                if (valueType != x.Property.PropertyType)
                    continue;   // nullable: untouched is null, so the zero member never reaches the router

                string expected = WireNameOfDefaultMember(valueType);
                if (expected != null && x.Attribute.DefaultValue != expected)
                    offenders.Add($"{x.Entity.Name}.{x.Property.Name} ('{x.Attribute.FieldName}') declares "
                                + $"DefaultValue = \"{x.Attribute.DefaultValue}\", but an untouched property "
                                + $"is the zero member, which serializes to \"{expected}\"");
            }

            Assert.AreEqual(0, offenders.Count,
                "Each of these is created with the wrong value on every /add. Either reorder the enum so the "
                + "router's default is its zero member, or declare the property nullable:"
                + Environment.NewLine + string.Join(Environment.NewLine, offenders));
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
