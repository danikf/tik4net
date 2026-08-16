using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// B1: the mapper reads and writes entity properties through a delegate bound once per property, not
    /// through <c>PropertyInfo.GetValue/SetValue</c> per field per row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The behaviour half of this file passes against the old code too, and is meant to — it is what says the
    /// optimisation changed nothing observable, and it was run against the old accessor first for exactly
    /// that reason. The half that could not exist before is
    /// <see cref="EveryMappedPropertyOfEveryShippedEntityBindsACompiledAccessor"/>: the fallback to
    /// reflection is deliberately silent, so without an explicit assertion a binding that quietly failed for
    /// every property would look exactly like a success and the whole item would be a no-op.
    /// </para>
    /// <para>
    /// The case worth naming is the <b>private setter</b>. Read-only entity properties are declared
    /// <c>{ get; private set; }</c> throughout — <c>.id</c> on every entity that has one — and the mapper
    /// writes them on load. That is why the accessor binds with <c>MethodInfo.CreateDelegate</c> instead of
    /// a compiled expression tree, which cannot reach a non-public accessor from the anonymous assembly it
    /// is emitted into.
    /// </para>
    /// </remarks>
    [TestClass]
    public class CompiledAccessorTests
    {
        [TikEntity("/test/accessor-entity")]
        internal class AccessorEntity
        {
            [TikProperty(".id", IsReadOnly = true)]
            public string Id { get; private set; }

            [TikProperty("name")]
            public string Name { get; set; }

            [TikProperty("port")]
            public int Port { get; set; }

            [TikProperty("enabled", DefaultValue = "yes")]
            public bool? Enabled { get; set; }

            [TikProperty("read-only-counter", IsReadOnly = true)]
            public long Packets { get; private set; }
        }

        private static TikEntityPropertyAccessor Accessor(string propertyName)
            => TikEntityMetadataCache.GetMetadata<AccessorEntity>()
                .Properties.Single(p => p.PropertyName == propertyName);

        // ── The fast path is actually taken ─────────────────────────────────────

        [TestMethod]
        public void APropertyWithAPrivateSetterStillBindsACompiledAccessor()
        {
            Assert.IsTrue(Accessor("Id").UsesCompiledAccessors,
                ".id is declared { get; private set; } on every entity that has one — if this is the case that falls back, the optimisation misses the property written on every single row.");
            Assert.IsTrue(Accessor("Packets").UsesCompiledAccessors);
        }

        [TestMethod]
        public void EveryMappedPropertyOfEveryShippedEntityBindsACompiledAccessor()
        {
            Type[] entityTypes = typeof(FirewallFilter).Assembly.GetTypes()
                .Where(t => t.GetCustomAttributes(typeof(TikEntityAttribute), true).Any())
                .ToArray();

            Assert.IsTrue(entityTypes.Length > 100, "sanity: the entity set should be found by reflection");

            var uncompiled = entityTypes
                .SelectMany(t => new TikEntityMetadata(t).Properties
                    .Where(p => !p.UsesCompiledAccessors)
                    .Select(p => t.Name + "." + p.PropertyName))
                .ToList();

            Assert.AreEqual(0, uncompiled.Count,
                "these properties fell back to reflection: " + string.Join(", ", uncompiled));
        }

        // ── …and reads and writes exactly what reflection did ───────────────────

        [TestMethod]
        public void ACompiledSetterWritesTheSameValueReflectionDid()
        {
            var entity = new AccessorEntity();

            Accessor("Id").SetEntityValue(entity, "*1A");
            Accessor("Name").SetEntityValue(entity, "ether1");
            Accessor("Port").SetEntityValue(entity, "8291");
            Accessor("Enabled").SetEntityValue(entity, "no");
            Accessor("Packets").SetEntityValue(entity, "4294967296");

            Assert.AreEqual("*1A", entity.Id);
            Assert.AreEqual("ether1", entity.Name);
            Assert.AreEqual(8291, entity.Port);
            Assert.AreEqual(false, entity.Enabled);
            Assert.AreEqual(4294967296L, entity.Packets);
        }

        [TestMethod]
        public void ACompiledGetterReadsTheSameValueReflectionDid()
        {
            var entity = new AccessorEntity { Name = "ether1", Port = 8291, Enabled = true };

            Assert.AreEqual("ether1", Accessor("Name").GetEntityValue(entity));
            Assert.AreEqual("8291", Accessor("Port").GetEntityValue(entity));
            Assert.AreEqual("yes", Accessor("Enabled").GetEntityValue(entity));
        }

        [TestMethod]
        public void ANullOnANullablePropertySurvivesTheCompiledSetter()
        {
            var entity = new AccessorEntity { Enabled = true };

            // The router did not report the field at all. That has to reach the property as null — a compiled
            // setter that boxed it into `default(bool)` would turn "nothing was said" into an explicit "no",
            // which is the conflation B4 removed.
            Accessor("Enabled").SetEntityValue(entity, null);

            Assert.IsNull(entity.Enabled);
            Assert.IsNull(Accessor("Enabled").GetEntityValue(entity));
        }

        internal class BaseEntity
        {
            // Public setter on purpose. A PRIVATE setter declared on a base class is not visible through the
            // derived type at all — PropertyInfo.SetMethod comes back null and SetValue throws "Property set
            // method not found" — so such a property could never be loaded, before this change or after it.
            // No shipped entity has that shape (EveryMappedPropertyOfEveryShippedEntity… is what says so),
            // and inventing one here would test the CLR's inheritance rules rather than the accessor.
            [TikProperty(".id", IsReadOnly = true)]
            public string Id { get; set; }
        }

        [TikEntity("/test/derived-entity")]
        internal class DerivedEntity : BaseEntity
        {
            [TikProperty("name")]
            public string Name { get; set; }
        }

        [TestMethod]
        public void ACompiledAccessorOnAnInheritedPropertyWritesTheDerivedInstance()
        {
            // A property declared on a base class binds a delegate whose receiver is the BASE type, while the
            // instance handed to it is the derived one. Getting that cast wrong is an exception on the first
            // row rather than a wrong value, so it is worth pinning separately.
            var metadata = TikEntityMetadataCache.GetMetadata<DerivedEntity>();
            var entity = new DerivedEntity();

            foreach (var property in metadata.Properties)
                property.SetEntityValue(entity, "x");

            Assert.AreEqual("x", entity.Id);
            Assert.AreEqual("x", entity.Name);
            Assert.IsTrue(metadata.Properties.All(p => p.UsesCompiledAccessors));
        }
    }
}
