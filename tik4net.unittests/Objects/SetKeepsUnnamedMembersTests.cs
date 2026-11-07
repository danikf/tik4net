#nullable enable
// SetKeepsUnnamedMembersTests.cs — a list whose text set keeps the members it does not name (a user group's policy,
// measured on 6.49.21 and 7.x; Docs/findings-value-lists.md §3): an add of `write,read,!ftp,test` reads
// `read,write,test,!local,!telnet,…`, and `set policy=ftp` grants ftp and keeps every other policy. The mapper makes an
// update the whole set by sending the members the loaded value had and the new one leaves out as denied.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.User;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    [TestClass]
    public class SetKeepsUnnamedMembersTests
    {
        private const string Loaded = "read,write,ftp,!local,!telnet";

        private static TikFakeConnection Router(string? policy = Loaded)
        {
            var row = new Dictionary<string, string> { [".id"] = "*1", ["name"] = "g" };
            if (policy != null) row["policy"] = policy;
            return new TikFakeConnection()
                .WithResponse(cmd => cmd.FirstOrDefault() == "/user/group/print",
                    _ => new ITikSentence[] { new TikFakeReSentence(row), new TikFakeDoneSentence() })
                .WithNonQuery(cmd => cmd.First() == "/user/group/set" || cmd.First() == "/user/group/unset")
                .WithScalarResponse(cmd => cmd.First() == "/user/group/add", "*9");
        }

        private static string[] Sent(TikFakeConnection c, string verb)
            => c.SentCommands.Where(x => x.First() == "/user/group/" + verb).SelectMany(x => x.Skip(1)).ToArray();

        private static TikValueList<TikNegatableValue<UserPolicy>> Policies(params TikNegatableValue<UserPolicy>[] items)
            => new TikValueList<TikNegatableValue<UserPolicy>>(items);

        [TestMethod]
        public void AssigningTheGrantedOnes_DeniesEveryOtherPolicyTheGroupHad()
        {
            var connection = Router();
            var group = connection.LoadAll<UserGroup>().Single();

            group.Policy = Policies(UserPolicy.Read, UserPolicy.Write);
            connection.Save(group);

            CollectionAssert.Contains(Sent(connection, "set"), "=policy=read,write,!ftp,!local,!telnet");
        }

        [TestMethod]
        public void AnEditedLoadedValue_IsSentAsItIs()
        {
            var connection = Router();
            var group = connection.LoadAll<UserGroup>().Single();

            group.Policy = group.Policy.Value!.Without(UserPolicy.Ftp).With(TikNegatableValue<UserPolicy>.Not(UserPolicy.Ftp));
            connection.Save(group);

            CollectionAssert.Contains(Sent(connection, "set"), "=policy=read,write,!local,!telnet,!ftp");
        }

        [TestMethod]
        public void ANegatedMember_IsNotDeniedTwice()
        {
            var connection = Router();
            var group = connection.LoadAll<UserGroup>().Single();

            group.Policy = Policies(UserPolicy.Read, TikNegatableValue<UserPolicy>.Not(UserPolicy.Write), UserPolicy.Local);
            connection.Save(group);

            CollectionAssert.Contains(Sent(connection, "set"), "=policy=read,!write,local,!ftp,!telnet");
        }

        [TestMethod]
        public void AnEmptyList_DeniesEveryPolicy()
        {
            // An empty text value changes nothing on the router; the whole set denied is what the caller asked for.
            var connection = Router();
            var group = connection.LoadAll<UserGroup>().Single();

            group.Policy = Policies();
            connection.Save(group);

            CollectionAssert.Contains(Sent(connection, "set"), "=policy=!read,!write,!ftp,!local,!telnet");
        }

        [TestMethod]
        public void APolicyTheEnumLacks_IsDeniedByTheRoutersWord()
        {
            var connection = Router("read,newer-policy,!local");
            var group = connection.LoadAll<UserGroup>().Single();

            group.Policy = Policies(UserPolicy.Read);
            connection.Save(group);

            CollectionAssert.Contains(Sent(connection, "set"), "=policy=read,!newer-policy,!local");
        }

        [TestMethod]
        public void OverStructuredWrites_TheSetIsCompletedAsWell()
        {
            var connection = Router();
            connection.Capabilities |= TikConnectionCapability.StructuredWrites;
            var group = connection.LoadAll<UserGroup>().Single();

            group.Policy = Policies(UserPolicy.Read);
            connection.Save(group);

            CollectionAssert.Contains(Sent(connection, "set"), "=policy=read,!write,!ftp,!local,!telnet");
        }

        [TestMethod]
        public void AnUnchangedValue_SendsNothing()
        {
            var connection = Router();
            var group = connection.LoadAll<UserGroup>().Single();

            group.Comment = "c";
            connection.Save(group);

            Assert.IsFalse(Sent(connection, "set").Any(w => w.StartsWith("=policy=", StringComparison.Ordinal)));
        }

        [TestMethod]
        public void AnUpdateOfAGroupNeverLoaded_IsRefusedBeforeSending()
        {
            var connection = Router();
            var group = new UserGroup { Name = "g", Policy = Policies(UserPolicy.Read) };
            typeof(UserGroup).GetProperty(nameof(UserGroup.Id))!.SetValue(group, "*1");

            var thrown = Assert.ThrowsException<InvalidOperationException>(() => connection.Save(group));

            StringAssert.Contains(thrown.Message, "Load the row");
            Assert.AreEqual(0, Sent(connection, "set").Length);
        }

        [TestMethod]
        public void AnAdd_IsSentAsGiven()
        {
            var connection = Router();
            connection.Save(new UserGroup { Name = "g", Policy = Policies(UserPolicy.Read, UserPolicy.Write) });

            CollectionAssert.Contains(Sent(connection, "add"), "=policy=read,write");
        }

        // ── Declaration ──────────────────────────────────────────────────────

        [TikEntity("/bad-members")]
        public class MembersWithoutNegation
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; private set; }

            [TikProperty("x", SetKeepsUnnamedMembers = true)]
            public TikField<TikValueList<UserPolicy>?> X { get; set; }
        }

        [TikEntity("/bad-both")]
        public class BothSetBehaviours
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; private set; }

            [TikProperty("x", SetKeepsUnnamedMembers = true, SetKeepsUnnamedHalf = true)]
            public TikField<TikValueList<TikNegatableValue<UserPolicy>>?> X { get; set; }
        }

        [TikEntity("/bad-sorted")]
        public class SortedNonList
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; private set; }

            [TikProperty("x", RouterSortsMembers = true)]
            public TikField<string?> X { get; set; }
        }

        [TestMethod]
        public void RouterSortsMembers_OnAFieldThatIsNotAList_IsRefused()
            => AssertRefused<SortedNonList>("RouterSortsMembers describes a list");

        [TestMethod]
        public void SetKeepsUnnamedMembers_WithoutNegatableMembers_IsRefused()
            => AssertRefused<MembersWithoutNegation>("declare its items TikNegatableValue<T>");

        [TestMethod]
        public void SetKeepsUnnamedMembers_WithSetKeepsUnnamedHalf_IsRefused()
            => AssertRefused<BothSetBehaviours>("a field has one");

        private static void AssertRefused<T>(string message) where T : new()
        {
            var thrown = Assert.ThrowsException<ArgumentException>(() =>
            {
                try { TikEntityMetadataCache.GetMetadata<T>(); }
                catch (TypeInitializationException e) when (e.InnerException != null) { throw e.InnerException; }
                catch (System.Reflection.TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
            });
            StringAssert.Contains(thrown.Message, message);
        }
    }
}
