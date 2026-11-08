#nullable enable
// TikListSetBehaviourTests.cs — which fields declare a set behaviour other than Exact, and what a declaration the
// mapper cannot honour is refused with. The behaviours themselves are pinned by SetKeepsUnnamedMembersTests (policy)
// and TikValueListMapperTests (tcp-flags).

using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;
using tik4net.Objects.User;

namespace tik4net.unittests.Objects
{
    [TestClass]
    public class TikListSetBehaviourTests
    {
        private static TikEntityPropertyAccessor Field<T>(string fieldName) where T : new()
            => TikEntityMetadataCache.GetMetadata<T>().Properties.Single(p => p.FieldName == fieldName);

        [TestMethod]
        public void TcpFlags_KeepsTheUnnamedHalf_OnFilterMangleAndRaw()
        {
            Assert.AreEqual(TikListSetBehaviour.KeepsUnnamedHalf, Field<FirewallFilter>("tcp-flags").SetBehaviour);
            Assert.AreEqual(TikListSetBehaviour.KeepsUnnamedHalf, Field<FirewallMangle>("tcp-flags").SetBehaviour);
            Assert.AreEqual(TikListSetBehaviour.KeepsUnnamedHalf, Field<FirewallRaw>("tcp-flags").SetBehaviour);
        }

        [TestMethod]
        public void UserGroupPolicy_KeepsTheUnnamedMembers()
            => Assert.AreEqual(TikListSetBehaviour.KeepsUnnamedMembers, Field<UserGroup>("policy").SetBehaviour);

        [TestMethod]
        public void AnUndeclaredField_IsExact()
        {
            Assert.AreEqual(TikListSetBehaviour.Exact, Field<UserGroup>("name").SetBehaviour);
            Assert.AreEqual(TikListSetBehaviour.Exact, Field<FirewallFilter>("dst-port").SetBehaviour);
            Assert.AreEqual(TikListSetBehaviour.Exact, default(TikListSetBehaviour));
        }

        [TikEntity("/bad-undefined")]
        public class UndefinedBehaviour
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; private set; }

            [TikProperty("x", SetBehaviour = (TikListSetBehaviour)3)]
            public TikField<TikValueList<TikNegatableValue<UserPolicy>>?> X { get; set; }
        }

        [TestMethod]
        public void AValueTheEnumDoesNotDefine_IsRefused()
        {
            var thrown = Assert.ThrowsException<ArgumentException>(() =>
            {
                try { TikEntityMetadataCache.GetMetadata<UndefinedBehaviour>(); }
                catch (TypeInitializationException e) when (e.InnerException != null) { throw e.InnerException; }
                catch (System.Reflection.TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
            });
            StringAssert.Contains(thrown.Message, "is not a TikListSetBehaviour");
        }
    }
}
