using System;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// The enum ↔ wire-value mapping is resolved once per enum type instead of by walking
    /// <c>Enum.GetNames</c> and asking every member for its attribute on every value converted.
    /// </summary>
    /// <remarks>
    /// An unknown value does not fall back to member zero, a duplicate wire value is rejected, and formatting a value
    /// that is not a defined member says which value and which type.
    /// </remarks>
    [TestClass]
    public class EnumMetadataTests
    {
        public enum PlainType
        {
            [TikEnum("accept")] Accept,
            [TikEnum("drop")] Drop,
            [TikEnum("add-src-to-address-list")] AddSrcToAddressList,
        }

        public enum AmbiguousType
        {
            [TikEnum("same")] First,
            [TikEnum("same")] Second,
        }

        [TikEntity("/test/enum-entity")]
        internal class EnumEntity
        {
            [TikProperty("plain")]
            public TikField<PlainType?> Plain { get; set; }

            [TikProperty("ambiguous")]
            public TikField<AmbiguousType?> Ambiguous { get; set; }

            [TikProperty("nullable-plain")]
            public TikField<PlainType?> NullablePlain { get; set; }
        }

        private static TikEntityPropertyAccessor Accessor(string propertyName)
            => TikEntityMetadataCache.GetMetadata<EnumEntity>()
                .Properties.Single(p => p.PropertyName == propertyName);

        // ── Parsing ─────────────────────────────────────────────────────────────

        [TestMethod]
        public void APlainEnumParsesItsWireValue()
        {
            var entity = new EnumEntity();

            Accessor("Plain").SetEntityValue(entity, "add-src-to-address-list");
            Assert.IsTrue(entity.Plain == PlainType.AddSrcToAddressList);

            // The router's casing is not guaranteed, and the old lookup compared OrdinalIgnoreCase.
            Accessor("Plain").SetEntityValue(entity, "DROP");
            Assert.IsTrue(entity.Plain == PlainType.Drop);
        }

        [TestMethod]
        public void AnUnknownValueIsNeverMemberZero()
        {
            var entity = new EnumEntity();

            Accessor("Plain").SetEntityValue(entity, "no-such-action");

            Assert.AreEqual(TikFieldState.Unparsed, entity.Plain.State);
            Assert.AreEqual("no-such-action", entity.Plain.RawValue);
            Assert.ThrowsException<FormatException>(() => TikEnumMetadata.Get(typeof(PlainType)).Parse("no-such-action"));
        }

        [TestMethod]
        public void ADuplicateWireValueIsRejectedRatherThanResolvedArbitrarily()
        {
            // Two members declaring one wire value is an entity defect. Nothing in the shipped entity set has the shape
            // today, which is why it is worth pinning: a dictionary would otherwise just keep the first writer.
            var entity = new EnumEntity();
            Accessor("Ambiguous").SetEntityValue(entity, "same");
            Assert.AreEqual(TikFieldState.Unparsed, entity.Ambiguous.State);
            Assert.ThrowsException<FormatException>(() => TikEnumMetadata.Get(typeof(AmbiguousType)).Parse("same"));
        }

        // ── Formatting ──────────────────────────────────────────────────────────

        [TestMethod]
        public void APlainEnumFormatsAsItsWireValue()
        {
            Assert.AreEqual("add-src-to-address-list",
                Accessor("Plain").GetEntityValue(new EnumEntity { Plain = PlainType.AddSrcToAddressList }));
        }

        [TestMethod]
        public void AnUndefinedValueSaysWhichValueAndWhichType()
        {
            var ex = Assert.ThrowsException<FormatException>(
                () => Accessor("Plain").GetEntityValue(new EnumEntity { Plain = (PlainType)99 }));
            StringAssert.Contains(ex.Message, "PlainType");
        }

        [TestMethod]
        public void ANullableEnumKeepsItsNullAndItsValues()
        {
            var entity = new EnumEntity();

            Assert.IsNull(Accessor("NullablePlain").GetEntityValue(entity));

            Accessor("NullablePlain").SetEntityValue(entity, "drop");
            Assert.IsTrue(entity.NullablePlain == PlainType.Drop);
            Assert.AreEqual("drop", Accessor("NullablePlain").GetEntityValue(entity));

            Accessor("NullablePlain").SetEntityValue(entity, null);
            Assert.AreEqual(TikFieldState.Absent, entity.NullablePlain.State);
        }

        // ── The cache is shared, and it agrees with the entities it serves ──────

        [TestMethod]
        public void OneTableIsBuiltPerEnumTypeNotPerProperty()
        {
            // Two accessors over the same enum type must reach the same instance, or the table is being
            // rebuilt per property and the cost simply moved from per-conversion to per-entity-type.
            var first = TikEnumMetadata.Get(typeof(FirewallFilter.ActionType));
            var second = TikEnumMetadata.Get(typeof(FirewallFilter.ActionType));

            Assert.AreSame(first, second);
        }

        [TestMethod]
        public void EveryEnumOfEveryShippedEntityRoundTripsThroughTheTables()
        {
            // The real assertion of this change: for every enum an entity actually uses, every member's wire
            // value must format to itself and parse back to the same member. A table that dropped or
            // conflated a member would otherwise only show up on the router.
            Type[] enumTypes = typeof(FirewallFilter).Assembly.GetTypes()
                .Where(t => t.GetCustomAttributes(typeof(TikEntityAttribute), true).Any())
                .SelectMany(t => new TikEntityMetadata(t).Properties)
                .Select(p => p.ValueType)
                .Where(t => t.GetTypeInfo().IsEnum)
                .Distinct()
                .ToArray();

            Assert.IsTrue(enumTypes.Length > 100, "sanity: the entity enums should be found by reflection");

            foreach (Type enumType in enumTypes)
            {
                var table = TikEnumMetadata.Get(enumType);
                foreach (string name in Enum.GetNames(enumType))
                {
                    string wire = enumType.GetRuntimeField(name).GetCustomAttribute<TikEnumAttribute>(false)?.Value;
                    if (wire == null)
                        continue;

                    object member = Enum.Parse(enumType, name, true);
                    Assert.AreEqual(wire, table.Format(member), enumType.Name + "." + name + " formats");

                    Assert.AreEqual(member, table.Parse(wire), enumType.Name + "." + name + " parses back");
                }
            }
        }
    }
}
