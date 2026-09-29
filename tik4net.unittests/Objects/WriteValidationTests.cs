// WriteValidationTests.cs — TikConnectionSetup.ValidateWrites: an entity write checked against the router's argument
// list, a renamed field written under the name the router takes. The lists are 6.49.13's (routing-mark) and
// 7.24.4's (routing-table) for /ip/route, measured 2026-09-28.

#nullable enable

using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    [TestClass]
    public class WriteValidationTests
    {
        private static readonly TikEntityMetadata Route = TikEntityMetadataCache.GetMetadata<IpRoute>();

        private static TikMenuSchema RouterOs6Route() => new TikMenuSchema("/ip/route", TikMenuSchemaSource.CliCompletion,
            new[] { "add", "set", "get" },
            new[] { "dst-address", "gateway", "routing-mark", "comment", "disabled" },
            new[] { "numbers", "dst-address", "gateway", "routing-mark", "comment", "disabled" },
            Array.Empty<string>(), null);

        private static ITikCommand Command(string verb, params (string Name, string Value)[] parameters)
        {
            var connection = new TikFakeConnection();
            var command = connection.CreateCommand("/ip/route/" + verb, TikCommandParameterFormat.NameValue);
            foreach (var p in parameters)
                command.AddParameter(p.Name, p.Value);
            return command;
        }

        [TestMethod]
        public void ARenamedFieldIsWrittenUnderTheNameTheRouterTakes()
        {
            var add = Command("add", ("dst-address", "203.0.113.7/32"), ("routing-table", "t4n-a"));

            TikWriteValidation.Check(add, Route, RouterOs6Route());

            CollectionAssert.AreEqual(new[] { "dst-address", "routing-mark" }, add.Parameters.Select(p => p.Name).ToArray());
            Assert.AreEqual("t4n-a", add.Parameters.Single(p => p.Name == "routing-mark").Value);
        }

        [TestMethod]
        public void AnUnsetNamesTheFieldInItsValue_WhichIsRenamedToo()
        {
            var unset = Command("unset", (".id", "*1"), ("value-name", "routing-table"));

            TikWriteValidation.Check(unset, Route, RouterOs6Route());

            Assert.AreEqual("routing-mark", unset.Parameters.Single(p => p.Name == "value-name").Value);
        }

        [TestMethod]
        public void AnUnsetOfAFieldTheRouterCannotClear_IsRefusedAsSuch()
        {
            // 6.49.13 /ip route: 'unset value-name=' lists routing-mark and check-gateway, not dst-address or gateway.
            var schema = new TikMenuSchema("/ip/route", TikMenuSchemaSource.CliCompletion, new[] { "add", "set", "unset" },
                null, new[] { "numbers", "dst-address", "gateway", "routing-mark" }, Array.Empty<string>(), null,
                unsetFields: new[] { "routing-mark", "check-gateway" });

            TikWriteValidation.Check(Command("unset", (".id", "*1"), ("value-name", "routing-table")), Route, schema);
            var ex = Assert.ThrowsException<TikUnknownFieldException>(
                () => TikWriteValidation.Check(Command("unset", (".id", "*1"), ("value-name", "gateway")), Route, schema));

            Assert.AreEqual(TikUnknownFieldUse.Unset, ex.Use);
            CollectionAssert.AreEqual(new[] { "gateway" }, ex.Fields.ToArray());
            StringAssert.Contains(ex.Message, "cannot clear");
        }

        [TestMethod]
        public void FieldsTheRouterTakesUnderNoName_AreRefusedTogether()
        {
            var set = Command("set", (".id", "*1"), ("scope", "30"), ("t4n-bogus", "1"), ("comment", "c"));

            var ex = Assert.ThrowsException<TikUnknownFieldException>(() => TikWriteValidation.Check(set, Route, RouterOs6Route()));

            CollectionAssert.AreEqual(new[] { "scope", "t4n-bogus" }, ex.Fields.ToArray());
            StringAssert.Contains(ex.Message, "nothing was sent");
            Assert.AreEqual(TikUnknownFieldUse.Write, ex.Use);
        }

        [TestMethod]
        public void MarkersAndTheArgumentsEveryMenuTakes_PassUnlisted()
        {
            var add = Command("add", ("dst-address", "203.0.113.7/32"), ("place-before", "*2"), (".winbox-labels", "x=X"));

            TikWriteValidation.Check(add, Route, RouterOs6Route());
        }

        [TestMethod]
        public void AVerbTheMenuDoesNotHave_IsLeftToTheRouter()
        {
            var singleton = new TikMenuSchema("/ip/route", TikMenuSchemaSource.ConsoleInspect, new[] { "set" },
                null, new[] { "name" }, Array.Empty<string>(), null);

            TikWriteValidation.Check(Command("add", ("t4n-bogus", "1")), Route, singleton);
        }

        [TestMethod]
        public void ValidateWritesIsOffByDefault_AndReachesTheSchemaTransports()
        {
            Assert.IsFalse(new TikConnectionSetup("192.0.2.1", "user", "pwd").ValidateWrites);
            var setup = new TikConnectionSetup("192.0.2.1", "user", "pwd") { ValidateWrites = true };
            foreach (TikConnectionType type in Enum.GetValues(typeof(TikConnectionType)))
            {
                if (type == TikConnectionType.Ssh)
                    continue;   // a satellite package, not referenced here
                using (var connection = setup.CreateUnopened(type))
                    Assert.IsTrue(((ITikMenuSchemaConnection)connection).ValidateWrites, type.ToString());
            }
        }
    }
}
