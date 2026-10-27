// EmptyParameterNameTests.cs — a parameter with an empty name is refused when it is created, on every transport
// (command-layer C8). The binary API always refused it; the ten other transports built it and sent a nameless word.
// TikFakeConnection refuses it too, so a test against the fake cannot pass on a call every transport refuses.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;
using tik4net.Connection;
using tik4net.Testing;

namespace tik4net.unittests.Connection
{
    [TestClass]
    public class EmptyParameterNameTests
    {
        /// <summary>A transport that never runs anything: only the factory methods are under test.</summary>
        private sealed class InertConnection : TikCommandConnectionBase
        {
            public override TikConnectionCapability Capabilities => TikConnectionCapability.Crud;
            protected override IList<TikRecordSentence> RunPrint(TikCommandDescriptor descriptor) => throw new NotSupportedException();
            protected override string RunAdd(TikCommandDescriptor descriptor) => throw new NotSupportedException();
            protected override void RunNonQuery(TikCommandDescriptor descriptor) => throw new NotSupportedException();
            protected override string RunRawText(TikCommandDescriptor descriptor) => throw new NotSupportedException();
            public override void Open(string host, string user, string password) { }
            public override void Open(string host, int port, string user, string password) { }
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public override void Close() { }
        }

        private static void AssertRefused(Action call, string what)
        {
            var ex = Assert.ThrowsException<ArgumentException>(call, what);
            Assert.AreEqual("name", ex.ParamName, what);
        }

        private static IEnumerable<(string Transport, ITikConnection Connection)> Connections()
        {
            yield return ("generic", new InertConnection());
            yield return ("api", new ApiConnection(false));
            yield return ("fake", new TikFakeConnection().WithNonQuery(_ => true));
        }

        [TestMethod]
        public void CreateParameter_EmptyName_IsRefused()
        {
            foreach (var (transport, connection) in Connections())
            {
                AssertRefused(() => connection.CreateParameter("", "x"), transport + " CreateParameter(name, value)");
                AssertRefused(() => connection.CreateParameter("", "x", TikCommandParameterFormat.NameValue),
                    transport + " CreateParameter(name, value, format)");
            }
        }

        [TestMethod]
        public void AddParameter_EmptyName_IsRefused_AndNothingIsAdded()
        {
            foreach (var (transport, connection) in Connections())
            {
                var cmd = connection.CreateCommand("/ip/address/print");
                AssertRefused(() => cmd.AddParameter("", "x"), transport + " AddParameter(name, value)");
                AssertRefused(() => cmd.AddParameter("", "x", TikCommandParameterFormat.Filter),
                    transport + " AddParameter(name, value, format)");
                AssertRefused(() => cmd.AddParameterAndValues("", "x"), transport + " AddParameterAndValues");
                Assert.AreEqual(0, cmd.Parameters.Count, transport + ": nothing is added");
            }
        }

        [TestMethod]
        public void CreateCommandAndParameters_EmptyName_IsRefused()
        {
            foreach (var (transport, connection) in Connections())
                AssertRefused(() => connection.CreateCommandAndParameters("/ip/address/print", "", "x"),
                    transport + " CreateCommandAndParameters");
        }

        [TestMethod]
        public void ANamedParameter_StillBuilds_WithANullValue()
        {
            // A bare '?name' filter has a null value, not an empty name: it must keep working.
            foreach (var (transport, connection) in Connections())
            {
                var p = connection.CreateParameter("disabled", null, TikCommandParameterFormat.Filter);
                Assert.AreEqual("disabled", p.Name, transport);
                Assert.IsNull(p.Value, transport);
            }
        }
    }
}
