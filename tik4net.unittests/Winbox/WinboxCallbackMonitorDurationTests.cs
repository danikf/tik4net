// WinboxCallbackMonitorDurationTests.cs — the callback monitor honours duration= as the read method does (WinBox + MAC
// review, §11 F8).
//
// The API ends `/tool/torch duration=3` by itself with !done; the native read method keeps the window open that long
// and ends. The callback path dropped duration as an input (X4) but then ran until the caller cancelled.

#nullable enable
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    [DoNotParallelize]
    public class WinboxCallbackMonitorDurationTests
    {
        private const int StartCmd = 0xFE0012;
        private const int CancelCmd = 0xFE0013;

        // A torch window (a continuous query monitor) cut down to one input, refreshing every 100 ms.
        private static readonly string TorchWindow =
            "[{name:'Tools',c:[{name:'Torch',c:[{name:'Torch',title:'Torch',type:'query',path:[ 24,7 ],startcmd:" + StartCmd
            + ",cancelcmd:" + CancelCmd + ",autorefresh:100,c:[" +
            "{name:'Interface',type:'string',id:'s1',ro:1}]}]}]}]";

        [TestMethod]
        public void F8_TheCallbackMonitor_EndsByItself_WhenItsDurationIsOver()
        {
            using (var h = new NativeConnectionHarness(TorchWindow))
            {
                // The start answers a monitor id; every poll ends a pass WITHOUT Finished — a continuous window.
                h.Answer = req => NativeConnectionHarness.CommandOf(req) == StartCmd
                    ? NativeConnectionHarness.ReplyTo(req, M2Message.U32Sys(WinboxM2Protocol.RecordKey.Id, 3))
                    : NativeConnectionHarness.ReplyTo(req);

                ITikTrapSentence? trap = null;
                using (var done = new ManualResetEventSlim())
                {
                    var cmd = h.Connection.CreateCommand("/tool/torch",
                        new TikCommandParameter("interface", "ether1"), new TikCommandParameter("duration", "1s"));
                    var watch = Stopwatch.StartNew();
                    cmd.ExecuteWithCallback(_ => { }, error => { trap = error; done.Set(); }, () => done.Set());
                    bool ended = done.Wait(5000);
                    if (!ended) cmd.CancelAndJoin();
                    Assert.IsTrue(ended, "duration=1s must end the monitor without a cancel from the caller");
                    Assert.IsNull(trap, "the monitor must end, not fail: " + trap?.Message);
                    Assert.IsTrue(watch.Elapsed >= TimeSpan.FromMilliseconds(900),
                        "and not before its duration: ended after " + watch.ElapsedMilliseconds + " ms");
                }
                Assert.AreEqual(1, h.RequestsWithCommand(CancelCmd).Count, "the window is closed with its cancelcmd");
            }
        }
    }
}
