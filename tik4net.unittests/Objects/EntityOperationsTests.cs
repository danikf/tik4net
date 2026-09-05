using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.CapsMan;
using tik4net.Objects.Interface.Wifi;
using tik4net.Objects.Interface.Wireless;
using tik4net.Objects.Ip.Firewall;
using tik4net.Objects.Ip.Hotspot;
using tik4net.Objects.Ip.Ipsec;
using tik4net.Objects.Ppp;
using tik4net.Objects.Routing.Ospf;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// Holds the per-verb entity model introduced for
    /// <see href="https://github.com/danikf/tik4net/issues/84">issue #84</see>: RouterOS decides <c>add</c>,
    /// <c>set</c>, <c>remove</c> and <c>move</c> per menu, and a single "the entity is read-only" bool could
    /// not say that <c>/ppp/active</c> refuses a write and performs a remove.
    /// </summary>
    /// <remarks>
    /// The declarations these tests pin were measured on the lab router (RouterOS 7.24) two ways — the
    /// menu's own tab completion, and a side-effect-free execution probe. <c>EntityOperationMatrixTest</c> in
    /// <c>tik4net.integrationtests</c> re-measures them against a live router on every transport; what is
    /// here is what can be checked without one: that the declarations are internally consistent, and that
    /// the mapper acts on them.
    /// </remarks>
    [TestClass]
    public class EntityOperationsTests
    {
        /// <summary>
        /// The menus that accept <c>remove</c> while refusing <c>add</c> and <c>set</c> — the entities
        /// issue #84 was reported against. Kicking a PPP or HotSpot session, de-authenticating a wireless
        /// client and dropping a tracked connection are ordinary operations, and every one of them used to
        /// throw <c>"Can not save R/O entity."</c>.
        /// </summary>
        private static readonly Type[] RemoveOnlyEntities =
        {
            typeof(PppActive),
            typeof(HotspotActive),
            typeof(FirewallConnection),
            typeof(WirelessRegistrationTable),
            typeof(WifiRegistrationTable),
            typeof(CapsManRegistrationTable),
            typeof(IpsecActivePeers),
        };

        // ── The declarations ──────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void ARemovableStatusMenuDeclaresRemoveAndNothingElse()
        {
            var offenders = new List<string>();

            foreach (Type entity in RemoveOnlyEntities)
            {
                var attribute = entity.GetCustomAttribute<TikEntityAttribute>()!;
                if (attribute.SupportedOperations != TikEntityOperations.Remove)
                    offenders.Add($"{entity.Name} ({attribute.EntityPath}) declares "
                                + $"{attribute.SupportedOperations}, expected Remove");
            }

            Assert.AreEqual(0, offenders.Count,
                "these menus accept /remove and refuse /add and /set - see issue #84:"
                + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        [TestMethod]
        public void TheOspfNeighborMenuDeclaresSetOnly()
        {
            // The mirror image of the seven above, and the reason the enum is not just "removable": this
            // menu has set and neither add nor remove. Its only settable field is comment.
            var attribute = typeof(OspfNeighbor).GetCustomAttribute<TikEntityAttribute>()!;

            Assert.AreEqual(TikEntityOperations.Set, attribute.SupportedOperations);

            var metadata = TikEntityMetadataCache.GetMetadata<OspfNeighbor>();
            CollectionAssert.AreEquivalent(
                new[] { "comment" },
                metadata.Properties.Where(p => !p.IsReadOnly).Select(p => p.FieldName).ToArray(),
                "on a menu whose /set accepts only comment, comment must be the only writable property");
        }

        // ── What the mapper does with them ────────────────────────────────────────────────────────

        [TestMethod]
        public void DeleteSendsRemoveOnARemoveOnlyEntity()
        {
            // The bug in issue #84, as a test: Delete threw InvalidOperationException before the command
            // was ever built. Asserting on the command the fake connection received, so this covers the
            // whole path - metadata, guard, command build - and not just the flag.
            foreach (Type entityType in RemoveOnlyEntities)
            {
                string path = entityType.GetCustomAttribute<TikEntityAttribute>()!.EntityPath;
                var connection = new TikFakeConnection()
                    .WithNonQuery(rows => rows.First() == path + "/remove");

                object entity = WithId(entityType, "*1A");
                InvokeDelete(connection, entityType, entity);

                connection.AssertWasSent(rows => rows.First() == path + "/remove"
                                              && rows.Contains("=.id=*1A"));
            }
        }

        [TestMethod]
        public void SaveIsRefusedOnARemoveOnlyEntityAndSaysWhichVerbIsMissing()
        {
            // "Can not save R/O entity." named neither the verb nor the menu, so a caller could not tell
            // this case from a menu that refuses everything. The message has to carry both.
            foreach (Type entityType in RemoveOnlyEntities)
            {
                string path = entityType.GetCustomAttribute<TikEntityAttribute>()!.EntityPath;
                var connection = new TikFakeConnection();

                // No .id => Save takes the create branch and wants Add.
                var onCreate = Assert.ThrowsException<TargetInvocationException>(
                    () => InvokeSave(connection, entityType, Activator.CreateInstance(entityType)!));
                AssertRefusal(onCreate, path, "add");

                var onUpdate = Assert.ThrowsException<TargetInvocationException>(
                    () => InvokeSave(connection, entityType, WithId(entityType, "*1A")));
                AssertRefusal(onUpdate, path, "set");

                Assert.AreEqual(0, connection.SentCommands.Count,
                    "the guard must refuse before anything reaches the router");
            }
        }

        [TestMethod]
        public void SaveIsRefusedOnAMenuWithoutAddAndAllowedOnTheSameMenusSet()
        {
            // Both branches of the same entity, which is the whole point of splitting the guard: the create
            // path is refused and the update path goes through.
            string path = typeof(OspfNeighbor).GetCustomAttribute<TikEntityAttribute>()!.EntityPath;

            var refused = new TikFakeConnection();
            var ex = Assert.ThrowsException<InvalidOperationException>(
                () => refused.Save(new OspfNeighbor()));
            StringAssert.Contains(ex.Message, "'add'");
            StringAssert.Contains(ex.Message, path);

            var accepted = new TikFakeConnection()
                .WithNonQuery(rows => rows.First() == path + "/set");
            accepted.Save(new OspfNeighbor { Comment = "core link" }.WithId("*2B"));
            accepted.AssertWasSent(rows => rows.First() == path + "/set"
                                        && rows.Contains("=comment=core link"));
        }

        [TestMethod]
        public void DeleteIsRefusedOnAMenuWithoutRemove()
        {
            var connection = new TikFakeConnection();

            var ex = Assert.ThrowsException<InvalidOperationException>(
                () => connection.Delete(new OspfNeighbor().WithId("*2B")));

            StringAssert.Contains(ex.Message, "'remove'");
            StringAssert.Contains(ex.Message, "/routing/ospf/neighbor");
            Assert.AreEqual(0, connection.SentCommands.Count);
        }

        [TestMethod]
        public void SaveListDifferencesChecksOnlyTheVerbsItsDiffNeeds()
        {
            // The one method that reaches for add, set, remove and move. Demanding all of them would refuse
            // a pure-delete pass on a remove-only menu, which is exactly what such a menu does allow - and
            // checking nothing up front would delete rows and then refuse the create.
            string path = typeof(PppActive).GetCustomAttribute<TikEntityAttribute>()!.EntityPath;

            var deleting = new TikFakeConnection()
                .WithNonQuery(rows => rows.First() == path + "/remove");
            deleting.SaveListDifferences(new List<PppActive>(),
                new List<PppActive> { new PppActive().WithId("*1A") });
            deleting.AssertWasSent(rows => rows.First() == path + "/remove");

            var creating = new TikFakeConnection();
            var ex = Assert.ThrowsException<InvalidOperationException>(
                () => creating.SaveListDifferences(new List<PppActive> { new PppActive() },
                                                   new List<PppActive>()));
            StringAssert.Contains(ex.Message, "'add'");
            Assert.AreEqual(0, creating.SentCommands.Count,
                "the whole diff is checked before the first command, so nothing is half-applied");
        }

        // ── Invariants that keep the tree honest ──────────────────────────────────────────────────

        [TestMethod]
        public void EveryConstructorSeedsAllOperations()
        {
            // Positive polarity means the default cannot be the enum's 0, so every ctor has to seed it.
            // A third ctor that forgets would silently declare None and break every entity using it.
            var offenders = new List<string>();

            foreach (ConstructorInfo ctor in typeof(TikEntityAttribute).GetConstructors())
            {
                object[] arguments = ctor.GetParameters().Select(DefaultArgumentFor).ToArray();
                var attribute = (TikEntityAttribute)ctor.Invoke(arguments);

                if (attribute.SupportedOperations != TikEntityOperations.All)
                    offenders.Add($"ctor({string.Join(", ", ctor.GetParameters().Select(p => p.ParameterType.Name))}) "
                                + $"seeds {attribute.SupportedOperations}");
            }

            Assert.AreEqual(0, offenders.Count,
                "every TikEntityAttribute constructor must seed SupportedOperations = All:"
                + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        [TestMethod]
        public void TheOldIsReadOnlyNameIsAnErrorThatNamesItsReplacement()
        {
            // Deleting the 3.x name would have given a downstream caller CS0117 ("no definition for
            // 'IsReadOnly'") and left them to work out what replaced it. [Obsolete(error: true)] gives them
            // the same hard stop with the answer attached - and, unlike a warning, it cannot be ignored into
            // a silent behaviour change: the getter now answers FALSE for /ppp/active, so code that kept
            // reading it would start offering an edit form on a menu whose fields are read-only.
            foreach (Type declaring in new[] { typeof(TikEntityAttribute), typeof(TikEntityMetadata) })
            {
                string member = declaring.Name + ".IsReadOnly";
                var property = declaring.GetProperty("IsReadOnly");
                Assert.IsNotNull(property, member + " must still exist, as an error");

                var obsolete = property!.GetCustomAttribute<ObsoleteAttribute>();
                Assert.IsNotNull(obsolete, member + " must be [Obsolete]");
                Assert.IsTrue(obsolete!.IsError,
                    member + " must be an error, not a warning - a suppressed warning here is a silent "
                    + "change of meaning");
                StringAssert.Contains(obsolete.Message, "TikEntityOperations",
                    "the message is the whole point: it has to name the replacement");
                StringAssert.Contains(obsolete.Message, "issues/84",
                    "and point at the report that explains why the bool could not stay");
            }
        }

        [TestMethod]
        public void TheOldIsReadOnlyNameStillMapsOntoTheNewOne()
        {
            // Reached by reflection because the compiler will not let anything call it. Two callers still
            // depend on the mapping: the 7-arg constructor's isReadOnly parameter, which is NOT obsolete,
            // and anyone reading the attribute dynamically.
            var attribute = new TikEntityAttribute("/test/entity");
            var property = typeof(TikEntityAttribute).GetProperty("IsReadOnly")!;

            Assert.AreEqual(false, property.GetValue(attribute), "a fresh attribute supports everything");

            property.SetValue(attribute, true);
            Assert.AreEqual(TikEntityOperations.None, attribute.SupportedOperations);
            Assert.AreEqual(true, property.GetValue(attribute));

            property.SetValue(attribute, false);
            Assert.AreEqual(TikEntityOperations.All, attribute.SupportedOperations);

            // The lossy direction, spelled out: a remove-only menu reads back as NOT read-only.
            attribute.SupportedOperations = TikEntityOperations.Remove;
            Assert.AreEqual(false, property.GetValue(attribute));

            // ...and the constructor that still takes the bool agrees with the setter.
            var viaCtor = new TikEntityAttribute("/test/entity", "/print",
                TikCommandParameterFormat.Filter, true, false, false, false);
            Assert.AreEqual(TikEntityOperations.None, viaCtor.SupportedOperations);
        }

        [TestMethod]
        public void AnEntityDeclaringAddOrSetHasSomethingToWrite()
        {
            // The invariant that stops the /ip/ipsec/active-peers mistake from happening quietly: declaring
            // a write verb on a menu whose every property is read-only makes Save build an /add or /set
            // with no fields on it - a command that either does nothing or is refused, and either way says
            // the entity is writable when nothing about it is.
            //
            // Only this direction is checkable here. The converse - "a menu with no write verb has no
            // writable property" - is true by construction, because AreFieldsReadOnly is half of
            // TikEntityPropertyAccessor.IsReadOnly.
            var offenders = new List<string>();

            foreach (Type entityType in EntityCatalogMarkdown.EntityTypes())
            {
                var attribute = entityType.GetCustomAttribute<TikEntityAttribute>()!;
                if (!attribute.SupportedOperations.HasFlag(TikEntityOperations.Add)
                    && !attribute.SupportedOperations.HasFlag(TikEntityOperations.Set))
                    continue;

                var metadata = new TikEntityMetadata(entityType);
                if (!metadata.Properties.Any(p => !p.IsReadOnly))
                    offenders.Add($"{entityType.Name} ({attribute.EntityPath}) declares "
                                + $"{attribute.SupportedOperations} but has no writable property");
            }

            Assert.AreEqual(0, offenders.Count,
                "an entity that declares add or set must have something to send:"
                + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        [TestMethod]
        public void AMenuThatOnlyRemovesStillHasReadOnlyFields()
        {
            // The distinction TikEntityPropertyAccessor now makes. Reading the verb set as a whole there -
            // "supports something, therefore writable" - would put status fields on a /set the menu has not
            // got, which is the failure mode the fix could have introduced.
            foreach (Type entityType in RemoveOnlyEntities)
            {
                var metadata = new TikEntityMetadata(entityType);

                Assert.IsTrue(metadata.AreFieldsReadOnly, entityType.Name + ".AreFieldsReadOnly");
                Assert.IsTrue(metadata.Supports(TikEntityOperations.Remove), entityType.Name + " supports Remove");
                Assert.IsFalse(metadata.Properties.Any(p => !p.IsReadOnly),
                    entityType.Name + " must have no writable property");
            }
        }

        [TestMethod]
        public void SupportsAsksForAllOfTheGivenFlags()
        {
            var metadata = TikEntityMetadataCache.GetMetadata<PppActive>();

            Assert.IsTrue(metadata.Supports(TikEntityOperations.Remove));
            Assert.IsTrue(metadata.Supports(TikEntityOperations.None), "None asks nothing and is always met");
            Assert.IsFalse(metadata.Supports(TikEntityOperations.Add | TikEntityOperations.Remove),
                "a combined value must mean ALL of them, matching ITikConnection.Supports");
        }

        // ── Helpers ───────────────────────────────────────────────────────────────────────────────

        private static void AssertRefusal(TargetInvocationException wrapper, string path, string verb)
        {
            var inner = wrapper.InnerException as InvalidOperationException;
            Assert.IsNotNull(inner, "expected InvalidOperationException, got " + wrapper.InnerException);
            StringAssert.Contains(inner!.Message, "'" + verb + "'");
            StringAssert.Contains(inner.Message, path);
        }

        private static object WithId(Type entityType, string id)
        {
            object entity = Activator.CreateInstance(entityType)!;
            return typeof(TikFakeEntityExtensions).GetMethod(nameof(TikFakeEntityExtensions.WithId))!
                .MakeGenericMethod(entityType).Invoke(null, new[] { entity, (object)id })!;
        }

        private static void InvokeDelete(ITikConnection connection, Type entityType, object entity)
            => typeof(TikConnectionExtensions).GetMethod(nameof(TikConnectionExtensions.Delete))!
                .MakeGenericMethod(entityType).Invoke(null, new[] { connection, entity });

        private static void InvokeSave(ITikConnection connection, Type entityType, object entity)
            => typeof(TikConnectionExtensions).GetMethod(nameof(TikConnectionExtensions.Save))!
                .MakeGenericMethod(entityType).Invoke(null, new object[] { connection, entity, null, TikSaveMode.Default });

        /// <summary>A throwaway value of the right type, so a ctor can be invoked just to read its defaults.</summary>
        private static object DefaultArgumentFor(ParameterInfo parameter)
            => parameter.ParameterType == typeof(string)
                ? "/test/entity"
                : Activator.CreateInstance(parameter.ParameterType)!;
    }
}
