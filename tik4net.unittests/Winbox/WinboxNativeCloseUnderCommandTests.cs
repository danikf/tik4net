using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;
using tik4net.WinboxNative;

namespace tik4net.unittests.Winbox
{
    /// <summary>
    /// A WinBox-native command in flight when <c>Close()</c> runs fails as a <see cref="TikConnectionException"/>.
    /// </summary>
    /// <remarks>
    /// <c>Close</c> does not wait for a running command: it disposes the M2 multiplexer, which fails every pending request
    /// with <see cref="ObjectDisposedException"/>. The CLI and REST transports map that to
    /// <see cref="TikConnectionNotOpenException"/> (<c>ClosedWhileRunning</c>), as the <see cref="ITikConnection"/> docs
    /// promise, and the native transport has to do the same — a raw framework exception reads as a bug in the library
    /// rather than the race the caller started.
    /// </remarks>
    [TestClass]
    [DoNotParallelize]
    public class WinboxNativeCloseUnderCommandTests
    {
        private static WinboxNativeConnection OpenedOver(SilentChannel channel)
        {
            var connection = new WinboxNativeConnection();
            var ops = new WinboxNativeM2Operations(channel, 5000);
            void Set(string name, object value) => typeof(WinboxNativeConnection)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(connection, value);
            Set("_session", channel);
            Set("_ops", ops);
            typeof(WinboxNativeConnection).GetMethod("StartMultiplexer", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(connection, new object[] { channel });
            typeof(tik4net.Connection.TikConnectionBase)
                .GetMethod("SetOpened", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(connection, null);
            return connection;
        }

        [TestMethod]
        public void CloseUnderARunningSafeModeTake_IsTikConnectionNotOpenException()
        {
            var channel = new SilentChannel();
            var connection = OpenedOver(channel);

            var take = Task.Run(() => connection.SafeModeTake());
            Assert.IsTrue(channel.Sent.Wait(5000), "the request never went out");
            connection.Close();

            Exception caught = null;
            try { take.Wait(5000); } catch (AggregateException ex) { caught = ex.InnerException; }
            Assert.IsTrue(take.IsCompleted, "the take never returned");
            Assert.IsNotNull(caught, "a take whose connection was closed under it reported success");
            Assert.IsInstanceOfType(caught, typeof(TikConnectionNotOpenException),
                "a command in flight when Close() runs should fail as TikConnectionNotOpenException; got " + caught);
            Assert.IsFalse(connection.SafeModeGet(), "a take that never completed left Safe Mode marked as held");
        }

        private const string ScriptWindow =
            "[{name:'System',c:[{name:'Script',title:'Script',type:'map',path:[ 48,101 ],c:[" +
            "{name:'Name',type:'string',id:'s10006'}]}]}]";

        // A print and a write, through the whole native command path: the harness's catalog, then a real multiplexer on
        // a channel that never answers.
        [DataTestMethod]
        [DataRow("/system/script/print")]
        [DataRow("/system/script/set")]
        public void CloseUnderARunningCrudCommand_IsTikConnectionNotOpenException(string command)
        {
            var h = new NativeConnectionHarness(ScriptWindow);
            var channel = new SilentChannel();
            var connection = h.Connection;
            typeof(WinboxNativeConnection).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(connection, channel);
            typeof(WinboxNativeConnection).GetMethod("StartMultiplexer", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(connection, new object[] { channel });

            var run = Task.Run(() =>
            {
                if (command.EndsWith("/print", StringComparison.Ordinal))
                    connection.CreateCommand(command).ExecuteList();
                else
                    connection.CreateCommandAndParameters(command, ".id", "*1", "name", "x").ExecuteNonQuery();
            });
            Assert.IsTrue(channel.Sent.Wait(5000), "the request never went out");
            connection.Close();

            Exception caught = null;
            try { run.Wait(5000); } catch (AggregateException ex) { caught = ex.InnerException; }
            Assert.IsTrue(run.IsCompleted, "the command never returned");
            Assert.IsInstanceOfType(caught, typeof(TikConnectionNotOpenException),
                "a command in flight when Close() runs should fail as TikConnectionNotOpenException; got " + caught);
        }

        internal sealed class SilentChannel : IWinboxM2Channel
        {
            private readonly ManualResetEventSlim _closed = new ManualResetEventSlim(false);
            internal readonly ManualResetEventSlim Sent = new ManualResetEventSlim(false);
            public bool IsEncrypted => true;
            public bool DataAvailable => false;
            public long BytesReceived => 0;
            public bool SupportsStaleDrain => false;
            public bool SendAbandoned => false;
            public bool SendStalled => false;
            public void Open(string host, int port, string user, string password, int connectTimeoutMs, int ioTimeoutMs, int sendTimeoutMs = 0) { }
            public byte[] NextReqIdField() => throw new NotSupportedException();
            public void Send(byte[] m2) => Sent.Set();
            public byte[] Receive(int timeoutMs) => null;
            public byte[] SendReceive(byte[] m2, int timeoutMs) => null;
            public byte[] ReceiveNextFrame() { _closed.Wait(); return null; }
            public void StartIdleServicing() { }
            public void Dispose() => _closed.Set();
        }
    }
}
