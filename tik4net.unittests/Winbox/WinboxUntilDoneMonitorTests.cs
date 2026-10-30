// WinboxUntilDoneMonitorTests.cs — ExecuteListUntilDone(timeout) on a never-ending monitor over WinBox native.
//
// The binary API reads `/tool/torch interface=…` until the timeout ends it with TikCommandAbortException
// (ToolTests.ExecuteListUntilDone_NeverEndingCommand_ThrowsAbortException). Native read a continuous window for one
// pass, as its plain read does, and returned that snapshot.

#nullable enable
using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    [DoNotParallelize]
    public class WinboxUntilDoneMonitorTests
    {
        private const int StartCmd = 0xFE0012;
        private const int CancelCmd = 0xFE0013;

        // A torch window (a continuous query monitor) cut down to one input, refreshing every 100 ms.
        private static readonly string TorchWindow =
            "[{name:'Tools',c:[{name:'Torch',c:[{name:'Torch',title:'Torch',type:'query',path:[ 24,7 ],startcmd:" + StartCmd
            + ",cancelcmd:" + CancelCmd + ",autorefresh:100,c:[" +
            "{name:'Interface',type:'string',id:'s1',ro:1}]}]}]}]";

        // The start answers a monitor id; every poll ends a pass WITHOUT Finished — a continuous window.
        private static NativeConnectionHarness Torch()
        {
            var h = new NativeConnectionHarness(TorchWindow);
            h.Answer = req => NativeConnectionHarness.CommandOf(req) == StartCmd
                ? NativeConnectionHarness.ReplyTo(req, M2Message.U32Sys(WinboxM2Protocol.RecordKey.Id, 3))
                : NativeConnectionHarness.ReplyTo(req);
            return h;
        }

        [TestMethod]
        public void UntilDone_OnAContinuousWindow_RunsUntilTheTimeoutAndAborts()
        {
            using (var h = Torch())
            {
                var cmd = h.Connection.CreateCommand("/tool/torch", new TikCommandParameter("interface", "ether1"));
                var watch = Stopwatch.StartNew();
                var ex = Assert.ThrowsException<TikCommandAbortException>(
                    () => cmd.ExecuteListUntilDone(TimeSpan.FromSeconds(1)).ToList());
                watch.Stop();

                StringAssert.Contains(ex.Message, "did not finish within 1 second(s)");
                Assert.IsTrue(watch.Elapsed >= TimeSpan.FromMilliseconds(900), "ended after " + watch.ElapsedMilliseconds + " ms");
                Assert.AreEqual(1, h.RequestsWithCommand(CancelCmd).Count, "the window is closed with its cancelcmd");
            }
        }

        [TestMethod]
        public void TheList_OfAContinuousWindow_IsStillOnePass()
        {
            using (var h = Torch())
            {
                var watch = Stopwatch.StartNew();
                h.Connection.CreateCommand("/tool/torch", new TikCommandParameter("interface", "ether1")).ExecuteList().ToList();
                Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(5), "a read of a continuous window is its snapshot");
                Assert.AreEqual(1, h.RequestsWithCommand(CancelCmd).Count);
            }
        }
    }
}
