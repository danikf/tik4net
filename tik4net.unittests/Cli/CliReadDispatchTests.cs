#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// What a read method sends for each verb and parameter format. The parameters of a <c>print</c> are a query; those
    /// of every other verb are its inputs, and the read path hands both over in Filter format.
    /// </summary>
    [TestClass]
    public class CliReadDispatchTests
    {
        private sealed class RecordingRouter : ScriptedCliConnectionBase
        {
            internal readonly List<string> Sent = new List<string>();

            internal RecordingRouter()
            {
                CliFieldSeparator = null;
                OpenScripted(_ => Task.FromResult(0),
                    (line, ct) =>
                    {
                        Sent.Add(line);
                        return Task.FromResult(line.StartsWith(":local d [") ? CliCommandBuilder.CountMarker + "0/nil" : string.Empty);
                    },
                    (raw, ct) => Task.FromResult(string.Empty), () => { });
            }

            public override void Open(string host, string user, string password) { }
            public override void Open(string host, int port, string user, string password) { }
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) => Task.FromResult(0);
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) => Task.FromResult(0);
        }

        // The line that carries the read, past whatever the connection asks the router first (the flags probe).
        private static string FirstLineOfARead(string path, params string[] nameValues)
        {
            using var router = new RecordingRouter();
            var command = router.CreateCommandAndParameters(path, nameValues);
            try { command.ExecuteList(); }
            catch (TikCommandException) { /* the empty answer is not this test's subject */ }
            return router.Sent.Find(line => line.Contains(CliCommandBuilder.ApiPathToCli(path)))!;
        }

        private static string Expression(string countedRead)
            => countedRead.Substring(":local d [".Length, countedRead.IndexOf("]; ", System.StringComparison.Ordinal) - ":local d [".Length);

        [TestMethod]
        public void APrintsParametersAreAQuery()
            => Assert.AreEqual("/ip address print as-value where interface=ether1",
                Expression(FirstLineOfARead("/ip/address/print", "interface", "ether1")));

        [DataTestMethod]
        [DataRow("/ping", ":put [/ping address=127.0.0.1 count=1 as-value]", "address", "127.0.0.1")]
        [DataRow("/tool/traceroute", ":put [/tool traceroute address=127.0.0.1 count=1 as-value]", "address", "127.0.0.1")]
        [DataRow("/interface/monitor-traffic", ":put [/interface monitor-traffic interface=ether1 once as-value]", "interface", "ether1")]
        [DataRow("/tool/profile", ":put [/tool profile cpu=all duration=1 as-value]", "cpu", "all")]
        [DataRow("/console/inspect", ":put [/console inspect request=child as-value]", "request", "child")]
        [DataRow("/tool/wol", "/tool wol mac=\"AA:BB:CC:DD:EE:FF\"", "mac", "AA:BB:CC:DD:EE:FF")]
        public void AnotherVerbsParametersAreItsInputs(string path, string expected, string name, string value)
            => Assert.AreEqual(expected, FirstLineOfARead(path, name, value));

        [TestMethod]
        public void GetIsItsOwnShape()
            => Assert.AreEqual(":put [/system identity get value-name=name]",
                FirstLineOfARead("/system/identity/get", "value-name", "name"));
    }
}
