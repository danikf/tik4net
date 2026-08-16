using System;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// B2: the enum ↔ wire-value mapping is resolved once per enum type instead of by walking
    /// <c>Enum.GetNames</c> and asking every member for its attribute on every value converted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This whole file is a behaviour pin, and every method in it was run against the old conversion code
    /// first — an optimisation's tests are supposed to pass before it lands, and one that did not would mean
    /// the tables had changed something rather than cached it. What the file is really for is the edges the
    /// old chain got right by accident of the LINQ it was written in: <c>Single()</c> rejecting a duplicate
    /// wire value, an unknown value throwing rather than returning member zero, and the zero member of a
    /// <c>[Flags]</c> enum formatting as its own declared value.
    /// </para>
    /// <para>
    /// One difference is deliberate and asserted below: formatting a value that is not a defined member used
    /// to raise <see cref="ArgumentNullException"/> — <c>GetRuntimeField</c> returned null and
    /// <c>GetCustomAttribute</c> is an extension method, so the null arrived as an argument — and now raises
    /// a <see cref="FormatException"/> that says which value and which type. It is the only method here that
    /// fails against the old code, which is what makes it the only one that is evidence of anything.
    /// </para>
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

        [Flags]
        public enum FlagType
        {
            [TikEnum("")] Empty = 0,
            [TikEnum("established")] Established = 1,
            [TikEnum("invalid")] Invalid = 2,
            [TikEnum("new")] New = 4,
        }

        [Flags]
        public enum FlagTypeWithoutZeroMember
        {
            [TikEnum("established")] Established = 1,
            [TikEnum("invalid")] Invalid = 2,
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
            public PlainType Plain { get; set; }

            [TikProperty("flags")]
            public FlagType Flags { get; set; }

            [TikProperty("flags-no-zero")]
            public FlagTypeWithoutZeroMember FlagsNoZero { get; set; }

            [TikProperty("ambiguous")]
            public AmbiguousType Ambiguous { get; set; }

            [TikProperty("nullable-plain")]
            public PlainType? NullablePlain { get; set; }
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
            Assert.AreEqual(PlainType.AddSrcToAddressList, entity.Plain);

            // The router's casing is not guaranteed, and the old lookup compared OrdinalIgnoreCase.
            Accessor("Plain").SetEntityValue(entity, "DROP");
            Assert.AreEqual(PlainType.Drop, entity.Plain);
        }

        [TestMethod]
        public void AFlagsEnumParsesEachCommaSeparatedPart()
        {
            var entity = new EnumEntity();

            Accessor("Flags").SetEntityValue(entity, "established,new");
            Assert.AreEqual(FlagType.Established | FlagType.New, entity.Flags);

            // A single part takes the non-flags branch (no comma) and must land on the same member.
            Accessor("Flags").SetEntityValue(entity, "invalid");
            Assert.AreEqual(FlagType.Invalid, entity.Flags);
        }

        [TestMethod]
        public void AnUnknownValueThrowsRatherThanFallingBackToMemberZero()
        {
            var entity = new EnumEntity();

            var ex = Assert.ThrowsException<FormatException>(
                () => Accessor("Plain").SetEntityValue(entity, "no-such-action"));
            StringAssert.Contains(ex.Message, "Plain(plain)", "the caller's wrapper still names the property");

            Assert.ThrowsException<FormatException>(
                () => Accessor("Flags").SetEntityValue(entity, "established,no-such-state"));
        }

        [TestMethod]
        public void ADuplicateWireValueIsRejectedRatherThanResolvedArbitrarily()
        {
            // Two members declaring one wire value is an entity defect, and the old Single() turned it into
            // an exception. Nothing in the shipped entity set has the shape today (all 150 enums checked),
            // which is exactly why it is worth pinning: a cache is the natural place to lose it, since a
            // dictionary would otherwise just keep the last writer.
            Assert.ThrowsException<FormatException>(
                () => Accessor("Ambiguous").SetEntityValue(new EnumEntity(), "same"));
        }

        // ── Formatting ──────────────────────────────────────────────────────────

        [TestMethod]
        public void APlainEnumFormatsAsItsWireValue()
        {
            Assert.AreEqual("add-src-to-address-list",
                Accessor("Plain").GetEntityValue(new EnumEntity { Plain = PlainType.AddSrcToAddressList }));
        }

        [TestMethod]
        public void AFlagsEnumFormatsInDeclarationOrder()
        {
            // Declaration order, not the order the caller OR'd them in — the old join walked Enum.GetNames.
            Assert.AreEqual("established,new",
                Accessor("Flags").GetEntityValue(new EnumEntity { Flags = FlagType.New | FlagType.Established }));
        }

        [TestMethod]
        public void ZeroFormatsAsTheZeroMemberOrAsEmpty()
        {
            Assert.AreEqual("", Accessor("Flags").GetEntityValue(new EnumEntity { Flags = FlagType.Empty }),
                "the zero member declares \"\" and that is what goes on the wire");
            Assert.AreEqual("", Accessor("FlagsNoZero").GetEntityValue(new EnumEntity()),
                "an enum with no zero member at all still formats zero as empty rather than throwing");
        }

        [TestMethod]
        public void AnUndefinedValueSaysWhichValueAndWhichType()
        {
            // Was an ArgumentNullException out of a null FieldInfo. A caller who casts a raw int gets told.
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
            Assert.AreEqual(PlainType.Drop, entity.NullablePlain);
            Assert.AreEqual("drop", Accessor("NullablePlain").GetEntityValue(entity));

            Accessor("NullablePlain").SetEntityValue(entity, null);
            Assert.IsNull(entity.NullablePlain);
        }

        // ── The cache is shared, and it agrees with the entities it serves ──────

        [TestMethod]
        public void OneTableIsBuiltPerEnumTypeNotPerProperty()
        {
            // Two accessors over the same enum type must reach the same instance, or the table is being
            // rebuilt per property and B2's cost simply moved from per-conversion to per-entity-type.
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

                    // A [Flags] zero member parses through the same table but formats via FormatFlags, which
                    // is covered above; here only the parse direction is common to both shapes.
                    Assert.AreEqual(member, table.Parse(wire), enumType.Name + "." + name + " parses back");
                }
            }
        }
    }
}
