using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// <see cref="TikCancellationMode.AbandonAndClose"/> promises to abandon the in-flight read. This pins
    /// that the read a CLI transport actually performs can be told to stop.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>CliConnectionBase.TransportToken</c> hands the caller's token to the transport in that mode and
    /// closes the connection if the read throws — but three of the five CLI clients declared their response
    /// read <b>without a token parameter at all</b>, so the token was handed over and dropped on the floor.
    /// The mode then behaved exactly like <see cref="TikCancellationMode.Cooperative"/>, silently, and the
    /// caller waited out <c>ReceiveTimeout</c> having asked not to.
    /// </para>
    /// <para>
    /// <b>What this test is and is not.</b> It is a signature check, and a signature check cannot prove the
    /// token is honoured inside the loop — only that there is one to honour. That is deliberate: the
    /// behavioural test for this mode
    /// (<c>CliAsyncCommandTests.AbandonAndClose_CutsTheReadAndClosesTheConnection</c>) drives a fake whose
    /// reply hook is <c>Task.Delay(Timeout.Infinite, ct)</c> — a transport that honours the token <i>by
    /// construction</i> — so it stayed green through the whole defect and could not have done otherwise. A
    /// cheap check that can fail is worth more here than an expensive one that cannot.
    /// </para>
    /// </remarks>
    [TestClass]
    public class CliReadCancellationReachTests
    {
        /// <summary>
        /// The terminal clients, by assembly-qualified name so an internal type in a satellite package is
        /// reachable without a compile-time reference.
        /// </summary>
        private static readonly (string Assembly, string TypeName)[] CliClients =
        {
            ("tik4net", "tik4net.Telnet.TelnetClient"),
            ("tik4net", "tik4net.MacTelnet.MacTelnetUdpClient"),
            ("tik4net", "tik4net.WinboxCli.WinboxCliClient"),   // also serves WinboxCliMac
            ("tik4net.ssh", "tik4net.Ssh.SshShellClient"),
        };

        [TestMethod]
        public void EveryCliClientsResponseReadCanBeCancelled()
        {
            var offenders = new List<string>();
            var checkedTypes = new List<string>();

            foreach (var (assemblyName, typeName) in CliClients)
            {
                Assembly assembly;
                try
                {
                    assembly = Assembly.Load(assemblyName);
                }
                catch (Exception ex)
                {
                    Assert.Fail($"could not load {assemblyName} to check {typeName}: {ex.Message}");
                    return;
                }

                Type type = assembly.GetType(typeName, throwOnError: false);
                Assert.IsNotNull(type,
                    $"{typeName} not found in {assemblyName} — if the client was renamed, rename it here too, "
                    + "otherwise this test silently stops checking anything");

                MethodInfo[] reads = type
                    .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .Where(m => m.Name == "ReadCommandResponseAsync")
                    .ToArray();

                Assert.AreNotEqual(0, reads.Length,
                    $"{typeName} has no ReadCommandResponseAsync — the name this test keys on has moved");

                checkedTypes.Add(type.Name);

                foreach (MethodInfo read in reads)
                {
                    if (!read.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)))
                        offenders.Add(type.Name + "." + read.Name + " takes no CancellationToken");
                }
            }

            Assert.AreEqual(0, offenders.Count,
                "TikCancellationMode.AbandonAndClose cannot abandon a read that cannot be told to stop:"
                + Environment.NewLine + string.Join(Environment.NewLine, offenders)
                + Environment.NewLine + "(checked: " + string.Join(", ", checkedTypes) + ")");
        }
    }
}
