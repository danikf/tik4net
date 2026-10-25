// CommandConnectionSetterTests.cs — ITikCommand.Connection refuses a connection the command cannot run on with
// ArgumentException, and a null with ArgumentNullException, on both command classes. The running check comes first
// (GenericCommandRunningGuardTests / ApiCommandRunningGuardTests pin that half).

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;
using tik4net.Connection;
using tik4net.Testing;

namespace tik4net.unittests.Connection
{
    [TestClass]
    public class CommandConnectionSetterTests
    {
        private sealed class MinimalTransport : TikCommandConnectionBase
        {
            public override void Open(string host, int port, string user, string password) => SetOpened();
            public override System.Threading.Tasks.Task OpenAsync(string host, int port, string user, string password,
                System.Threading.CancellationToken cancellationToken = default)
            {
                SetOpened();
                return System.Threading.Tasks.Task.CompletedTask;
            }
            public override void Close() => SetClosed();
            protected override IList<TikRecordSentence> RunPrint(TikCommandDescriptor descriptor) => new List<TikRecordSentence>();
            protected override string RunAdd(TikCommandDescriptor descriptor) => "*1";
            protected override void RunNonQuery(TikCommandDescriptor descriptor) { }
        }

        [TestMethod]
        public void AnApiCommandRefusesAConnectionThatIsNotTheBinaryApi()
        {
            using var api = new ApiConnection(false);
            var cmd = api.CreateCommand("/system/identity/print");

            var ex = Assert.ThrowsException<ArgumentException>(() => cmd.Connection = new TikFakeConnection());
            StringAssert.Contains(ex.Message, nameof(ApiConnection));
            Assert.AreSame(api, cmd.Connection, "a refused connection is not kept");
        }

        [TestMethod]
        public void AGenericCommandRefusesAConnectionThatIsNotACommandTransport()
        {
            var transport = new MinimalTransport();
            var cmd = transport.CreateCommand("/system/identity/print");

            var ex = Assert.ThrowsException<ArgumentException>(() => cmd.Connection = new TikFakeConnection());
            StringAssert.Contains(ex.Message, nameof(TikCommandConnectionBase));
            Assert.AreSame(transport, cmd.Connection, "a refused connection is not kept");
        }

        [TestMethod]
        public void BothCommandsRefuseANullConnection()
        {
            using var api = new ApiConnection(false);
            var apiCmd = api.CreateCommand("/system/identity/print");
            Assert.ThrowsException<ArgumentNullException>(() => apiCmd.Connection = null!);
            Assert.AreSame(api, apiCmd.Connection);

            var transport = new MinimalTransport();
            var genericCmd = transport.CreateCommand("/system/identity/print");
            Assert.ThrowsException<ArgumentNullException>(() => genericCmd.Connection = null!);
            Assert.AreSame(transport, genericCmd.Connection);
        }

        [TestMethod]
        public void TheSameKindOfConnectionIsAccepted()
        {
            using var api = new ApiConnection(false);
            using var other = new ApiConnection(false);
            var apiCmd = api.CreateCommand("/system/identity/print");
            apiCmd.Connection = other;
            Assert.AreSame(other, apiCmd.Connection);

            var genericCmd = new MinimalTransport().CreateCommand("/system/identity/print");
            var second = new MinimalTransport();
            genericCmd.Connection = second;
            Assert.AreSame(second, genericCmd.Connection);
        }
    }
}
