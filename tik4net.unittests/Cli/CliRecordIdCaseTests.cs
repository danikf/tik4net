// CliRecordIdCaseTests.cs — router-free tests for the spelling of a row's .id on the CLI transports.
//
// RouterOS before 7.20 prints a row id in LOWERCASE hex on the CLI (*59b) — in as-value, in the JSON read and in
// what `add` answers — while its binary API, and every version from 7.20 on, prints *59B. Measured on 7.19.6 and
// 7.24.4 against the same rows. An id is a key: a caller who reads a table over one transport and matches it
// against another, or against an id it kept, finds nothing, so a CLI read hands it back in the API's spelling.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    [TestClass]
    public class CliRecordIdCaseTests
    {
        [TestMethod]
        public void TheIdFieldIsSpelledInUppercaseHex()
        {
            Assert.AreEqual("*59B", CliValueNormalizer.Normalize(".id", "*59b"));
            Assert.AreEqual("*5A0", CliValueNormalizer.Normalize(".id", "*5a0"));
            Assert.AreEqual("*FFFFFFFF", CliValueNormalizer.Normalize(".id", "*ffffffff"));
        }

        [TestMethod]
        public void AnIdAlreadyInTheApisSpellingIsUnchanged()
        {
            Assert.AreEqual("*13", CliValueNormalizer.Normalize(".id", "*13"));
            Assert.AreEqual("*1A", CliValueNormalizer.Normalize(".id", "*1A"));
        }

        /// <summary>Only a value shaped like an id, and only in the id field: a comment or a name that happens
        /// to start with a star is text, and is left as the router sent it.</summary>
        [TestMethod]
        public void NothingElseIsTouched()
        {
            Assert.AreEqual("*abc", CliValueNormalizer.Normalize("comment", "*abc"));
            Assert.AreEqual("*ab", CliValueNormalizer.Normalize("name", "*ab"));
            Assert.AreEqual("*xyz", CliValueNormalizer.Normalize(".id", "*xyz"));
            Assert.AreEqual("*", CliValueNormalizer.Normalize(".id", "*"));
            Assert.AreEqual("abc", CliValueNormalizer.Normalize(".id", "abc"));
        }

        [TestMethod]
        public void AnAsValueReadReturnsTheApisSpelling()
        {
            var rows = CliOutputParser.ParseAsValue(".id=*59b;address=198.18.0.1;list=x;.id=*5a0;address=198.18.0.2;list=x");
            CollectionAssert.AreEqual(new[] { "*59B", "*5A0" }, rows.Select(r => r.GetResponseField(".id")).ToArray());
        }

        [TestMethod]
        public void AJsonReadReturnsTheApisSpelling()
        {
            var rows = CliJsonParser.ParseJson("[{\".id\":\"*59b\",\"list\":\"x\"},{\".id\":\"*5a0\",\"list\":\"x\"}]");
            CollectionAssert.AreEqual(new[] { "*59B", "*5A0" }, rows.Select(r => r.GetResponseField(".id")).ToArray());
        }

        [TestMethod]
        public void AnAddReturnsTheApisSpelling()
        {
            var connection = new AddAnsweringConnection("*5a0");
            connection.OpenScripted();
            Assert.AreEqual("*5A0", connection.CreateCommandAndParameters("/ip/firewall/address-list/add",
                "list", "x", "address", "198.18.0.1").ExecuteScalar());
        }

        /// <summary>A terminal that answers every command with the same text.</summary>
        private sealed class AddAnsweringConnection : CliConnectionBase
        {
            private readonly string _reply;

            public AddAnsweringConnection(string reply) => _reply = reply;

            protected override string TransportName => "Scripted";

            public void OpenScripted()
                => OpenWith(_ => Task.FromResult(0), (cliText, ct) => Task.FromResult(_reply),
                    (raw, ct) => Task.FromResult(string.Empty), () => { });

            public override void Open(string host, string user, string password) => OpenScripted();
            public override void Open(string host, int port, string user, string password) => OpenScripted();
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
        }
    }
}
