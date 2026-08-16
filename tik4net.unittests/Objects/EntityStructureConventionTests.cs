using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// C1: reflection checks over every <c>[TikEntity]</c> for the STRUCTURAL conventions — the ones about
    /// how an entity is put together, as opposed to
    /// <see cref="EntityDefaultValueConventionTests"/>, which is about the values it declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These run in CI on every push, which is the point: an entity is written once and read by the mapper
    /// forever after, and most of what follows would otherwise surface as a runtime exception on the first
    /// load of a menu nobody happens to have a test for. There are ~150 entities and a few thousand
    /// properties; drift is not a hypothetical.
    /// </para>
    /// <para>
    /// Every rule enforced here is one the code already depends on. Nothing is invented for tidiness — a
    /// convention with no consequence is a convention nobody should be forced to follow.
    /// </para>
    /// </remarks>
    [TestClass]
    public class EntityStructureConventionTests
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

        private static void AssertNoOffenders(List<string> offenders, string what)
        {
            Assert.AreEqual(0, offenders.Count,
                offenders.Count + " " + what + ":" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        // ── The entity itself ───────────────────────────────────────────────────

        [TestMethod]
        public void EveryEntityBuildsItsMetadata()
        {
            // The broadest check there is, and the cheapest: TikEntityMetadata's ctor is what rejects a
            // property with no [TikProperty], two properties claiming one router field (it keys a dictionary
            // by FieldName), and a property type nothing can convert. Without this the first person to load
            // the menu finds out.
            var offenders = new List<string>();

            foreach (Type entity in EntityTypes())
            {
                try { new TikEntityMetadata(entity); }
                catch (Exception ex) { offenders.Add(entity.Name + ": " + ex.GetType().Name + " — " + ex.Message); }
            }

            AssertNoOffenders(offenders, "entities whose metadata cannot be built");
        }

        [TestMethod]
        public void AnEntityPathIsAbsoluteAndLowercase()
        {
            // The path is concatenated with "/print", "/add", … and sent verbatim. RouterOS menus are
            // lowercase, and a trailing slash produces "//print".
            var offenders = new List<string>();

            // Read through the metadata, which is what the mapper actually sends: it supplies the leading
            // slash for the 42 entities that declare the path without one. What is left to check is the
            // shapes nothing repairs.
            foreach (Type entity in EntityTypes())
            {
                string path = new TikEntityMetadata(entity).EntityPath;

                if (string.IsNullOrEmpty(path) || !path.StartsWith("/", StringComparison.Ordinal))
                    offenders.Add(entity.Name + ": path '" + path + "' does not start with '/'");
                else if (path.EndsWith("/", StringComparison.Ordinal))
                    offenders.Add(entity.Name + ": path '" + path + "' has a trailing slash");
                else if (path != path.ToLowerInvariant())
                    offenders.Add(entity.Name + ": path '" + path + "' is not lowercase");
            }

            AssertNoOffenders(offenders, "entities with a malformed path");
        }

        // ── .id ─────────────────────────────────────────────────────────────────

        [TestMethod]
        public void AnIdPropertyIsAReadOnlyMandatoryString()
        {
            // ARCHITECTURE.md: `Id` is always [TikProperty(".id", IsReadOnly = true, IsMandatory = true)].
            // Each half has teeth. Not read-only and the mapper sends .id as a settable field on /set;
            // not mandatory and a row that omits it silently produces an entity with an empty id, which then
            // fails at Save/Delete far from the cause; not a string and the value cannot round-trip ("*1A"
            // is hex).
            //
            // IsMandatory is required only on a LIST entity, and that exception is the router's, not a
            // convenience: a SINGLETON menu returns one record with no .id word at all, so demanding the
            // field would make every load of it throw. WirelessSniffer is the case — it declares a .id
            // property it can never be sent (harmless at IsMandatory = false, where it reads as "").
            var offenders = new List<string>();

            foreach (var x in Properties().Where(x => x.Attribute.FieldName == TikSpecialProperties.Id))
            {
                if (x.Property.PropertyType != typeof(string))
                    offenders.Add($"{x.Entity.Name}.{x.Property.Name} is {x.Property.PropertyType.Name}, not string");
                if (!x.Attribute.IsReadOnly)
                    offenders.Add($"{x.Entity.Name}.{x.Property.Name} is not IsReadOnly");
                if (!x.Attribute.IsMandatory && !x.Entity.GetCustomAttribute<TikEntityAttribute>().IsSingleton)
                    offenders.Add($"{x.Entity.Name}.{x.Property.Name} is not IsMandatory");
            }

            AssertNoOffenders(offenders, "malformed .id properties");
        }

        [TestMethod]
        public void AWritableListEntityHasAnIdToWriteAgainst()
        {
            // Save (of an existing row), Delete and Move all call EnsureHasIdProperty and throw without one.
            // A singleton has no .id and does not need one — its /set addresses the menu itself.
            var offenders = new List<string>();

            foreach (Type entity in EntityTypes())
            {
                var attribute = entity.GetCustomAttribute<TikEntityAttribute>();
                if (attribute.IsReadOnly || attribute.IsSingleton)
                    continue;

                bool hasId = entity.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Any(p => p.GetCustomAttribute<TikPropertyAttribute>()?.FieldName == TikSpecialProperties.Id);

                if (!hasId)
                    offenders.Add(entity.Name + " (" + attribute.EntityPath + ") is writable but has no .id property");
            }

            AssertNoOffenders(offenders, "writable entities without an .id");
        }

        [TestMethod]
        public void AnOrderedEntityIsAListWithIds()
        {
            // Move sends =numbers=<id> =destination=<id>, so ordering without ids is not expressible.
            var offenders = new List<string>();

            foreach (Type entity in EntityTypes())
            {
                var attribute = entity.GetCustomAttribute<TikEntityAttribute>();
                if (!attribute.IsOrdered)
                    continue;

                if (attribute.IsSingleton)
                    offenders.Add(entity.Name + " is both IsOrdered and IsSingleton");

                bool hasId = entity.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Any(p => p.GetCustomAttribute<TikPropertyAttribute>()?.FieldName == TikSpecialProperties.Id);

                if (!hasId)
                    offenders.Add(entity.Name + " is IsOrdered but has no .id, so it cannot be moved");
            }

            AssertNoOffenders(offenders, "malformed ordered entities");
        }

        // ── Enums ───────────────────────────────────────────────────────────────

        [TestMethod]
        public void EveryEnumMemberCarriesItsWireValue()
        {
            // Not a style rule: TikEnumMetadata builds its tables from [TikEnum], so a member without one
            // can be neither written (nothing to send) nor read (nothing to match) — it throws on the first
            // value that lands on it, which may be years after the member was added.
            var offenders = new List<string>();

            foreach (Type enumType in EntityEnumTypes())
            {
                foreach (string name in Enum.GetNames(enumType))
                {
                    if (enumType.GetRuntimeField(name).GetCustomAttribute<TikEnumAttribute>(false) == null)
                        offenders.Add(enumType.Name + "." + name + " has no [TikEnum]");
                }
            }

            AssertNoOffenders(offenders, "enum members with no wire value");
        }

        [TestMethod]
        public void NoTwoEnumMembersClaimTheSameWireValue()
        {
            // Two members with one wire value make the value unparseable — the mapper cannot know which was
            // meant, and rejects it rather than guessing (see TikEnumMetadata). A duplicate is normally a
            // copy-paste in a long enum, so it is worth catching at PR time rather than on a router.
            var offenders = new List<string>();

            foreach (Type enumType in EntityEnumTypes())
            {
                var duplicates = Enum.GetNames(enumType)
                    .Select(n => enumType.GetRuntimeField(n).GetCustomAttribute<TikEnumAttribute>(false)?.Value)
                    .Where(v => v != null)
                    .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1);

                foreach (var duplicate in duplicates)
                    offenders.Add(enumType.Name + " declares '" + duplicate.Key + "' " + duplicate.Count() + " times");
            }

            AssertNoOffenders(offenders, "enums with a duplicated wire value");
        }

        [TestMethod]
        public void AFlagsEnumIsMadeOfSingleBits()
        {
            // The mapper joins a [Flags] value by testing each member's bits and splits an incoming list by
            // OR-ing them, so a member holding two bits would be emitted alongside the members it contains
            // (established,related + a combined 'both') and would never round-trip.
            var offenders = new List<string>();

            foreach (Type enumType in EntityEnumTypes().Where(t => t.GetCustomAttribute<FlagsAttribute>() != null))
            {
                foreach (string name in Enum.GetNames(enumType))
                {
                    long value = Convert.ToInt64(Enum.Parse(enumType, name));
                    if (value != 0 && (value & (value - 1)) != 0)
                        offenders.Add(enumType.Name + "." + name + " = " + value + " is not a single bit");
                }
            }

            AssertNoOffenders(offenders, "[Flags] members holding more than one bit");
        }

        private static IEnumerable<Type> EntityEnumTypes()
            => Properties()
                .Select(x => Nullable.GetUnderlyingType(x.Property.PropertyType) ?? x.Property.PropertyType)
                .Where(t => t.IsEnum)
                .Distinct();

        // ── Read-only fields ────────────────────────────────────────────────────

        /// <summary>
        /// Router fields that are always a live measurement, never a setting. A curated list on purpose —
        /// a name-shaped heuristic ("anything ending in -bytes") would both miss fields and fabricate rules
        /// nobody agreed to, and the same mistake has been made in this repo before with the WinBox alias
        /// table.
        /// </summary>
        private static readonly string[] AlwaysReadOnlyFields =
        {
            "bytes", "packets", "rx-byte", "tx-byte", "rx-packet", "tx-packet",
            "rx-drop", "tx-drop", "rx-error", "tx-error",
            "uptime", "last-link-up-time", "last-link-down-time", "link-downs",
        };

        [TestMethod]
        public void ALiveCounterIsReadOnly()
        {
            // A counter the caller can "set" is a field the mapper will happily put on a /set — the router
            // then either refuses the whole command or, worse, accepts it and the entity stops meaning what
            // it says. These are the fields that are unambiguously measurements on every menu that has them.
            var offenders = new List<string>();

            foreach (var x in Properties())
            {
                if (!AlwaysReadOnlyFields.Contains(x.Attribute.FieldName, StringComparer.OrdinalIgnoreCase))
                    continue;

                bool readOnly = x.Attribute.IsReadOnly
                    || x.Entity.GetCustomAttribute<TikEntityAttribute>().IsReadOnly
                    || x.Property.SetMethod == null
                    || !x.Property.CanWrite;

                if (!readOnly)
                    offenders.Add($"{x.Entity.Name}.{x.Property.Name} ('{x.Attribute.FieldName}') is writable");
            }

            AssertNoOffenders(offenders, "live counters that are not read-only");
        }
    }
}
