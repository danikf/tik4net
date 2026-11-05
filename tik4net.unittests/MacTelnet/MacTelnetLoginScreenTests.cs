using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.MacTelnet;

namespace tik4net.unittests.MacTelnet
{
    /// <summary>
    /// The screen a MAC-Telnet login lands on after EC-SRP5 (review D2): the change-password nag is answered with
    /// Ctrl-C up to three times, as on the WinBox CLI. It used to be answered once; a nag that came back was waited on
    /// until the login timed out.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class MacTelnetLoginScreenTests
    {
        private const string Nag = "\r\nChange your password\r\nnew password> ";
        private const string Prompt = "\r\n[admin@MikroTik] > ";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

        [TestMethod]
        public async Task ANagThatComesBackTwice_IsAnsweredThreeTimes_ThenThePrompt()
        {
            Assert.AreEqual(3, await CtrlCsBeforeThePrompt(Nag, Nag, Nag, Prompt));
        }

        [TestMethod]
        public async Task ANagThatPersists_EndsTheLogin_AfterThreeCtrlCs()
        {
            var ex = await Assert.ThrowsExceptionAsync<TikConnectionLoginException>(
                () => CtrlCsBeforeThePrompt(Nag, Nag, Nag, Nag, Nag));
            StringAssert.Contains(ex.Message, "new password");
        }

        // Runs the client's prompt wait against a scripted router: the first screen, then the next one after each
        // Ctrl-C. Answers how many Ctrl-Cs the client sent.
        private static async Task<int> CtrlCsBeforeThePrompt(string first, params string[] afterEachCtrlC)
        {
            using (var router = new MacTelnetFakeRouter())
            {
                var client = router.Client;
                var screens = new Queue<string>(afterEachCtrlC);
                int ctrlCs = 0;
                var seen = new HashSet<uint>();

                var wait = Task.Run(() =>
                {
                    try { typeof(MacTelnetUdpClient).GetMethod("WaitForPromptSync", Flags).Invoke(client, null); }
                    catch (TargetInvocationException ex) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); }
                });
                router.SendData(first);

                while (!wait.IsCompleted)
                {
                    if (!router.TryReceive(200, out byte type, out uint at, out byte[] payload))
                        continue;
                    if (type == MacTelnetFakeRouter.PktData && payload.Length == 1 && payload[0] == 0x03 && seen.Add(at))   // a retransmit is not another Ctrl-C
                    {
                        ctrlCs++;
                        if (screens.Count > 0) router.SendData(screens.Dequeue());
                    }
                }
                await wait;
                return ctrlCs;
            }
        }
    }
}
